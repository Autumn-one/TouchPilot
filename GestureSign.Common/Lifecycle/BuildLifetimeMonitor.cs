using System;
using System.Threading;

namespace GestureSign.Common.Lifecycle
{
    public sealed class BuildLifetimeMonitor : IDisposable
    {
        private static readonly TimeSpan DefaultMaximumCheckInterval = TimeSpan.FromMinutes(1);

        private readonly Action _expirationAction;
        private readonly Func<DateTimeOffset, BuildLifetimeDecision> _evaluate;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly TimeSpan _maximumCheckInterval;
        private readonly Timer _timer;
        private int _started;
        private int _signaled;
        private int _disposed;

        public BuildLifetimeMonitor(Action expirationAction)
            : this(expirationAction, BuildLifetime.Evaluate, () => DateTimeOffset.UtcNow,
                DefaultMaximumCheckInterval)
        {
        }

        internal BuildLifetimeMonitor(Action expirationAction,
            Func<DateTimeOffset, BuildLifetimeDecision> evaluate, Func<DateTimeOffset> utcNow,
            TimeSpan maximumCheckInterval)
        {
            _expirationAction = expirationAction ?? throw new ArgumentNullException(nameof(expirationAction));
            _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _maximumCheckInterval = maximumCheckInterval > TimeSpan.Zero
                ? maximumCheckInterval
                : throw new ArgumentOutOfRangeException(nameof(maximumCheckInterval));
            _timer = new Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0 || Volatile.Read(ref _disposed) != 0)
                return;
            ScheduleNextCheck();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _timer.Dispose();
        }

        private void OnTimer(object state)
        {
            ScheduleNextCheck();
        }

        private void ScheduleNextCheck()
        {
            if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _signaled) != 0)
                return;

            DateTimeOffset now = _utcNow().ToUniversalTime();
            BuildLifetimeDecision decision = _evaluate(now);
            if (!decision.CanRun)
            {
                if (Interlocked.Exchange(ref _signaled, 1) == 0)
                    _expirationAction();
                return;
            }

            if (decision.Status == BuildLifetimeStatus.Unrestricted)
                return;

            TimeSpan remaining = decision.ExpiresAtUtc.Value - now;
            TimeSpan dueTime = remaining <= TimeSpan.Zero
                ? TimeSpan.Zero
                : remaining < _maximumCheckInterval ? remaining : _maximumCheckInterval;
            try
            {
                _timer.Change(dueTime, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
            {
            }
        }
    }
}

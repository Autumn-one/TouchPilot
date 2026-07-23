using GestureSign.Common.Lifecycle;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GestureSign.Tests
{
    public class BuildLifetimeTests
    {
        private static readonly DateTimeOffset BuiltAt =
            new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero);

        [Fact]
        public void MissingBuildMetadataLeavesDevelopmentBuildUnrestricted()
        {
            BuildLifetimeDecision decision = BuildLifetime.Evaluate(null, null, BuiltAt);

            Assert.Equal(BuildLifetimeStatus.Unrestricted, decision.Status);
            Assert.True(decision.CanRun);
        }

        [Fact]
        public void BuildExpiresAtExactThreeCalendarMonthBoundary()
        {
            DateTimeOffset expiresAt = BuiltAt.AddMonths(3);
            string builtAtTicks = BuiltAt.Ticks.ToString(CultureInfo.InvariantCulture);
            string expiresAtTicks = expiresAt.Ticks.ToString(CultureInfo.InvariantCulture);

            BuildLifetimeDecision before = BuildLifetime.Evaluate(builtAtTicks, expiresAtTicks,
                expiresAt.AddTicks(-1));
            BuildLifetimeDecision atBoundary = BuildLifetime.Evaluate(builtAtTicks, expiresAtTicks,
                expiresAt);

            Assert.Equal(BuildLifetimeStatus.Active, before.Status);
            Assert.True(before.CanRun);
            Assert.Equal(BuildLifetimeStatus.Expired, atBoundary.Status);
            Assert.False(atBoundary.CanRun);
        }

        [Theory]
        [InlineData(null, "1")]
        [InlineData("1", null)]
        [InlineData("not-ticks", "2")]
        public void IncompleteOrMalformedMetadataFailsClosed(string builtAtTicks, string expiresAtTicks)
        {
            BuildLifetimeDecision decision = BuildLifetime.Evaluate(builtAtTicks, expiresAtTicks, BuiltAt);

            Assert.Equal(BuildLifetimeStatus.Invalid, decision.Status);
            Assert.False(decision.CanRun);
        }

        [Fact]
        public void ExpiryOtherThanThreeCalendarMonthsFailsClosed()
        {
            BuildLifetimeDecision decision = BuildLifetime.Evaluate(
                BuiltAt.Ticks.ToString(CultureInfo.InvariantCulture),
                BuiltAt.AddDays(90).Ticks.ToString(CultureInfo.InvariantCulture), BuiltAt);

            Assert.Equal(BuildLifetimeStatus.Invalid, decision.Status);
            Assert.False(decision.CanRun);
        }

        [Fact]
        public async Task MonitorSignalsOnceWhenBuildCrossesExpiry()
        {
            DateTimeOffset expiresAt = BuiltAt.AddMonths(3);
            long nowTicks = BuiltAt.Ticks;
            int signals = 0;
            var signaled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            BuildLifetimeDecision Evaluate(DateTimeOffset value) => BuildLifetime.Evaluate(
                BuiltAt.Ticks.ToString(CultureInfo.InvariantCulture),
                expiresAt.Ticks.ToString(CultureInfo.InvariantCulture), value);
            using var monitor = new BuildLifetimeMonitor(() =>
            {
                if (Interlocked.Increment(ref signals) == 1)
                    signaled.TrySetResult(true);
            }, Evaluate, () => new DateTimeOffset(Interlocked.Read(ref nowTicks), TimeSpan.Zero),
                TimeSpan.FromMilliseconds(10));

            monitor.Start();
            Interlocked.Exchange(ref nowTicks, expiresAt.Ticks);
            await signaled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(30);

            Assert.Equal(1, Volatile.Read(ref signals));
        }

        [Fact]
        public void EmbeddedBuildMetadataMatchesBuildPropertiesWhenRequested()
        {
            string expectedBuiltAt = Environment.GetEnvironmentVariable(
                "TOUCHPILOT_EXPECT_BUILT_AT_TICKS");
            string expectedExpiresAt = Environment.GetEnvironmentVariable(
                "TOUCHPILOT_EXPECT_EXPIRES_AT_TICKS");
            if (string.IsNullOrWhiteSpace(expectedBuiltAt) || string.IsNullOrWhiteSpace(expectedExpiresAt))
            {
                Assert.Equal(BuildLifetimeStatus.Unrestricted,
                    BuildLifetime.Evaluate(BuiltAt).Status);
                return;
            }

            long builtAtTicks = long.Parse(expectedBuiltAt, CultureInfo.InvariantCulture);
            long expiresAtTicks = long.Parse(expectedExpiresAt, CultureInfo.InvariantCulture);
            var now = new DateTimeOffset(builtAtTicks, TimeSpan.Zero).AddTicks(1);

            BuildLifetimeDecision decision = BuildLifetime.Evaluate(now);

            Assert.Equal(BuildLifetimeStatus.Active, decision.Status);
            Assert.Equal(builtAtTicks, decision.BuiltAtUtc.Value.Ticks);
            Assert.Equal(expiresAtTicks, decision.ExpiresAtUtc.Value.Ticks);
        }
    }
}

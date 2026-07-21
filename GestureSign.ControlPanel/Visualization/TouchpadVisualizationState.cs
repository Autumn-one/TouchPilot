using GestureSign.Common.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.ControlPanel.Visualization
{
    internal readonly struct TouchpadTracePoint
    {
        public TouchpadTracePoint(long timestampMilliseconds, double normalizedX, double normalizedY)
        {
            TimestampMilliseconds = timestampMilliseconds;
            NormalizedX = normalizedX;
            NormalizedY = normalizedY;
        }

        public long TimestampMilliseconds { get; }
        public double NormalizedX { get; }
        public double NormalizedY { get; }
    }

    internal sealed class TouchpadContactTrace
    {
        private readonly List<TouchpadTracePoint> _points = new List<TouchpadTracePoint>();

        public TouchpadContactTrace(int contactIdentifier)
        {
            ContactIdentifier = contactIdentifier;
        }

        public int ContactIdentifier { get; }
        public bool IsActive { get; private set; }
        public long ReleasedAtMilliseconds { get; private set; }
        public IReadOnlyList<TouchpadTracePoint> Points => _points;
        public TouchpadTracePoint LastPoint => _points[_points.Count - 1];

        public void AddPoint(TouchpadTracePoint point, int maximumPointCount)
        {
            if (!IsActive)
                _points.Clear();

            IsActive = true;
            ReleasedAtMilliseconds = 0;
            _points.Add(point);
            int excess = _points.Count - maximumPointCount;
            if (excess > 0)
                _points.RemoveRange(0, excess);
        }

        public void Release(long timestampMilliseconds)
        {
            if (!IsActive)
                return;

            IsActive = false;
            ReleasedAtMilliseconds = timestampMilliseconds;
        }

        public void PrunePointsBefore(long timestampMilliseconds)
        {
            int removeCount = 0;
            while (removeCount < _points.Count &&
                   _points[removeCount].TimestampMilliseconds < timestampMilliseconds)
            {
                removeCount++;
            }

            if (removeCount > 0)
                _points.RemoveRange(0, removeCount);
        }
    }

    internal sealed class TouchpadVisualizationState
    {
        internal const int MaximumTrailPointCount = 40;
        internal const int TrailLifetimeMilliseconds = 650;
        private readonly Dictionary<int, TouchpadContactTrace> _traces =
            new Dictionary<int, TouchpadContactTrace>();

        public IReadOnlyCollection<TouchpadContactTrace> Traces => _traces.Values;
        public int ActiveContactCount => _traces.Values.Count(trace => trace.IsActive);

        public void ApplyFrame(TouchpadVisualizationFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var activeIdentifiers = new HashSet<int>();
            foreach (TouchpadContact contact in frame.Contacts)
            {
                if (!contact.IsActive)
                {
                    if (_traces.TryGetValue(contact.ContactIdentifier, out TouchpadContactTrace releasedTrace))
                        releasedTrace.Release(frame.TimestampMilliseconds);
                    continue;
                }

                activeIdentifiers.Add(contact.ContactIdentifier);
                if (!_traces.TryGetValue(contact.ContactIdentifier, out TouchpadContactTrace trace))
                {
                    trace = new TouchpadContactTrace(contact.ContactIdentifier);
                    _traces.Add(contact.ContactIdentifier, trace);
                }

                trace.AddPoint(new TouchpadTracePoint(frame.TimestampMilliseconds,
                    contact.NormalizedX, contact.NormalizedY), MaximumTrailPointCount);
            }

            foreach (TouchpadContactTrace trace in _traces.Values)
            {
                if (trace.IsActive && !activeIdentifiers.Contains(trace.ContactIdentifier))
                    trace.Release(frame.TimestampMilliseconds);
            }

            Advance(frame.TimestampMilliseconds);
        }

        public void Advance(long timestampMilliseconds)
        {
            long oldestPointTimestamp = timestampMilliseconds - TrailLifetimeMilliseconds;
            foreach (TouchpadContactTrace trace in _traces.Values)
                trace.PrunePointsBefore(oldestPointTimestamp);

            int[] expiredIdentifiers = _traces
                .Where(pair => !pair.Value.IsActive &&
                               (pair.Value.Points.Count == 0 ||
                                timestampMilliseconds - pair.Value.ReleasedAtMilliseconds >=
                                TrailLifetimeMilliseconds))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (int identifier in expiredIdentifiers)
                _traces.Remove(identifier);
        }

        public void Clear()
        {
            _traces.Clear();
        }
    }
}

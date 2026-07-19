using System;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Common.Input
{
    public enum FixedEdgeGesture
    {
        None = 0,
        LeftSwipeIn,
        LeftSlideUp,
        LeftSlideDown,
        RightSwipeIn,
        RightSlideUp,
        RightSlideDown,
        TopSwipeIn,
        TopSlideLeft,
        TopSlideRight,
        BottomSwipeIn,
        BottomSlideLeft,
        BottomSlideRight,
        TwoFingerLeftSwipeIn,
        TwoFingerLeftSlideUp,
        TwoFingerLeftSlideDown,
        TwoFingerRightSwipeIn,
        TwoFingerRightSlideUp,
        TwoFingerRightSlideDown,
        TwoFingerTopSwipeIn,
        TwoFingerTopSlideLeft,
        TwoFingerTopSlideRight,
        TwoFingerBottomSwipeIn,
        TwoFingerBottomSlideLeft,
        TwoFingerBottomSlideRight
    }

    public enum TouchpadWindowDragMode
    {
        Disabled = 0,
        BottomEdgeAnchor
    }

    public enum TouchpadWindowDragImplementation
    {
        DirectSetWindowPos = 0,
        SimulatedCaptionDrag
    }

    public sealed class TouchpadInteractionOptions
    {
        public bool EdgeGesturesEnabled { get; set; }
        public ISet<FixedEdgeGesture> EnabledEdgeGestures { get; set; } = new HashSet<FixedEdgeGesture>();
        public TouchpadWindowDragMode WindowDragMode { get; set; }
        public double EdgeZone { get; set; } = 0.12;
        public double EdgeActivationDistance { get; set; } = 0.08;
        public double EdgeSlideStep { get; set; } = 0.05;
        public int EdgeGestureTimeoutMilliseconds { get; set; } = 800;
        public int BottomAnchorHoldMilliseconds { get; set; } = 100;
        public int AnchorDropoutGraceMilliseconds { get; set; } = 80;
        public double WindowDragActivationDistance { get; set; } = 0.015;
    }

    public enum TouchpadInteractionEventType
    {
        EdgeGesture,
        WindowDragStarted,
        WindowDragMoved,
        WindowDragPaused,
        WindowDragResumed,
        WindowDragEnded
    }

    public readonly struct TouchpadInteractionEvent
    {
        private TouchpadInteractionEvent(TouchpadInteractionEventType eventType, FixedEdgeGesture edgeGesture, int contactIdentifier, double normalizedX, double normalizedY)
        {
            EventType = eventType;
            EdgeGesture = edgeGesture;
            ContactIdentifier = contactIdentifier;
            NormalizedX = normalizedX;
            NormalizedY = normalizedY;
        }

        public TouchpadInteractionEventType EventType { get; }
        public FixedEdgeGesture EdgeGesture { get; }
        public int ContactIdentifier { get; }
        public double NormalizedX { get; }
        public double NormalizedY { get; }

        public static TouchpadInteractionEvent Edge(FixedEdgeGesture gesture)
        {
            return new TouchpadInteractionEvent(TouchpadInteractionEventType.EdgeGesture, gesture, 0, 0, 0);
        }

        public static TouchpadInteractionEvent Window(TouchpadInteractionEventType eventType, TouchpadContact contact)
        {
            return new TouchpadInteractionEvent(eventType, FixedEdgeGesture.None, contact.ContactIdentifier, contact.NormalizedX, contact.NormalizedY);
        }

        public static TouchpadInteractionEvent WindowEnded()
        {
            return new TouchpadInteractionEvent(TouchpadInteractionEventType.WindowDragEnded, FixedEdgeGesture.None, 0, 0, 0);
        }
    }

    public sealed class TouchpadInteractionFrameResult
    {
        internal TouchpadInteractionFrameResult(bool claimInput, bool sessionActive, IReadOnlyList<TouchpadInteractionEvent> events)
        {
            ClaimInput = claimInput;
            SessionActive = sessionActive;
            Events = events;
        }

        public bool ClaimInput { get; }
        public bool SessionActive { get; }
        public IReadOnlyList<TouchpadInteractionEvent> Events { get; }
    }

    public sealed class TouchpadInteractionRecognizer
    {
        private enum TouchpadEdge
        {
            Left,
            Right,
            Top,
            Bottom
        }

        private enum EdgeTrackingMode
        {
            Candidate,
            Slide,
            Completed
        }

        private static readonly IReadOnlyList<TouchpadInteractionEvent> NoEvents = Array.Empty<TouchpadInteractionEvent>();
        private const int TwoFingerGestureOffset = 12;

        private readonly TouchpadInteractionOptions _options;
        private readonly Dictionary<int, TouchpadContact> _activeContacts = new Dictionary<int, TouchpadContact>();
        private readonly Dictionary<int, TouchpadContact> _contactStarts = new Dictionary<int, TouchpadContact>();
        private readonly List<int> _edgeContactIdentifiers = new List<int>(2);
        private readonly Dictionary<int, TouchpadContact> _edgeContactStarts = new Dictionary<int, TouchpadContact>(2);

        private bool _sessionActive;
        private bool _claimed;
        private bool _windowDragActive;
        private long _sessionStartTimestamp;

        private int? _anchorContactIdentifier;
        private TouchpadContact _lastAnchorContact;
        private long _anchorStartTimestamp;
        private long? _anchorMissingSinceTimestamp;
        private int? _movingContactIdentifier;

        private TouchpadEdge _edge;
        private EdgeTrackingMode _edgeTrackingMode;
        private double _edgeLastAlongDisplacement;
        private int _edgeObservedContactCount;
        private bool _edgeCandidateClosed;

        public TouchpadInteractionRecognizer(TouchpadInteractionOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (_options.EdgeZone <= 0 || _options.EdgeZone >= 0.5)
                throw new ArgumentOutOfRangeException(nameof(options.EdgeZone));
            if (_options.EdgeActivationDistance <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.EdgeActivationDistance));
            if (_options.EdgeSlideStep <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.EdgeSlideStep));
            if (_options.AnchorDropoutGraceMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(options.AnchorDropoutGraceMilliseconds));
            if (_options.WindowDragActivationDistance <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.WindowDragActivationDistance));
        }

        public bool SessionActive => _sessionActive;

        public TouchpadInteractionFrameResult ProcessFrame(IReadOnlyList<TouchpadContact> contacts, long timestampMilliseconds)
        {
            if (contacts == null)
                throw new ArgumentNullException(nameof(contacts));

            bool claimedAtFrameStart = _claimed;
            UpdateActiveContacts(contacts);

            if (!_sessionActive && _activeContacts.Count != 0)
                BeginSession(contacts, timestampMilliseconds);
            else if (_sessionActive)
                RecordNewContacts();

            if (_activeContacts.Count == 0)
            {
                List<TouchpadInteractionEvent> endingEvents = null;
                if (_windowDragActive)
                    endingEvents = new List<TouchpadInteractionEvent> { TouchpadInteractionEvent.WindowEnded() };

                bool claimEndingFrame = claimedAtFrameStart || _claimed;
                Reset();
                return new TouchpadInteractionFrameResult(claimEndingFrame, false, endingEvents ?? NoEvents);
            }

            var output = new List<TouchpadInteractionEvent>();
            if (_windowDragActive)
            {
                ProcessActiveWindowDrag(timestampMilliseconds, output);
            }
            else if (!_claimed)
            {
                ProcessEdgeCandidate(timestampMilliseconds, output);
                if (!_claimed)
                    TryActivateWindowDrag(timestampMilliseconds, output);
            }
            else if (_edgeTrackingMode == EdgeTrackingMode.Slide)
            {
                ProcessClaimedEdgeSlide(output);
            }

            return new TouchpadInteractionFrameResult(claimedAtFrameStart || _claimed, true, output.Count == 0 ? NoEvents : output);
        }

        public void Reset()
        {
            _activeContacts.Clear();
            _contactStarts.Clear();
            _sessionActive = false;
            _claimed = false;
            _windowDragActive = false;
            _sessionStartTimestamp = 0;
            _anchorContactIdentifier = null;
            _anchorMissingSinceTimestamp = null;
            _movingContactIdentifier = null;
            _edgeContactIdentifiers.Clear();
            _edgeContactStarts.Clear();
            _edgeTrackingMode = EdgeTrackingMode.Candidate;
            _edgeLastAlongDisplacement = 0;
            _edgeObservedContactCount = 0;
            _edgeCandidateClosed = false;
        }

        private void UpdateActiveContacts(IReadOnlyList<TouchpadContact> contacts)
        {
            _activeContacts.Clear();
            foreach (TouchpadContact contact in contacts)
            {
                if (contact.IsActive)
                    _activeContacts[contact.ContactIdentifier] = contact;
            }
        }

        private void RecordNewContacts()
        {
            foreach (KeyValuePair<int, TouchpadContact> contact in _activeContacts)
            {
                if (!_contactStarts.ContainsKey(contact.Key))
                    _contactStarts.Add(contact.Key, contact.Value);
            }
        }

        private void BeginSession(IReadOnlyList<TouchpadContact> contacts, long timestampMilliseconds)
        {
            _sessionActive = true;
            _sessionStartTimestamp = timestampMilliseconds;
            RecordNewContacts();

            List<TouchpadContact> activeInFrameOrder = contacts.Where(contact => contact.IsActive).ToList();
            ConfigureAnchorCandidate(activeInFrameOrder, timestampMilliseconds);
            BeginEdgeCandidate(activeInFrameOrder);
        }

        private void ConfigureAnchorCandidate(List<TouchpadContact> contacts, long timestampMilliseconds)
        {
            if (_options.WindowDragMode != TouchpadWindowDragMode.BottomEdgeAnchor)
                return;

            TouchpadContact? anchor = null;
            foreach (TouchpadContact contact in contacts.OrderByDescending(contact => contact.NormalizedY))
            {
                if (IsInBottomEdgeZone(contact))
                {
                    anchor = contact;
                    break;
                }
            }

            if (!anchor.HasValue)
                return;

            _anchorContactIdentifier = anchor.Value.ContactIdentifier;
            _lastAnchorContact = anchor.Value;
            _anchorStartTimestamp = timestampMilliseconds;
            _anchorMissingSinceTimestamp = null;
        }

        private void BeginEdgeCandidate(List<TouchpadContact> contacts)
        {
            if (!_options.EdgeGesturesEnabled || _options.EnabledEdgeGestures.Count == 0)
            {
                _edgeCandidateClosed = true;
                return;
            }

            _edgeObservedContactCount = contacts.Count;
            if (contacts.Count == 0 || contacts.Count > 2)
            {
                _edgeCandidateClosed = true;
                return;
            }

            if (!TryConfigureEdgeCandidate(contacts) && contacts.Count == 2)
                _edgeCandidateClosed = true;
        }

        private bool TryConfigureEdgeCandidate(IReadOnlyList<TouchpadContact> contacts)
        {
            TouchpadEdge edge;
            if (!TryGetClosestEnabledEdge(contacts, contacts.Count, out edge))
                return false;

            _edgeContactIdentifiers.Clear();
            _edgeContactStarts.Clear();
            foreach (TouchpadContact contact in contacts)
            {
                _edgeContactIdentifiers.Add(contact.ContactIdentifier);
                _edgeContactStarts[contact.ContactIdentifier] = contact;
            }

            _edge = edge;
            _edgeTrackingMode = EdgeTrackingMode.Candidate;
            _edgeLastAlongDisplacement = 0;
            return true;
        }

        private void TryActivateWindowDrag(long timestampMilliseconds, List<TouchpadInteractionEvent> output)
        {
            if (!_anchorContactIdentifier.HasValue)
                return;

            TouchpadContact anchor;
            if (!_activeContacts.TryGetValue(_anchorContactIdentifier.Value, out anchor) || !IsInBottomEdgeZone(anchor))
            {
                _anchorContactIdentifier = null;
                _movingContactIdentifier = null;
                return;
            }
            _lastAnchorContact = anchor;

            if (timestampMilliseconds - _anchorStartTimestamp < _options.BottomAnchorHoldMilliseconds)
                return;

            if (!_movingContactIdentifier.HasValue)
            {
                TouchpadContact moving;
                if (!TryGetNonAnchorContact(out moving))
                    return;
                _movingContactIdentifier = moving.ContactIdentifier;
            }

            TouchpadContact currentMoving;
            TouchpadContact movingStart;
            if (!_activeContacts.TryGetValue(_movingContactIdentifier.Value, out currentMoving) ||
                !_contactStarts.TryGetValue(_movingContactIdentifier.Value, out movingStart) ||
                GetDistance(currentMoving, movingStart) < _options.WindowDragActivationDistance)
                return;

            _claimed = true;
            _windowDragActive = true;
            _anchorMissingSinceTimestamp = null;
            CloseEdgeCandidate();
            output.Add(TouchpadInteractionEvent.Window(TouchpadInteractionEventType.WindowDragStarted, currentMoving));
        }

        private void ProcessActiveWindowDrag(long timestampMilliseconds, List<TouchpadInteractionEvent> output)
        {
            TouchpadContact anchor;
            if (!TryMaintainActiveAnchor(timestampMilliseconds, out anchor))
            {
                _windowDragActive = false;
                _movingContactIdentifier = null;
                output.Add(TouchpadInteractionEvent.WindowEnded());
                return;
            }

            TouchpadContact moving;
            if (_movingContactIdentifier.HasValue && _activeContacts.TryGetValue(_movingContactIdentifier.Value, out moving))
            {
                output.Add(TouchpadInteractionEvent.Window(TouchpadInteractionEventType.WindowDragMoved, moving));
                return;
            }

            if (_movingContactIdentifier.HasValue)
            {
                _movingContactIdentifier = null;
                output.Add(TouchpadInteractionEvent.Window(TouchpadInteractionEventType.WindowDragPaused, anchor));
            }

            TouchpadContact replacement;
            if (!TryGetNonAnchorContact(out replacement))
                return;

            _movingContactIdentifier = replacement.ContactIdentifier;
            output.Add(TouchpadInteractionEvent.Window(TouchpadInteractionEventType.WindowDragResumed, replacement));
        }

        private bool TryMaintainActiveAnchor(long timestampMilliseconds, out TouchpadContact anchor)
        {
            TouchpadContact current;
            if (_anchorContactIdentifier.HasValue &&
                _activeContacts.TryGetValue(_anchorContactIdentifier.Value, out current))
            {
                if (!IsInBottomEdgeZone(current))
                {
                    anchor = current;
                    return false;
                }

                _lastAnchorContact = current;
                _anchorMissingSinceTimestamp = null;
                anchor = current;
                return true;
            }

            TouchpadContact replacement;
            if (TryGetReplacementAnchor(out replacement))
            {
                _anchorContactIdentifier = replacement.ContactIdentifier;
                _lastAnchorContact = replacement;
                _anchorMissingSinceTimestamp = null;
                anchor = replacement;
                return true;
            }

            if (!_anchorMissingSinceTimestamp.HasValue)
                _anchorMissingSinceTimestamp = timestampMilliseconds;

            anchor = _lastAnchorContact;
            return timestampMilliseconds - _anchorMissingSinceTimestamp.Value <= _options.AnchorDropoutGraceMilliseconds;
        }

        private bool TryGetReplacementAnchor(out TouchpadContact contact)
        {
            foreach (KeyValuePair<int, TouchpadContact> candidate in _activeContacts)
            {
                if ((!_movingContactIdentifier.HasValue || candidate.Key != _movingContactIdentifier.Value) &&
                    IsInBottomEdgeZone(candidate.Value))
                {
                    contact = candidate.Value;
                    return true;
                }
            }

            contact = default(TouchpadContact);
            return false;
        }

        private bool TryGetNonAnchorContact(out TouchpadContact contact)
        {
            foreach (KeyValuePair<int, TouchpadContact> candidate in _activeContacts)
            {
                if (!_anchorContactIdentifier.HasValue || candidate.Key != _anchorContactIdentifier.Value)
                {
                    contact = candidate.Value;
                    return true;
                }
            }

            contact = default(TouchpadContact);
            return false;
        }

        private bool IsInBottomEdgeZone(TouchpadContact contact)
        {
            return contact.NormalizedY >= 1 - _options.EdgeZone;
        }

        private void ProcessEdgeCandidate(long timestampMilliseconds, List<TouchpadInteractionEvent> output)
        {
            if (_edgeCandidateClosed)
                return;

            if (timestampMilliseconds - _sessionStartTimestamp > _options.EdgeGestureTimeoutMilliseconds ||
                _activeContacts.Count > 2)
            {
                CloseEdgeCandidate();
                return;
            }

            if (_activeContacts.Count > _edgeObservedContactCount)
            {
                _edgeObservedContactCount = _activeContacts.Count;
                if (_activeContacts.Count == 2 &&
                    TryConfigureEdgeCandidate(_activeContacts.Values.ToList()))
                    return;

                CloseEdgeCandidate();
                return;
            }

            if (_activeContacts.Count != _edgeObservedContactCount || _edgeContactIdentifiers.Count == 0)
            {
                if (_activeContacts.Count != _edgeObservedContactCount)
                    CloseEdgeCandidate();
                return;
            }

            List<TouchpadContact> currentContacts;
            if (!TryGetTrackedEdgeContacts(out currentContacts))
            {
                CloseEdgeCandidate();
                return;
            }

            int fingerCount = _edgeContactIdentifiers.Count;
            double inward = GetMinimumInwardDisplacement(currentContacts);
            double along = GetConsistentAlongDisplacement(currentContacts);
            double averageAlong = GetAverageAlongDisplacement(currentContacts);
            FixedEdgeGesture inwardGesture = GetInwardGesture(_edge, fingerCount);
            FixedEdgeGesture alongGesture = GetAlongGesture(_edge, along, fingerCount);
            bool inwardEnabled = _options.EnabledEdgeGestures.Contains(inwardGesture);
            bool alongEnabled = alongGesture != FixedEdgeGesture.None && _options.EnabledEdgeGestures.Contains(alongGesture);

            double inwardProgress = inwardEnabled && inward > 0 ? inward / _options.EdgeActivationDistance : 0;
            double alongProgress = alongEnabled ? Math.Abs(along) / _options.EdgeSlideStep : 0;

            if (inwardProgress >= 1 && inwardProgress >= alongProgress && Math.Abs(averageAlong) <= inward * 1.5)
            {
                _claimed = true;
                _edgeCandidateClosed = true;
                _edgeTrackingMode = EdgeTrackingMode.Completed;
                output.Add(TouchpadInteractionEvent.Edge(inwardGesture));
            }
            else if (alongProgress >= 1 && alongProgress > inwardProgress)
            {
                _claimed = true;
                _edgeCandidateClosed = true;
                _edgeTrackingMode = EdgeTrackingMode.Slide;
                EmitEdgeSlideSteps(currentContacts, output);
            }
        }

        private void ProcessClaimedEdgeSlide(List<TouchpadInteractionEvent> output)
        {
            if (_activeContacts.Count != _edgeContactIdentifiers.Count)
                return;

            List<TouchpadContact> currentContacts;
            if (TryGetTrackedEdgeContacts(out currentContacts))
                EmitEdgeSlideSteps(currentContacts, output);
        }

        private void EmitEdgeSlideSteps(IReadOnlyList<TouchpadContact> currentContacts, List<TouchpadInteractionEvent> output)
        {
            double currentAlongDisplacement = GetConsistentAlongDisplacement(currentContacts);
            double delta = currentAlongDisplacement - _edgeLastAlongDisplacement;
            if (Math.Abs(delta) < _options.EdgeSlideStep)
                return;

            int direction = Math.Sign(delta);
            FixedEdgeGesture gesture = GetAlongGesture(_edge, direction, _edgeContactIdentifiers.Count);
            if (!_options.EnabledEdgeGestures.Contains(gesture))
                return;

            int steps = Math.Min(4, (int)(Math.Abs(delta) / _options.EdgeSlideStep));
            for (int i = 0; i < steps; i++)
                output.Add(TouchpadInteractionEvent.Edge(gesture));
            _edgeLastAlongDisplacement += direction * steps * _options.EdgeSlideStep;
        }

        private bool TryGetClosestEnabledEdge(IReadOnlyList<TouchpadContact> contacts, int fingerCount, out TouchpadEdge edge)
        {
            var candidates = new List<KeyValuePair<TouchpadEdge, double>>();
            AddEdgeCandidate(candidates, TouchpadEdge.Left, contacts, fingerCount);
            AddEdgeCandidate(candidates, TouchpadEdge.Right, contacts, fingerCount);
            AddEdgeCandidate(candidates, TouchpadEdge.Top, contacts, fingerCount);
            AddEdgeCandidate(candidates, TouchpadEdge.Bottom, contacts, fingerCount);

            if (candidates.Count == 0)
            {
                edge = default(TouchpadEdge);
                return false;
            }

            edge = candidates.OrderBy(candidate => candidate.Value).First().Key;
            return true;
        }

        private void AddEdgeCandidate(List<KeyValuePair<TouchpadEdge, double>> candidates, TouchpadEdge edge,
            IReadOnlyList<TouchpadContact> contacts, int fingerCount)
        {
            if (!HasEnabledGesture(edge, fingerCount))
                return;

            double maximumDistance = contacts.Max(contact => GetEdgeDistance(edge, contact));
            if (maximumDistance <= _options.EdgeZone)
            {
                double averageDistance = contacts.Average(contact => GetEdgeDistance(edge, contact));
                candidates.Add(new KeyValuePair<TouchpadEdge, double>(edge, averageDistance));
            }
        }

        private bool HasEnabledGesture(TouchpadEdge edge, int fingerCount)
        {
            return _options.EnabledEdgeGestures.Contains(GetInwardGesture(edge, fingerCount)) ||
                   _options.EnabledEdgeGestures.Contains(GetAlongGesture(edge, -1, fingerCount)) ||
                   _options.EnabledEdgeGestures.Contains(GetAlongGesture(edge, 1, fingerCount));
        }

        private static FixedEdgeGesture GetInwardGesture(TouchpadEdge edge, int fingerCount)
        {
            FixedEdgeGesture gesture;
            switch (edge)
            {
                case TouchpadEdge.Left: gesture = FixedEdgeGesture.LeftSwipeIn; break;
                case TouchpadEdge.Right: gesture = FixedEdgeGesture.RightSwipeIn; break;
                case TouchpadEdge.Top: gesture = FixedEdgeGesture.TopSwipeIn; break;
                case TouchpadEdge.Bottom: gesture = FixedEdgeGesture.BottomSwipeIn; break;
                default: gesture = FixedEdgeGesture.None; break;
            }
            return ForFingerCount(gesture, fingerCount);
        }

        private static FixedEdgeGesture GetAlongGesture(TouchpadEdge edge, double direction, int fingerCount)
        {
            if (direction == 0)
                return FixedEdgeGesture.None;

            FixedEdgeGesture gesture;
            switch (edge)
            {
                case TouchpadEdge.Left:
                    gesture = direction < 0 ? FixedEdgeGesture.LeftSlideUp : FixedEdgeGesture.LeftSlideDown;
                    break;
                case TouchpadEdge.Right:
                    gesture = direction < 0 ? FixedEdgeGesture.RightSlideUp : FixedEdgeGesture.RightSlideDown;
                    break;
                case TouchpadEdge.Top:
                    gesture = direction < 0 ? FixedEdgeGesture.TopSlideLeft : FixedEdgeGesture.TopSlideRight;
                    break;
                case TouchpadEdge.Bottom:
                    gesture = direction < 0 ? FixedEdgeGesture.BottomSlideLeft : FixedEdgeGesture.BottomSlideRight;
                    break;
                default:
                    gesture = FixedEdgeGesture.None;
                    break;
            }
            return ForFingerCount(gesture, fingerCount);
        }

        private static FixedEdgeGesture ForFingerCount(FixedEdgeGesture oneFingerGesture, int fingerCount)
        {
            return fingerCount == 2 && oneFingerGesture != FixedEdgeGesture.None
                ? (FixedEdgeGesture)((int)oneFingerGesture + TwoFingerGestureOffset)
                : oneFingerGesture;
        }

        private bool TryGetTrackedEdgeContacts(out List<TouchpadContact> contacts)
        {
            contacts = new List<TouchpadContact>(_edgeContactIdentifiers.Count);
            foreach (int identifier in _edgeContactIdentifiers)
            {
                TouchpadContact contact;
                if (!_activeContacts.TryGetValue(identifier, out contact))
                    return false;
                contacts.Add(contact);
            }
            return true;
        }

        private double GetMinimumInwardDisplacement(IReadOnlyList<TouchpadContact> currentContacts)
        {
            double minimum = double.MaxValue;
            foreach (TouchpadContact current in currentContacts)
                minimum = Math.Min(minimum, GetInwardDisplacement(_edge, _edgeContactStarts[current.ContactIdentifier], current));
            return minimum == double.MaxValue ? 0 : minimum;
        }

        private double GetConsistentAlongDisplacement(IReadOnlyList<TouchpadContact> currentContacts)
        {
            double average = GetAverageAlongDisplacement(currentContacts);
            int direction = Math.Sign(average);
            if (direction == 0)
                return 0;

            double minimum = double.MaxValue;
            foreach (TouchpadContact current in currentContacts)
            {
                TouchpadContact start = _edgeContactStarts[current.ContactIdentifier];
                double displacement = GetAlongPosition(_edge, current) - GetAlongPosition(_edge, start);
                if (Math.Sign(displacement) != direction)
                    return 0;
                minimum = Math.Min(minimum, Math.Abs(displacement));
            }

            return direction * (minimum == double.MaxValue ? 0 : minimum);
        }

        private double GetAverageAlongDisplacement(IReadOnlyList<TouchpadContact> currentContacts)
        {
            double total = 0;
            foreach (TouchpadContact current in currentContacts)
            {
                TouchpadContact start = _edgeContactStarts[current.ContactIdentifier];
                total += GetAlongPosition(_edge, current) - GetAlongPosition(_edge, start);
            }
            return currentContacts.Count == 0 ? 0 : total / currentContacts.Count;
        }

        private static double GetEdgeDistance(TouchpadEdge edge, TouchpadContact contact)
        {
            switch (edge)
            {
                case TouchpadEdge.Left: return contact.NormalizedX;
                case TouchpadEdge.Right: return 1 - contact.NormalizedX;
                case TouchpadEdge.Top: return contact.NormalizedY;
                case TouchpadEdge.Bottom: return 1 - contact.NormalizedY;
                default: return 1;
            }
        }

        private void CloseEdgeCandidate()
        {
            _edgeContactIdentifiers.Clear();
            _edgeContactStarts.Clear();
            _edgeCandidateClosed = true;
        }

        private static double GetInwardDisplacement(TouchpadEdge edge, TouchpadContact start, TouchpadContact current)
        {
            switch (edge)
            {
                case TouchpadEdge.Left: return current.NormalizedX - start.NormalizedX;
                case TouchpadEdge.Right: return start.NormalizedX - current.NormalizedX;
                case TouchpadEdge.Top: return current.NormalizedY - start.NormalizedY;
                case TouchpadEdge.Bottom: return start.NormalizedY - current.NormalizedY;
                default: return 0;
            }
        }

        private static double GetAlongPosition(TouchpadEdge edge, TouchpadContact contact)
        {
            return edge == TouchpadEdge.Left || edge == TouchpadEdge.Right ? contact.NormalizedY : contact.NormalizedX;
        }

        private static double GetDistance(TouchpadContact first, TouchpadContact second)
        {
            double x = first.NormalizedX - second.NormalizedX;
            double y = first.NormalizedY - second.NormalizedY;
            return Math.Sqrt(x * x + y * y);
        }
    }
}

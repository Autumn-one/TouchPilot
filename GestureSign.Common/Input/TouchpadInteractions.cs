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
        TwoFingerBottomSlideRight,
        ThreeFingerLeftSwipeIn,
        ThreeFingerLeftSlideUp,
        ThreeFingerLeftSlideDown,
        ThreeFingerRightSwipeIn,
        ThreeFingerRightSlideUp,
        ThreeFingerRightSlideDown,
        ThreeFingerTopSwipeIn,
        ThreeFingerTopSlideLeft,
        ThreeFingerTopSlideRight,
        ThreeFingerBottomSwipeIn,
        ThreeFingerBottomSlideLeft,
        ThreeFingerBottomSlideRight
    }

    public enum TouchpadWindowDragMode
    {
        Disabled = 0,
        BottomEdgeAnchor = 1,
        ThreeFingerDrag = 3
    }

    public enum TouchpadWindowDragImplementation
    {
        DirectSetWindowPos = 0,
        SimulatedMouseDrag = 1,
        ThreeFingerDrag = 2,
        NativeMoveLoop = 3,
        [Obsolete("Use SimulatedMouseDrag.")]
        SimulatedCaptionDrag = SimulatedMouseDrag
    }

    public sealed class TouchpadInteractionOptions
    {
        public bool EdgeGesturesEnabled { get; set; }
        public ISet<FixedEdgeGesture> EnabledEdgeGestures { get; set; } = new HashSet<FixedEdgeGesture>();
        public TouchpadWindowDragMode WindowDragMode { get; set; }
        public double EdgeZone { get; set; } = 0.12;
        public double? LeftEdgeZone { get; set; }
        public double? RightEdgeZone { get; set; }
        public double? TopEdgeZone { get; set; }
        public double? BottomEdgeZone { get; set; }
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
        internal TouchpadInteractionFrameResult(bool claimInput, bool sessionActive,
            IReadOnlyList<TouchpadInteractionEvent> events,
            bool bottomAnchoredWindowDragCandidateStarted,
            bool bottomAnchoredWindowDragCandidateEnded)
        {
            ClaimInput = claimInput;
            SessionActive = sessionActive;
            Events = events;
            BottomAnchoredWindowDragCandidateStarted = bottomAnchoredWindowDragCandidateStarted;
            BottomAnchoredWindowDragCandidateEnded = bottomAnchoredWindowDragCandidateEnded;
        }

        public bool ClaimInput { get; }
        public bool SessionActive { get; }
        public IReadOnlyList<TouchpadInteractionEvent> Events { get; }
        public bool BottomAnchoredWindowDragCandidateStarted { get; }
        public bool BottomAnchoredWindowDragCandidateEnded { get; }
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
        private const int GesturesPerFingerCount = 12;
        private const int MaximumEdgeFingerCount = 3;

        private readonly TouchpadInteractionOptions _options;
        private readonly Dictionary<int, TouchpadContact> _activeContacts = new Dictionary<int, TouchpadContact>();
        private readonly Dictionary<int, TouchpadContact> _releasedContacts = new Dictionary<int, TouchpadContact>();
        private readonly Dictionary<int, TouchpadContact> _contactStarts = new Dictionary<int, TouchpadContact>();
        private readonly List<int> _edgeContactIdentifiers = new List<int>(MaximumEdgeFingerCount);
        private readonly Dictionary<int, TouchpadContact> _edgeContactStarts = new Dictionary<int, TouchpadContact>(MaximumEdgeFingerCount);

        private bool _sessionActive;
        private bool _claimed;
        private bool _windowDragActive;
        private bool _windowDragMotionPaused;
        private bool _bottomDragReactivationPending;
        private bool _bottomDragCandidatePrepared;
        private int _bottomDragCandidateAnchorIdentifier;
        private int _bottomDragCandidateMovingIdentifier;
        private bool _bottomDragCandidateStartedThisFrame;
        private bool _bottomDragCandidateEndedThisFrame;
        private long _sessionStartTimestamp;

        private int? _anchorContactIdentifier;
        private TouchpadContact _lastAnchorContact;
        private long _anchorStartTimestamp;
        private long? _anchorMissingSinceTimestamp;
        private int? _movingContactIdentifier;

        private readonly List<int> _threeFingerContactIdentifiers = new List<int>(3);
        private bool _threeFingerTracking;
        private bool _threeFingerBaselineValid;
        private TouchpadContact _threeFingerBaseline;
        private TouchpadContact _lastThreeFingerCentroid;

        private TouchpadEdge _edge;
        private EdgeTrackingMode _edgeTrackingMode;
        private double _edgeLastAlongDisplacement;
        private int _edgeObservedContactCount;
        private bool _edgeCandidateClosed;
        private bool _claimedThreeFingerEdgeCandidate;

        public TouchpadInteractionRecognizer(TouchpadInteractionOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateEdgeZone(_options.EdgeZone, nameof(options.EdgeZone));
            ValidateEdgeZone(_options.LeftEdgeZone, nameof(options.LeftEdgeZone));
            ValidateEdgeZone(_options.RightEdgeZone, nameof(options.RightEdgeZone));
            ValidateEdgeZone(_options.TopEdgeZone, nameof(options.TopEdgeZone));
            ValidateEdgeZone(_options.BottomEdgeZone, nameof(options.BottomEdgeZone));
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

            _bottomDragCandidateStartedThisFrame = false;
            _bottomDragCandidateEndedThisFrame = false;
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

                ClearBottomDragCandidate();
                bool candidateStarted = _bottomDragCandidateStartedThisFrame;
                bool candidateEnded = _bottomDragCandidateEndedThisFrame;
                bool claimEndingFrame = claimedAtFrameStart || _claimed;
                Reset();
                return new TouchpadInteractionFrameResult(claimEndingFrame, false, endingEvents ?? NoEvents,
                    candidateStarted, candidateEnded);
            }

            var output = new List<TouchpadInteractionEvent>();
            if (_claimedThreeFingerEdgeCandidate)
            {
                ProcessClaimedThreeFingerEdgeCandidate(timestampMilliseconds, output);
            }
            else if (_windowDragActive)
            {
                if (_options.WindowDragMode == TouchpadWindowDragMode.ThreeFingerDrag)
                    ProcessThreeFingerWindowDrag(output);
                else
                    ProcessActiveWindowDrag(timestampMilliseconds, output);
            }
            else if (_options.WindowDragMode == TouchpadWindowDragMode.ThreeFingerDrag &&
                     (_threeFingerTracking || (!_claimed && _activeContacts.Count >= 3)))
            {
                if (_threeFingerTracking || !TryClaimThreeFingerEdgeCandidate())
                    ProcessThreeFingerWindowDrag(output);
            }
            else if (ShouldProcessBottomDragCandidate())
            {
                ProcessBottomDragCandidate(timestampMilliseconds, output);
            }
            else if (!_claimed)
            {
                ProcessEdgeCandidate(timestampMilliseconds, output);
                if (!_claimed)
                {
                    if (!_anchorContactIdentifier.HasValue)
                        ConfigureAnchorCandidate(_activeContacts.Values.ToList(), timestampMilliseconds);
                    TryActivateWindowDrag(timestampMilliseconds, output);
                }
            }
            else if (_edgeTrackingMode == EdgeTrackingMode.Slide)
            {
                ProcessClaimedEdgeSlide(output);
            }

            return new TouchpadInteractionFrameResult(claimedAtFrameStart || _claimed, true,
                output.Count == 0 ? NoEvents : output,
                _bottomDragCandidateStartedThisFrame, _bottomDragCandidateEndedThisFrame);
        }

        public void Reset()
        {
            _activeContacts.Clear();
            _releasedContacts.Clear();
            _contactStarts.Clear();
            _sessionActive = false;
            _claimed = false;
            _windowDragActive = false;
            _windowDragMotionPaused = false;
            _bottomDragReactivationPending = false;
            _bottomDragCandidatePrepared = false;
            _bottomDragCandidateAnchorIdentifier = 0;
            _bottomDragCandidateMovingIdentifier = 0;
            _bottomDragCandidateStartedThisFrame = false;
            _bottomDragCandidateEndedThisFrame = false;
            _sessionStartTimestamp = 0;
            _anchorContactIdentifier = null;
            _anchorMissingSinceTimestamp = null;
            _movingContactIdentifier = null;
            _threeFingerContactIdentifiers.Clear();
            _threeFingerTracking = false;
            _threeFingerBaselineValid = false;
            _threeFingerBaseline = default(TouchpadContact);
            _lastThreeFingerCentroid = default(TouchpadContact);
            _edgeContactIdentifiers.Clear();
            _edgeContactStarts.Clear();
            _edgeTrackingMode = EdgeTrackingMode.Candidate;
            _edgeLastAlongDisplacement = 0;
            _edgeObservedContactCount = 0;
            _edgeCandidateClosed = false;
            _claimedThreeFingerEdgeCandidate = false;
        }

        private void UpdateActiveContacts(IReadOnlyList<TouchpadContact> contacts)
        {
            _activeContacts.Clear();
            _releasedContacts.Clear();
            foreach (TouchpadContact contact in contacts)
            {
                if (contact.IsActive)
                    _activeContacts[contact.ContactIdentifier] = contact;
                else
                {
                    _releasedContacts[contact.ContactIdentifier] = contact;
                    _contactStarts.Remove(contact.ContactIdentifier);
                }
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

        private void ConfigureAnchorCandidate(List<TouchpadContact> contacts, long timestampMilliseconds,
            bool resetMovingContactStarts = false)
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
            if (resetMovingContactStarts)
            {
                foreach (TouchpadContact contact in contacts)
                {
                    if (contact.ContactIdentifier != anchor.Value.ContactIdentifier)
                        _contactStarts[contact.ContactIdentifier] = contact;
                }
            }
        }

        private bool ShouldProcessBottomDragCandidate()
        {
            return _options.WindowDragMode == TouchpadWindowDragMode.BottomEdgeAnchor &&
                   (!_claimed || _bottomDragReactivationPending);
        }

        private void ProcessBottomDragCandidate(long timestampMilliseconds,
            List<TouchpadInteractionEvent> output)
        {
            ResetReleasedBottomDragCandidateContacts();
            if (!_claimed)
            {
                ProcessEdgeCandidate(timestampMilliseconds, output);
                if (_claimed)
                {
                    ClearBottomDragCandidate();
                    return;
                }
            }

            if (!_anchorContactIdentifier.HasValue)
                ConfigureAnchorCandidate(_activeContacts.Values.ToList(), timestampMilliseconds,
                    _bottomDragReactivationPending);
            TryActivateWindowDrag(timestampMilliseconds, output);
        }

        private void ResetReleasedBottomDragCandidateContacts()
        {
            if (_anchorContactIdentifier.HasValue &&
                _releasedContacts.ContainsKey(_anchorContactIdentifier.Value))
            {
                _anchorContactIdentifier = null;
                _anchorMissingSinceTimestamp = null;
                _movingContactIdentifier = null;
            }
            else if (_movingContactIdentifier.HasValue &&
                     _releasedContacts.ContainsKey(_movingContactIdentifier.Value))
            {
                _movingContactIdentifier = null;
            }
        }

        private void ProcessThreeFingerWindowDrag(List<TouchpadInteractionEvent> output)
        {
            if (!_threeFingerTracking)
            {
                _threeFingerTracking = true;
                _claimed = true;
                CloseEdgeCandidate();
            }

            if (_activeContacts.Count != 3)
            {
                _threeFingerContactIdentifiers.Clear();
                _threeFingerBaselineValid = false;
                if (_windowDragActive && !_windowDragMotionPaused)
                {
                    _windowDragMotionPaused = true;
                    output.Add(TouchpadInteractionEvent.Window(
                        TouchpadInteractionEventType.WindowDragPaused,
                        _lastThreeFingerCentroid));
                }
                return;
            }

            List<TouchpadContact> contacts = _activeContacts.Values
                .OrderBy(contact => contact.ContactIdentifier)
                .ToList();
            TouchpadContact centroid = GetCentroid(contacts);
            bool sameContacts = _threeFingerBaselineValid &&
                                contacts.Select(contact => contact.ContactIdentifier)
                                    .SequenceEqual(_threeFingerContactIdentifiers);
            if (!sameContacts)
            {
                bool rebaseActiveDrag = _windowDragActive;
                if (rebaseActiveDrag && !_windowDragMotionPaused)
                {
                    output.Add(TouchpadInteractionEvent.Window(
                        TouchpadInteractionEventType.WindowDragPaused,
                        _lastThreeFingerCentroid));
                }

                ConfigureThreeFingerBaseline(contacts, centroid);
                if (rebaseActiveDrag)
                {
                    _windowDragMotionPaused = false;
                    output.Add(TouchpadInteractionEvent.Window(
                        TouchpadInteractionEventType.WindowDragResumed,
                        centroid));
                }
                return;
            }

            _lastThreeFingerCentroid = centroid;
            if (!_windowDragActive)
            {
                if (GetDistance(centroid, _threeFingerBaseline) < _options.WindowDragActivationDistance)
                    return;

                _windowDragActive = true;
                _windowDragMotionPaused = false;
                output.Add(TouchpadInteractionEvent.Window(
                    TouchpadInteractionEventType.WindowDragStarted,
                    centroid));
                return;
            }

            TouchpadInteractionEventType eventType = _windowDragMotionPaused
                ? TouchpadInteractionEventType.WindowDragResumed
                : TouchpadInteractionEventType.WindowDragMoved;
            _windowDragMotionPaused = false;
            output.Add(TouchpadInteractionEvent.Window(eventType, centroid));
        }

        private void ConfigureThreeFingerBaseline(IReadOnlyList<TouchpadContact> contacts, TouchpadContact centroid)
        {
            _threeFingerContactIdentifiers.Clear();
            _threeFingerContactIdentifiers.AddRange(contacts.Select(contact => contact.ContactIdentifier));
            _threeFingerBaseline = centroid;
            _lastThreeFingerCentroid = centroid;
            _threeFingerBaselineValid = true;
        }

        private static TouchpadContact GetCentroid(IReadOnlyList<TouchpadContact> contacts)
        {
            double normalizedX = contacts.Average(contact => contact.NormalizedX);
            double normalizedY = contacts.Average(contact => contact.NormalizedY);
            return new TouchpadContact(contacts[0].ContactIdentifier, DeviceStates.Tip, normalizedX, normalizedY);
        }

        private bool TryClaimThreeFingerEdgeCandidate()
        {
            if (!_options.EdgeGesturesEnabled || _activeContacts.Count != MaximumEdgeFingerCount ||
                !TryConfigureEdgeCandidate(_activeContacts.Values.ToList()))
                return false;

            _edgeObservedContactCount = MaximumEdgeFingerCount;
            _edgeCandidateClosed = false;
            _claimed = true;
            _claimedThreeFingerEdgeCandidate = true;
            return true;
        }

        private void ProcessClaimedThreeFingerEdgeCandidate(long timestampMilliseconds,
            List<TouchpadInteractionEvent> output)
        {
            ProcessEdgeCandidate(timestampMilliseconds, output);
            if (_edgeTrackingMode != EdgeTrackingMode.Candidate)
            {
                _claimedThreeFingerEdgeCandidate = false;
                return;
            }

            if (!_edgeCandidateClosed)
                return;

            _claimedThreeFingerEdgeCandidate = false;
            ProcessThreeFingerWindowDrag(output);
        }

        private void BeginEdgeCandidate(List<TouchpadContact> contacts)
        {
            if (!_options.EdgeGesturesEnabled || _options.EnabledEdgeGestures.Count == 0)
            {
                _edgeCandidateClosed = true;
                return;
            }

            _edgeObservedContactCount = contacts.Count;
            if (contacts.Count == 0 || contacts.Count > MaximumEdgeFingerCount)
            {
                _edgeCandidateClosed = true;
                return;
            }

            if (!TryConfigureEdgeCandidate(contacts) && contacts.Count == MaximumEdgeFingerCount)
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
            {
                ClearBottomDragCandidate();
                return;
            }

            TouchpadContact anchor;
            if (!_activeContacts.TryGetValue(_anchorContactIdentifier.Value, out anchor) || !IsInBottomEdgeZone(anchor))
            {
                _anchorContactIdentifier = null;
                _movingContactIdentifier = null;
                ClearBottomDragCandidate();
                return;
            }
            _lastAnchorContact = anchor;

            TouchpadContact moving;
            if (_movingContactIdentifier.HasValue)
            {
                if (!_activeContacts.TryGetValue(_movingContactIdentifier.Value, out moving))
                {
                    ClearBottomDragCandidate();
                    return;
                }
            }
            else if (!TryGetNonAnchorContact(out moving))
            {
                ClearBottomDragCandidate();
                return;
            }

            PrepareBottomDragCandidate(anchor.ContactIdentifier, moving.ContactIdentifier);

            if (timestampMilliseconds - _anchorStartTimestamp < _options.BottomAnchorHoldMilliseconds)
                return;

            if (!_movingContactIdentifier.HasValue)
                _movingContactIdentifier = moving.ContactIdentifier;

            TouchpadContact currentMoving;
            TouchpadContact movingStart;
            if (!_activeContacts.TryGetValue(_movingContactIdentifier.Value, out currentMoving) ||
                !_contactStarts.TryGetValue(_movingContactIdentifier.Value, out movingStart) ||
                GetDistance(currentMoving, movingStart) < _options.WindowDragActivationDistance)
                return;

            _claimed = true;
            _windowDragActive = true;
            _windowDragMotionPaused = false;
            _bottomDragReactivationPending = false;
            _anchorMissingSinceTimestamp = null;
            CloseEdgeCandidate();
            ConsumeBottomDragCandidate();
            output.Add(TouchpadInteractionEvent.Window(TouchpadInteractionEventType.WindowDragStarted, currentMoving));
        }

        private void PrepareBottomDragCandidate(int anchorIdentifier, int movingIdentifier)
        {
            if (_bottomDragCandidatePrepared &&
                _bottomDragCandidateAnchorIdentifier == anchorIdentifier &&
                _bottomDragCandidateMovingIdentifier == movingIdentifier)
                return;

            if (_bottomDragCandidatePrepared)
                _bottomDragCandidateEndedThisFrame = true;

            _bottomDragCandidatePrepared = true;
            _bottomDragCandidateAnchorIdentifier = anchorIdentifier;
            _bottomDragCandidateMovingIdentifier = movingIdentifier;
            _bottomDragCandidateStartedThisFrame = true;
        }

        private void ClearBottomDragCandidate()
        {
            if (!_bottomDragCandidatePrepared)
                return;

            _bottomDragCandidatePrepared = false;
            _bottomDragCandidateAnchorIdentifier = 0;
            _bottomDragCandidateMovingIdentifier = 0;
            _bottomDragCandidateEndedThisFrame = true;
        }

        private void ConsumeBottomDragCandidate()
        {
            _bottomDragCandidatePrepared = false;
            _bottomDragCandidateAnchorIdentifier = 0;
            _bottomDragCandidateMovingIdentifier = 0;
        }

        private void ProcessActiveWindowDrag(long timestampMilliseconds, List<TouchpadInteractionEvent> output)
        {
            if (TrackedBottomDragContactWasReleased())
            {
                EndBottomDragForReactivation(output);
                return;
            }

            if (_activeContacts.Count < 2)
            {
                PauseActiveWindowDrag(output);
                return;
            }

            TouchpadContact anchor;
            bool anchorAllowsMovement;
            bool anchorRequiresRebase;
            if (!TryMaintainActiveAnchor(timestampMilliseconds, out anchor, out anchorAllowsMovement, out anchorRequiresRebase))
            {
                _windowDragActive = false;
                _windowDragMotionPaused = false;
                _movingContactIdentifier = null;
                output.Add(TouchpadInteractionEvent.WindowEnded());
                return;
            }

            TouchpadContact moving = default(TouchpadContact);
            bool movingAvailable = _movingContactIdentifier.HasValue &&
                                   _activeContacts.TryGetValue(_movingContactIdentifier.Value, out moving);
            if (!movingAvailable)
            {
                _movingContactIdentifier = null;
                TouchpadContact replacement;
                if (TryGetNonAnchorContact(out replacement))
                {
                    _movingContactIdentifier = replacement.ContactIdentifier;
                    moving = replacement;
                    movingAvailable = true;
                }
            }

            if (anchorRequiresRebase)
                _windowDragMotionPaused = true;

            if (anchorAllowsMovement && movingAvailable)
            {
                TouchpadInteractionEventType eventType = _windowDragMotionPaused
                    ? TouchpadInteractionEventType.WindowDragResumed
                    : TouchpadInteractionEventType.WindowDragMoved;
                _windowDragMotionPaused = false;
                output.Add(TouchpadInteractionEvent.Window(eventType, moving));
                return;
            }

            PauseActiveWindowDrag(output, anchor);
        }

        private void PauseActiveWindowDrag(List<TouchpadInteractionEvent> output,
            TouchpadContact contact = default(TouchpadContact))
        {
            if (_windowDragMotionPaused)
                return;

            if (contact.ContactIdentifier == 0 && _activeContacts.Count != 0)
                contact = _activeContacts.Values.First();
            _windowDragMotionPaused = true;
            output.Add(TouchpadInteractionEvent.Window(TouchpadInteractionEventType.WindowDragPaused, contact));
        }

        private bool TrackedBottomDragContactWasReleased()
        {
            return (_anchorContactIdentifier.HasValue &&
                    _releasedContacts.ContainsKey(_anchorContactIdentifier.Value)) ||
                   (_movingContactIdentifier.HasValue &&
                    _releasedContacts.ContainsKey(_movingContactIdentifier.Value));
        }

        private void EndBottomDragForReactivation(List<TouchpadInteractionEvent> output)
        {
            _windowDragActive = false;
            _windowDragMotionPaused = false;
            _bottomDragReactivationPending = true;
            _anchorContactIdentifier = null;
            _anchorMissingSinceTimestamp = null;
            _movingContactIdentifier = null;
            output.Add(TouchpadInteractionEvent.WindowEnded());
        }

        private bool TryMaintainActiveAnchor(long timestampMilliseconds, out TouchpadContact anchor,
            out bool movementAllowed, out bool requiresRebase)
        {
            requiresRebase = _anchorMissingSinceTimestamp.HasValue &&
                             timestampMilliseconds - _anchorMissingSinceTimestamp.Value >
                             _options.AnchorDropoutGraceMilliseconds;

            TouchpadContact current;
            if (_anchorContactIdentifier.HasValue &&
                _activeContacts.TryGetValue(_anchorContactIdentifier.Value, out current))
            {
                _lastAnchorContact = current;
                _anchorMissingSinceTimestamp = null;
                anchor = current;
                movementAllowed = true;
                return true;
            }

            TouchpadContact replacement;
            if (TryGetReplacementAnchor(out replacement))
            {
                _anchorContactIdentifier = replacement.ContactIdentifier;
                _lastAnchorContact = replacement;
                _anchorMissingSinceTimestamp = null;
                anchor = replacement;
                movementAllowed = true;
                return true;
            }

            if (!_anchorMissingSinceTimestamp.HasValue)
                _anchorMissingSinceTimestamp = timestampMilliseconds;

            anchor = _lastAnchorContact;
            movementAllowed = timestampMilliseconds - _anchorMissingSinceTimestamp.Value <=
                              _options.AnchorDropoutGraceMilliseconds;
            requiresRebase = false;
            return true;
        }

        private bool TryGetReplacementAnchor(out TouchpadContact contact)
        {
            foreach (KeyValuePair<int, TouchpadContact> candidate in _activeContacts)
            {
                if (!_movingContactIdentifier.HasValue || candidate.Key != _movingContactIdentifier.Value)
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
            return contact.NormalizedY >= 1 - GetEdgeZone(TouchpadEdge.Bottom);
        }

        private void ProcessEdgeCandidate(long timestampMilliseconds, List<TouchpadInteractionEvent> output)
        {
            if (_edgeCandidateClosed)
                return;

            if (timestampMilliseconds - _sessionStartTimestamp > _options.EdgeGestureTimeoutMilliseconds ||
                _activeContacts.Count > MaximumEdgeFingerCount)
            {
                CloseEdgeCandidate();
                return;
            }

            if (_activeContacts.Count > _edgeObservedContactCount)
            {
                _edgeObservedContactCount = _activeContacts.Count;
                if (TryConfigureEdgeCandidate(_activeContacts.Values.ToList()))
                    return;

                _edgeContactIdentifiers.Clear();
                _edgeContactStarts.Clear();
                if (_activeContacts.Count == MaximumEdgeFingerCount)
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
            if (maximumDistance <= GetEdgeZone(edge))
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
            if (direction == 0 || fingerCount != 1)
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
            if (oneFingerGesture == FixedEdgeGesture.None || fingerCount < 1 || fingerCount > MaximumEdgeFingerCount)
                return FixedEdgeGesture.None;

            return (FixedEdgeGesture)((int)oneFingerGesture +
                                      (fingerCount - 1) * GesturesPerFingerCount);
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

        private double GetEdgeZone(TouchpadEdge edge)
        {
            switch (edge)
            {
                case TouchpadEdge.Left: return _options.LeftEdgeZone ?? _options.EdgeZone;
                case TouchpadEdge.Right: return _options.RightEdgeZone ?? _options.EdgeZone;
                case TouchpadEdge.Top: return _options.TopEdgeZone ?? _options.EdgeZone;
                case TouchpadEdge.Bottom: return _options.BottomEdgeZone ?? _options.EdgeZone;
                default: return _options.EdgeZone;
            }
        }

        private static void ValidateEdgeZone(double? edgeZone, string parameterName)
        {
            if (edgeZone.HasValue)
                ValidateEdgeZone(edgeZone.Value, parameterName);
        }

        private static void ValidateEdgeZone(double edgeZone, string parameterName)
        {
            if (edgeZone < 0 || edgeZone >= 0.5)
                throw new ArgumentOutOfRangeException(parameterName);
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

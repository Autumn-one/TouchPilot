using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using ManagedWinapi.Windows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GestureSign.Daemon.Triggers
{
    internal sealed class TouchpadInteractionTrigger : Trigger
    {
        private TouchpadInteractionRecognizer _recognizer;
        private readonly WindowDragDiagnostics _windowDragDiagnostics;
        private readonly WindowDragController _windowDragController;
        private readonly BottomAnchoredWindowDragTargetLock _windowDragTargetLock =
            new BottomAnchoredWindowDragTargetLock();
        private readonly TouchpadWheelSuppressor _wheelSuppressor = new TouchpadWheelSuppressor();
        private readonly ITouchpadContactFilter _confidenceFilter;
        private Point _sessionStartPoint;
        private TouchpadWindowDragImplementation _sessionWindowDragImplementation;
        private bool _sessionWindowDragBringToFront;

        public TouchpadInteractionTrigger() : this(new TouchpadConfidenceContactFilter())
        {
        }

        internal TouchpadInteractionTrigger(ITouchpadContactFilter confidenceFilter)
        {
            _confidenceFilter = confidenceFilter ?? throw new ArgumentNullException(nameof(confidenceFilter));
            _windowDragDiagnostics = new WindowDragDiagnostics(WindowDragDiagnosticWriter.Instance);
            _windowDragController = new WindowDragController(_windowDragDiagnostics);
            _sessionWindowDragImplementation = AppConfig.TouchpadWindowDragImplementation;
            _sessionWindowDragBringToFront = AppConfig.TouchpadWindowDragBringToFront;
            _recognizer = CreateRecognizer(_sessionWindowDragImplementation);
            PointCapture.Instance.TouchpadFrame += PointCapture_TouchpadFrame;
        }

        private void PointCapture_TouchpadFrame(object sender, TouchpadFrameEventArgs e)
        {
            bool normalMode = PointCapture.Instance.Mode == CaptureMode.Normal;
            Point frameCursorPosition = Cursor.Position;
            IReadOnlyList<TouchpadContact> contacts = AppConfig.TouchpadEdgeConfidenceFilteringEnabled
                ? _confidenceFilter.Filter(e.Contacts)
                : e.Contacts;
            _windowDragDiagnostics.ObserveTouchpadFrame(e.TimestampMilliseconds, e.Contacts, contacts);
            if (normalMode && AppConfig.TouchpadEdgeGesturesEnabled &&
                contacts.Count(contact => contact.IsActive) >= 2)
            {
                _wheelSuppressor.StartMonitoring();
            }

            if (!_recognizer.SessionActive)
            {
                _sessionWindowDragImplementation = AppConfig.TouchpadWindowDragImplementation;
                _sessionWindowDragBringToFront = AppConfig.TouchpadWindowDragBringToFront;
                _recognizer = CreateRecognizer(_sessionWindowDragImplementation);
                _sessionStartPoint = frameCursorPosition;
                _windowDragTargetLock.Clear();
            }

            TouchpadInteractionFrameResult result = _recognizer.ProcessFrame(contacts, e.TimestampMilliseconds);
            e.ClaimInput = result.ClaimInput && normalMode;
            _wheelSuppressor.SuppressWheel = e.ClaimInput;

            if (normalMode)
            {
                _windowDragTargetLock.Update(result, frameCursorPosition, GetWindowAtPoint);
                foreach (TouchpadInteractionEvent interactionEvent in result.Events)
                    ProcessInteractionEvent(interactionEvent, e.TimestampMilliseconds);
            }
            else
                _windowDragTargetLock.Clear();

            if (!result.SessionActive)
            {
                _windowDragController.End();
                _windowDragDiagnostics.Complete(e.TimestampMilliseconds, "session-inactive");
                _windowDragTargetLock.Clear();
                _wheelSuppressor.StopMonitoring();
            }
            else if (!normalMode)
                _wheelSuppressor.StopMonitoring();
        }

        private void ProcessInteractionEvent(TouchpadInteractionEvent interactionEvent,
            long timestampMilliseconds)
        {
            switch (interactionEvent.EventType)
            {
                case TouchpadInteractionEventType.EdgeGesture:
                    FireEdgeGesture(interactionEvent.EdgeGesture);
                    break;
                case TouchpadInteractionEventType.WindowDragStarted:
                    if (!_wheelSuppressor.IsMonitoring && !_wheelSuppressor.StartMonitoring())
                    {
                        _windowDragTargetLock.Clear();
                        break;
                    }

                    SystemWindow targetWindow;
                    Point capturedCursor;
                    bool targetLocked = _windowDragTargetLock.TryTake(out targetWindow,
                        out capturedCursor);
                    if (!targetLocked)
                        targetWindow = GetWindowUnderCursor();

                    _windowDragDiagnostics.Begin(timestampMilliseconds,
                        _sessionWindowDragImplementation,
                        targetWindow?.HWnd ?? IntPtr.Zero,
                        targetLocked,
                        AppConfig.TouchpadEdgeConfidenceFilteringEnabled);

                    bool began;
                    if (targetLocked)
                    {
                        began = _windowDragController.Begin(targetWindow, capturedCursor,
                            interactionEvent.NormalizedX, interactionEvent.NormalizedY,
                            _sessionWindowDragImplementation, _sessionWindowDragBringToFront);
                    }
                    else
                    {
                        began = _windowDragController.Begin(targetWindow,
                            interactionEvent.NormalizedX, interactionEvent.NormalizedY,
                            _sessionWindowDragImplementation, _sessionWindowDragBringToFront);
                    }
                    _windowDragDiagnostics.RecordControllerBegin(began, timestampMilliseconds);
                    if (!began)
                        _windowDragDiagnostics.Complete(timestampMilliseconds, "begin-failed");
                    break;
                case TouchpadInteractionEventType.WindowDragMoved:
                    _windowDragDiagnostics.RecordInteraction(interactionEvent.EventType,
                        timestampMilliseconds);
                    if (!_windowDragController.Update(interactionEvent.NormalizedX,
                            interactionEvent.NormalizedY,
                            AppConfig.TouchpadWindowDragSensitivityPercent / 100d))
                    {
                        _windowDragDiagnostics.Complete(timestampMilliseconds,
                            "controller-update-failed");
                    }
                    break;
                case TouchpadInteractionEventType.WindowDragPaused:
                    _windowDragDiagnostics.RecordInteraction(interactionEvent.EventType,
                        timestampMilliseconds);
                    _windowDragController.Pause();
                    break;
                case TouchpadInteractionEventType.WindowDragResumed:
                    _windowDragDiagnostics.RecordInteraction(interactionEvent.EventType,
                        timestampMilliseconds);
                    if (!_windowDragController.Rebase(GetWindowUnderCursor(),
                            interactionEvent.NormalizedX, interactionEvent.NormalizedY))
                    {
                        _windowDragDiagnostics.Complete(timestampMilliseconds,
                            "controller-rebase-failed");
                    }
                    break;
                case TouchpadInteractionEventType.WindowDragEnded:
                    _windowDragController.End();
                    _windowDragDiagnostics.Complete(timestampMilliseconds, "ended");
                    _windowDragTargetLock.Clear();
                    break;
            }
        }

        private static SystemWindow GetWindowUnderCursor()
        {
            return GetWindowAtPoint(Cursor.Position);
        }

        private static SystemWindow GetWindowAtPoint(Point point)
        {
            return ApplicationManager.Instance.GetWindowFromPoint(point);
        }

        private void FireEdgeGesture(FixedEdgeGesture gesture)
        {
            List<IAction> actions = ApplicationManager.Instance.GetRecognizedDefinedAction(action =>
                action != null && action.EdgeGestures != null && action.EdgeGestures.Contains(gesture));
            if (actions.Count != 0)
                OnTriggerFired(new TriggerFiredEventArgs(actions, _sessionStartPoint));
        }

        private static TouchpadInteractionRecognizer CreateRecognizer(
            TouchpadWindowDragImplementation windowDragImplementation)
        {
            bool enabled = AppConfig.TouchpadEdgeGesturesEnabled;
            TouchpadWindowDragMode windowDragMode = AppConfig.TouchpadWindowDragMode;
            if (windowDragMode != TouchpadWindowDragMode.Disabled)
            {
                windowDragMode = windowDragImplementation == TouchpadWindowDragImplementation.ThreeFingerDrag
                    ? TouchpadWindowDragMode.ThreeFingerDrag
                    : TouchpadWindowDragMode.BottomEdgeAnchor;
            }
            var assignedGestures = new HashSet<FixedEdgeGesture>();
            if (enabled)
            {
                foreach (IAction action in ApplicationManager.Instance.Applications
                    .Where(application => !(application is IgnoredApp) && application.Actions != null)
                    .SelectMany(application => application.Actions)
                    .Where(action => action != null && action.EdgeGestures != null))
                {
                    assignedGestures.UnionWith(action.EdgeGestures);
                }
            }

            double activationDistance = AppConfig.TouchpadEdgeActivationPercent / 100d;
            return new TouchpadInteractionRecognizer(new TouchpadInteractionOptions
            {
                EdgeGesturesEnabled = enabled,
                EnabledEdgeGestures = assignedGestures,
                WindowDragMode = enabled ? windowDragMode : TouchpadWindowDragMode.Disabled,
                EdgeZone = AppConfig.TouchpadEdgeZonePercent / 100d,
                LeftEdgeZone = AppConfig.TouchpadLeftEdgeZonePercent / 100d,
                RightEdgeZone = AppConfig.TouchpadRightEdgeZonePercent / 100d,
                TopEdgeZone = AppConfig.TouchpadTopEdgeZonePercent / 100d,
                BottomEdgeZone = AppConfig.TouchpadBottomEdgeZonePercent / 100d,
                EdgeActivationDistance = activationDistance,
                EdgeSlideStep = System.Math.Max(0.03, activationDistance * 0.625)
            });
        }
    }

    internal sealed class BottomAnchoredWindowDragTargetLock
    {
        private bool _locked;
        private Point _cursorPosition;
        private SystemWindow _window;

        internal void Update(TouchpadInteractionFrameResult result, Point frameCursorPosition,
            Func<Point, SystemWindow> resolveWindow)
        {
            if (result.BottomAnchoredWindowDragCandidateEnded)
                Clear();

            if (!result.BottomAnchoredWindowDragCandidateStarted)
                return;

            _cursorPosition = frameCursorPosition;
            _window = resolveWindow(frameCursorPosition);
            _locked = true;
        }

        internal bool TryTake(out SystemWindow window, out Point cursorPosition)
        {
            window = _window;
            cursorPosition = _cursorPosition;
            bool locked = _locked;
            Clear();
            return locked;
        }

        internal void Clear()
        {
            _locked = false;
            _cursorPosition = default(Point);
            _window = null;
        }
    }
}

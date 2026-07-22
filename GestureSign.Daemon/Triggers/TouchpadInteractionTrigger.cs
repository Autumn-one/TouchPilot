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
        private readonly WindowDragController _windowDragController = new WindowDragController();
        private readonly TouchpadWheelSuppressor _wheelSuppressor = new TouchpadWheelSuppressor();
        private readonly ITouchpadContactFilter _confidenceFilter;
        private Point _sessionStartPoint;
        private TouchpadWindowDragImplementation _sessionWindowDragImplementation;

        public TouchpadInteractionTrigger() : this(new TouchpadConfidenceContactFilter())
        {
        }

        internal TouchpadInteractionTrigger(ITouchpadContactFilter confidenceFilter)
        {
            _confidenceFilter = confidenceFilter ?? throw new ArgumentNullException(nameof(confidenceFilter));
            _sessionWindowDragImplementation = AppConfig.TouchpadWindowDragImplementation;
            _recognizer = CreateRecognizer(_sessionWindowDragImplementation);
            PointCapture.Instance.TouchpadFrame += PointCapture_TouchpadFrame;
        }

        private void PointCapture_TouchpadFrame(object sender, TouchpadFrameEventArgs e)
        {
            bool normalMode = PointCapture.Instance.Mode == CaptureMode.Normal;
            IReadOnlyList<TouchpadContact> contacts = AppConfig.TouchpadEdgeConfidenceFilteringEnabled
                ? _confidenceFilter.Filter(e.Contacts)
                : e.Contacts;
            if (normalMode && AppConfig.TouchpadEdgeGesturesEnabled &&
                contacts.Count(contact => contact.IsActive) >= 2)
            {
                _wheelSuppressor.StartMonitoring();
            }

            if (!_recognizer.SessionActive)
            {
                _sessionWindowDragImplementation = AppConfig.TouchpadWindowDragImplementation;
                _recognizer = CreateRecognizer(_sessionWindowDragImplementation);
                _sessionStartPoint = Cursor.Position;
            }

            TouchpadInteractionFrameResult result = _recognizer.ProcessFrame(contacts, e.TimestampMilliseconds);
            e.ClaimInput = result.ClaimInput && normalMode;
            _wheelSuppressor.SuppressWheel = e.ClaimInput;

            if (normalMode)
            {
                foreach (TouchpadInteractionEvent interactionEvent in result.Events)
                    ProcessInteractionEvent(interactionEvent);
            }

            if (!result.SessionActive)
            {
                _windowDragController.End();
                _wheelSuppressor.StopMonitoring();
            }
            else if (!normalMode)
                _wheelSuppressor.StopMonitoring();
        }

        private void ProcessInteractionEvent(TouchpadInteractionEvent interactionEvent)
        {
            switch (interactionEvent.EventType)
            {
                case TouchpadInteractionEventType.EdgeGesture:
                    FireEdgeGesture(interactionEvent.EdgeGesture);
                    break;
                case TouchpadInteractionEventType.WindowDragStarted:
                    if (!_wheelSuppressor.IsMonitoring && !_wheelSuppressor.StartMonitoring())
                        break;
                    _windowDragController.Begin(GetWindowUnderCursor(), interactionEvent.NormalizedX, interactionEvent.NormalizedY,
                        _sessionWindowDragImplementation);
                    break;
                case TouchpadInteractionEventType.WindowDragMoved:
                    _windowDragController.Update(interactionEvent.NormalizedX, interactionEvent.NormalizedY, AppConfig.TouchpadWindowDragSensitivityPercent / 100d);
                    break;
                case TouchpadInteractionEventType.WindowDragPaused:
                    _windowDragController.Pause();
                    break;
                case TouchpadInteractionEventType.WindowDragResumed:
                    _windowDragController.Rebase(GetWindowUnderCursor(), interactionEvent.NormalizedX,
                        interactionEvent.NormalizedY);
                    break;
                case TouchpadInteractionEventType.WindowDragEnded:
                    _windowDragController.End();
                    break;
            }
        }

        private static SystemWindow GetWindowUnderCursor()
        {
            return ApplicationManager.Instance.GetWindowFromPoint(Cursor.Position);
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
}

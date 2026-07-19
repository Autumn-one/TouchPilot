using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using ManagedWinapi.Windows;
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
        private Point _sessionStartPoint;
        private SystemWindow _sessionWindow;
        private TouchpadWindowDragImplementation _sessionWindowDragImplementation;

        public TouchpadInteractionTrigger()
        {
            _sessionWindowDragImplementation = AppConfig.TouchpadWindowDragImplementation;
            _recognizer = CreateRecognizer(_sessionWindowDragImplementation);
            PointCapture.Instance.TouchpadFrame += PointCapture_TouchpadFrame;
        }

        private void PointCapture_TouchpadFrame(object sender, TouchpadFrameEventArgs e)
        {
            if (!_recognizer.SessionActive)
            {
                _sessionWindowDragImplementation = AppConfig.TouchpadWindowDragImplementation;
                _recognizer = CreateRecognizer(_sessionWindowDragImplementation);
                _sessionStartPoint = Cursor.Position;
                _sessionWindow = ApplicationManager.Instance.GetWindowFromPoint(_sessionStartPoint);
            }

            TouchpadInteractionFrameResult result = _recognizer.ProcessFrame(e.Contacts, e.TimestampMilliseconds);
            e.ClaimInput = result.ClaimInput && PointCapture.Instance.Mode == CaptureMode.Normal;

            if (PointCapture.Instance.Mode == CaptureMode.Normal)
            {
                foreach (TouchpadInteractionEvent interactionEvent in result.Events)
                    ProcessInteractionEvent(interactionEvent);
            }

            if (!result.SessionActive)
            {
                _windowDragController.End();
                _sessionWindow = null;
            }
        }

        private void ProcessInteractionEvent(TouchpadInteractionEvent interactionEvent)
        {
            switch (interactionEvent.EventType)
            {
                case TouchpadInteractionEventType.EdgeGesture:
                    FireEdgeGesture(interactionEvent.EdgeGesture);
                    break;
                case TouchpadInteractionEventType.WindowDragStarted:
                    _windowDragController.Begin(_sessionWindow, interactionEvent.NormalizedX, interactionEvent.NormalizedY,
                        _sessionWindowDragImplementation);
                    break;
                case TouchpadInteractionEventType.WindowDragMoved:
                    _windowDragController.Update(interactionEvent.NormalizedX, interactionEvent.NormalizedY, AppConfig.TouchpadWindowDragSensitivityPercent / 100d);
                    break;
                case TouchpadInteractionEventType.WindowDragPaused:
                    _windowDragController.Pause();
                    break;
                case TouchpadInteractionEventType.WindowDragResumed:
                    _windowDragController.Rebase(interactionEvent.NormalizedX, interactionEvent.NormalizedY);
                    break;
                case TouchpadInteractionEventType.WindowDragEnded:
                    _windowDragController.End();
                    break;
            }
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
                EdgeActivationDistance = activationDistance,
                EdgeSlideStep = System.Math.Max(0.03, activationDistance * 0.625)
            });
        }
    }
}

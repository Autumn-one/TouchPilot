using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Daemon.Input;
using System;
using System.Threading;

namespace GestureSign.Daemon
{
    class MessageProcessor : IMessageProcessor
    {
        private SynchronizationContext _synchronizationContext;
        private readonly System.Action _requestUpdateCheck;

        public MessageProcessor(SynchronizationContext synchronizationContext,
            System.Action requestUpdateCheck)
        {
            _synchronizationContext = synchronizationContext ??
                                      throw new ArgumentNullException(nameof(synchronizationContext));
            _requestUpdateCheck = requestUpdateCheck ??
                                  throw new ArgumentNullException(nameof(requestUpdateCheck));
        }

        public bool ProcessMessages(IpcCommands command, object data)
        {
            _synchronizationContext.Post(state =>
            {
                switch (command)
                {
                    case IpcCommands.StartTeaching:
                        PointCapture.Instance.Mode = CaptureMode.Training;
                        break;
                    case IpcCommands.StopTraining:
                        if (PointCapture.Instance.Mode != CaptureMode.UserDisabled)
                            PointCapture.Instance.Mode = CaptureMode.Normal;
                        break;
                    case IpcCommands.LoadApplications:
                        ApplicationManager.Instance.LoadApplications().Wait();
                        break;
                    case IpcCommands.LoadGestures:
                        GestureManager.Instance.LoadGestures().Wait();
                        break;
                    case IpcCommands.LoadConfiguration:
                        AppConfig.Reload();
                        break;
                    case IpcCommands.StartControlPanel:
                        TrayManager.StartControlPanel();
                        break;
                    case IpcCommands.CheckForUpdates:
                        _requestUpdateCheck();
                        break;
                }
            }, null);

            return true;
        }

    }
}

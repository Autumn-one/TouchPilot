using GestureSign.Common.Log;
using ManagedWinapi.Hooks;
using System;
using System.Drawing;

namespace GestureSign.Daemon.Triggers
{
    internal sealed class TouchpadWheelSuppressor : IDisposable
    {
        private const int WmMouseWheel = 0x020A;
        private const int WmMouseHorizontalWheel = 0x020E;

        private readonly LowLevelMouseHook _mouseHook = new LowLevelMouseHook();
        private volatile bool _suppressWheel;
        private bool _disposed;
        private bool _failureLogged;

        public TouchpadWheelSuppressor()
        {
            _mouseHook.MessageIntercepted += MouseHook_MessageIntercepted;
            _mouseHook.MouseMove += MouseHook_MouseMove;
        }

        internal event Action<Point> ExternalMouseMoved;

        internal static bool IsExternalMouseMove(int flags)
        {
            return (flags & 0x03) == 0;
        }

        private void MouseHook_MouseMove(LowLevelMouseMessage message, ref bool handled)
        {
            if (IsExternalMouseMove(message.Flags))
                ExternalMouseMoved?.Invoke(message.Point);
        }

        internal bool IsMonitoring => !_disposed && _mouseHook.Hooked;

        internal bool SuppressWheel
        {
            get { return _suppressWheel; }
            set { _suppressWheel = value; }
        }

        internal string LastFailure { get; private set; }

        internal bool StartMonitoring()
        {
            if (_disposed)
                return false;
            if (_mouseHook.Hooked)
                return true;

            try
            {
                _mouseHook.StartHook();
                return true;
            }
            catch (Exception exception)
            {
                LogFailureOnce("install the low-level mouse hook", exception.Message);
                return false;
            }
        }

        internal void StopMonitoring()
        {
            _suppressWheel = false;
            if (_disposed || !_mouseHook.Hooked)
                return;

            try
            {
                _mouseHook.Unhook();
            }
            catch (Exception exception)
            {
                LogFailureOnce("remove the low-level mouse hook", exception.Message);
            }
        }

        internal static bool ShouldSuppressWheel(bool suppressionActive, int message)
        {
            return suppressionActive &&
                   (message == WmMouseWheel || message == WmMouseHorizontalWheel);
        }

        private void MouseHook_MessageIntercepted(LowLevelMessage message, ref bool handled)
        {
            if (ShouldSuppressWheel(_suppressWheel, message.Message))
                handled = true;
        }

        private void LogFailureOnce(string operation, string detail)
        {
            if (_failureLogged)
                return;

            _failureLogged = true;
            LastFailure = $"Touchpad wheel suppression could not {operation}. {detail}.";
            Logging.LogMessage(LastFailure);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            StopMonitoring();
            _mouseHook.MessageIntercepted -= MouseHook_MessageIntercepted;
            _mouseHook.MouseMove -= MouseHook_MouseMove;
            _mouseHook.Dispose();
            _disposed = true;
        }
    }
}

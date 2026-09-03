using GestureSign.Common.Applications;
using GestureSign.Common.Plugins;
using GestureSign.Daemon.Input;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using WinFormsApplication = System.Windows.Forms.Application;

namespace GestureSign.Daemon.Triggers
{
    class TriggerManager : IDisposable
    {
        #region Private Variables

        private List<Trigger> _triggerList = new List<Trigger>(4);
        private bool _applicationExitSubscribed;
        private bool _disposed;

        #endregion

        #region Constructors

        static TriggerManager()
        {
            Instance = new TriggerManager();
        }

        #endregion

        #region Public Instance Properties

        public static TriggerManager Instance { get; }

        #endregion

        #region Public Methods

        public void Load()
        {
            if (!_applicationExitSubscribed)
            {
                WinFormsApplication.ApplicationExit += Application_ApplicationExit;
                _applicationExitSubscribed = true;
            }
            AddTrigger(new HotKeyManager());
            AddTrigger(new MouseTrigger());
            AddTrigger(new ContinuousGestureTrigger());
            AddTrigger(new TouchpadInteractionTrigger());
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_applicationExitSubscribed)
            {
                WinFormsApplication.ApplicationExit -= Application_ApplicationExit;
                _applicationExitSubscribed = false;
            }

            foreach (IDisposable trigger in _triggerList.OfType<IDisposable>())
                trigger.Dispose();
            _triggerList.Clear();
        }

        #endregion


        #region Private Methods

        private void AddTrigger(Trigger newTrigger)
        {
            newTrigger.TriggerFired += Trigger_TriggerFired;
            _triggerList.Add(newTrigger);
        }

        private void Trigger_TriggerFired(object sender, TriggerFiredEventArgs e)
        {
            if (e.FiredActions == null || e.FiredActions.Count == 0) return;
            var point = new List<Point>(new[] { e.FiredPoint });
            PluginManager.Instance.ExecuteAction(e.FiredActions, PointCapture.Instance.Mode, PointCapture.Instance.SourceDevice, new List<int>(new[] { 1 }), point, new List<List<Point>>(new[] { point }));
        }

        private void Application_ApplicationExit(object sender, EventArgs e)
        {
            Dispose();
        }

        #endregion
    }
}

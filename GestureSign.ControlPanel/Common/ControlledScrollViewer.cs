using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GestureSign.ControlPanel.Common
{
    public class ControlledScrollViewer : ScrollViewer
    {
        private const double WheelDeltaPerNotch = 120d;

        public static readonly DependencyProperty WheelScrollStepProperty = DependencyProperty.Register(
            nameof(WheelScrollStep),
            typeof(double),
            typeof(ControlledScrollViewer),
            new FrameworkPropertyMetadata(24d),
            value => value is double step && step >= 0 && !double.IsNaN(step) && !double.IsInfinity(step));

        public double WheelScrollStep
        {
            get => (double)GetValue(WheelScrollStepProperty);
            set => SetValue(WheelScrollStepProperty, value);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (e.Handled || ScrollableHeight <= 0)
            {
                base.OnMouseWheel(e);
                return;
            }

            double offsetDelta = -e.Delta / WheelDeltaPerNotch * WheelScrollStep;
            ScrollToVerticalOffset(VerticalOffset + offsetDelta);
            e.Handled = true;
        }
    }
}

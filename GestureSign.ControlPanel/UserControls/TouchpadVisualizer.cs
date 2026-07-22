using GestureSign.Common.Input;
using GestureSign.ControlPanel.Visualization;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace GestureSign.ControlPanel.UserControls
{
    public sealed class TouchpadVisualizer : FrameworkElement
    {
        private static readonly Color[] ContactColors =
        {
            Color.FromRgb(45, 149, 232),
            Color.FromRgb(38, 166, 91),
            Color.FromRgb(235, 174, 52),
            Color.FromRgb(220, 84, 126),
            Color.FromRgb(25, 174, 181),
            Color.FromRgb(143, 102, 210)
        };

        private static readonly Brush[] ContactBrushes = ContactColors
            .Select(color => Freeze(new SolidColorBrush(color)))
            .ToArray();

        private static readonly Brush[] ContactHaloBrushes = ContactColors
            .Select(color => Freeze(new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B))))
            .ToArray();

        private static readonly Pen[] TrailPens = ContactColors
            .Select(color => Freeze(new Pen(
                new SolidColorBrush(Color.FromArgb(125, color.R, color.G, color.B)), 4)))
            .ToArray();

        private static readonly Brush[] LowConfidenceContactBrushes = ContactColors
            .Select(color => Freeze(new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B))))
            .ToArray();

        private static readonly Pen[] LowConfidenceContactPens = ContactColors
            .Select(color => Freeze(new Pen(new SolidColorBrush(color), 3) { DashStyle = DashStyles.Dash }))
            .ToArray();

        private static readonly Pen[] LowConfidenceTrailPens = ContactColors
            .Select(color => Freeze(new Pen(
                new SolidColorBrush(Color.FromArgb(95, color.R, color.G, color.B)), 3)
            {
                DashStyle = DashStyles.Dash
            }))
            .ToArray();

        private readonly TouchpadVisualizationState _state = new TouchpadVisualizationState();
        private readonly DispatcherTimer _fadeTimer;

        public TouchpadVisualizer()
        {
            ClipToBounds = true;
            Focusable = false;
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
            _fadeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render,
                FadeTimer_Tick, Dispatcher);
            _fadeTimer.Stop();
            Unloaded += (sender, eventArgs) => _fadeTimer.Stop();
        }

        public static readonly DependencyProperty EdgeZonePercentProperty = DependencyProperty.Register(
            nameof(EdgeZonePercent), typeof(double), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LeftEdgeZonePercentProperty = DependencyProperty.Register(
            nameof(LeftEdgeZonePercent), typeof(double), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RightEdgeZonePercentProperty = DependencyProperty.Register(
            nameof(RightEdgeZonePercent), typeof(double), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TopEdgeZonePercentProperty = DependencyProperty.Register(
            nameof(TopEdgeZonePercent), typeof(double), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BottomEdgeZonePercentProperty = DependencyProperty.Register(
            nameof(BottomEdgeZonePercent), typeof(double), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsConnectedProperty = DependencyProperty.Register(
            nameof(IsConnected), typeof(bool), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
                OnIsConnectedChanged));

        public static readonly DependencyProperty SurfaceBrushProperty = DependencyProperty.Register(
            nameof(SurfaceBrush), typeof(Brush), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty OutlineBrushProperty = DependencyProperty.Register(
            nameof(OutlineBrush), typeof(Brush), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty EdgeAreaBrushProperty = DependencyProperty.Register(
            nameof(EdgeAreaBrush), typeof(Brush), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(Brushes.LightBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty EdgeBoundaryBrushProperty = DependencyProperty.Register(
            nameof(EdgeBoundaryBrush), typeof(Brush), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty EmptyTextBrushProperty = DependencyProperty.Register(
            nameof(EmptyTextBrush), typeof(Brush), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty WaitingTextProperty = DependencyProperty.Register(
            nameof(WaitingText), typeof(string), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DisconnectedTextProperty = DependencyProperty.Register(
            nameof(DisconnectedText), typeof(string), typeof(TouchpadVisualizer),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public double EdgeZonePercent
        {
            get => (double)GetValue(EdgeZonePercentProperty);
            set => SetValue(EdgeZonePercentProperty, value);
        }

        public double LeftEdgeZonePercent
        {
            get => (double)GetValue(LeftEdgeZonePercentProperty);
            set => SetValue(LeftEdgeZonePercentProperty, value);
        }

        public double RightEdgeZonePercent
        {
            get => (double)GetValue(RightEdgeZonePercentProperty);
            set => SetValue(RightEdgeZonePercentProperty, value);
        }

        public double TopEdgeZonePercent
        {
            get => (double)GetValue(TopEdgeZonePercentProperty);
            set => SetValue(TopEdgeZonePercentProperty, value);
        }

        public double BottomEdgeZonePercent
        {
            get => (double)GetValue(BottomEdgeZonePercentProperty);
            set => SetValue(BottomEdgeZonePercentProperty, value);
        }

        public bool IsConnected
        {
            get => (bool)GetValue(IsConnectedProperty);
            set => SetValue(IsConnectedProperty, value);
        }

        public Brush SurfaceBrush
        {
            get => (Brush)GetValue(SurfaceBrushProperty);
            set => SetValue(SurfaceBrushProperty, value);
        }

        public Brush OutlineBrush
        {
            get => (Brush)GetValue(OutlineBrushProperty);
            set => SetValue(OutlineBrushProperty, value);
        }

        public Brush EdgeAreaBrush
        {
            get => (Brush)GetValue(EdgeAreaBrushProperty);
            set => SetValue(EdgeAreaBrushProperty, value);
        }

        public Brush EdgeBoundaryBrush
        {
            get => (Brush)GetValue(EdgeBoundaryBrushProperty);
            set => SetValue(EdgeBoundaryBrushProperty, value);
        }

        public Brush EmptyTextBrush
        {
            get => (Brush)GetValue(EmptyTextBrushProperty);
            set => SetValue(EmptyTextBrushProperty, value);
        }

        public string WaitingText
        {
            get => (string)GetValue(WaitingTextProperty);
            set => SetValue(WaitingTextProperty, value);
        }

        public string DisconnectedText
        {
            get => (string)GetValue(DisconnectedTextProperty);
            set => SetValue(DisconnectedTextProperty, value);
        }

        public void UpdateFrame(TouchpadVisualizationFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => UpdateFrame(frame), DispatcherPriority.Render);
                return;
            }

            _state.ApplyFrame(frame);
            IsConnected = true;
            if (_state.Traces.Count != 0 && !_fadeTimer.IsEnabled)
                _fadeTimer.Start();
            InvalidateVisual();
        }

        public void ClearContacts()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(ClearContacts, DispatcherPriority.Render);
                return;
            }

            _state.Clear();
            _fadeTimer.Stop();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (ActualWidth < 4 || ActualHeight < 4)
                return;

            var bounds = new Rect(1, 1, ActualWidth - 2, ActualHeight - 2);
            drawingContext.DrawRoundedRectangle(SurfaceBrush, new Pen(OutlineBrush, 2), bounds, 8, 8);
            DrawEdgeZones(drawingContext, bounds);

            long timestampMilliseconds = Environment.TickCount64;
            foreach (TouchpadContactTrace trace in _state.Traces.OrderBy(trace => trace.ContactIdentifier))
                DrawTrace(drawingContext, bounds, trace, timestampMilliseconds);

            if (_state.Traces.Count == 0)
                DrawEmptyState(drawingContext, bounds);
        }

        private void DrawEdgeZones(DrawingContext drawingContext, Rect bounds)
        {
            double leftRatio = GetEdgeZoneRatio(LeftEdgeZonePercent);
            double rightRatio = GetEdgeZoneRatio(RightEdgeZonePercent);
            double topRatio = GetEdgeZoneRatio(TopEdgeZonePercent);
            double bottomRatio = GetEdgeZoneRatio(BottomEdgeZonePercent);
            var inner = new Rect(
                bounds.Left + bounds.Width * leftRatio,
                bounds.Top + bounds.Height * topRatio,
                bounds.Width * (1 - leftRatio - rightRatio),
                bounds.Height * (1 - topRatio - bottomRatio));
            var zone = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(bounds, 8, 8), new RectangleGeometry(inner));
            drawingContext.DrawGeometry(EdgeAreaBrush, null, zone);

            var boundaryPen = new Pen(EdgeBoundaryBrush, 1) { DashStyle = DashStyles.Dash };
            drawingContext.DrawLine(boundaryPen,
                new Point(inner.Left, bounds.Top), new Point(inner.Left, bounds.Bottom));
            drawingContext.DrawLine(boundaryPen,
                new Point(inner.Right, bounds.Top), new Point(inner.Right, bounds.Bottom));
            drawingContext.DrawLine(boundaryPen,
                new Point(bounds.Left, inner.Top), new Point(bounds.Right, inner.Top));
            drawingContext.DrawLine(boundaryPen,
                new Point(bounds.Left, inner.Bottom), new Point(bounds.Right, inner.Bottom));
        }

        private double GetEdgeZoneRatio(double edgeZonePercent)
        {
            double percent = double.IsNaN(edgeZonePercent) ? EdgeZonePercent : edgeZonePercent;
            if (double.IsNaN(percent) || double.IsInfinity(percent))
                return 0;
            return Math.Max(0, Math.Min(0.49, percent / 100d));
        }

        private void DrawTrace(DrawingContext drawingContext, Rect bounds,
            TouchpadContactTrace trace, long timestampMilliseconds)
        {
            if (trace.Points.Count == 0)
                return;

            int paletteIndex = Math.Abs(trace.ContactIdentifier % ContactBrushes.Length);
            bool lowConfidence = trace.LastPoint.Confidence == TouchpadContactConfidence.LowConfidence;
            double opacity = trace.IsActive
                ? 1
                : Math.Max(0, 1 - (timestampMilliseconds - trace.ReleasedAtMilliseconds) /
                    (double)TouchpadVisualizationState.TrailLifetimeMilliseconds);
            drawingContext.PushOpacity(opacity);
            if (trace.Points.Count > 1)
            {
                var geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(Map(bounds, trace.Points[0]), false, false);
                    for (int i = 1; i < trace.Points.Count; i++)
                        context.LineTo(Map(bounds, trace.Points[i]), true, false);
                }
                geometry.Freeze();
                drawingContext.DrawGeometry(null,
                    lowConfidence ? LowConfidenceTrailPens[paletteIndex] : TrailPens[paletteIndex], geometry);
            }

            Point center = Map(bounds, trace.LastPoint);
            drawingContext.DrawEllipse(ContactHaloBrushes[paletteIndex], null, center, 19, 19);
            if (lowConfidence)
            {
                drawingContext.DrawEllipse(LowConfidenceContactBrushes[paletteIndex],
                    LowConfidenceContactPens[paletteIndex], center, 13, 13);
                DrawContactIdentifier(drawingContext, center, trace.ContactIdentifier,
                    ContactBrushes[paletteIndex]);
            }
            else
            {
                drawingContext.DrawEllipse(ContactBrushes[paletteIndex],
                    new Pen(Brushes.White, 2), center, 13, 13);
                DrawContactIdentifier(drawingContext, center, trace.ContactIdentifier, Brushes.White);
            }
            drawingContext.Pop();
        }

        private static Point Map(Rect bounds, TouchpadTracePoint point)
        {
            return new Point(
                bounds.Left + Math.Max(0, Math.Min(1, point.NormalizedX)) * bounds.Width,
                bounds.Top + Math.Max(0, Math.Min(1, point.NormalizedY)) * bounds.Height);
        }

        private void DrawContactIdentifier(DrawingContext drawingContext, Point center,
            int contactIdentifier, Brush textBrush)
        {
            var text = new FormattedText(
                contactIdentifier.ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                11,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(text,
                new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }

        private void DrawEmptyState(DrawingContext drawingContext, Rect bounds)
        {
            string value = IsConnected ? WaitingText : DisconnectedText;
            if (string.IsNullOrWhiteSpace(value))
                return;

            var text = new FormattedText(
                value,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                14,
                EmptyTextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(text,
                new Point(bounds.Left + (bounds.Width - text.Width) / 2,
                    bounds.Top + (bounds.Height - text.Height) / 2));
        }

        private void FadeTimer_Tick(object sender, EventArgs e)
        {
            _state.Advance(Environment.TickCount64);
            if (_state.Traces.Count == 0)
                _fadeTimer.Stop();
            InvalidateVisual();
        }

        private static void OnIsConnectedChanged(DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs eventArgs)
        {
            if (!(bool)eventArgs.NewValue)
                ((TouchpadVisualizer)dependencyObject).ClearContacts();
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}

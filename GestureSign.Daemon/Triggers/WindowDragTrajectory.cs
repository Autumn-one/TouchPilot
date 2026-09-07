using System;
using System.Drawing;

namespace GestureSign.Daemon.Triggers
{
    internal sealed class WindowDragTrajectory
    {
        private const double MinimumCutoff = 5;
        private const double SpeedCoefficient = 0.05;
        private const double DerivativeCutoff = 1;
        private const double MaximumFilterLagPixels = 1;
        private const double PixelHysteresis = 0.75;

        private Size _scale;
        private double _lastX;
        private double _lastY;
        private double _timestamp;
        private double _rawX;
        private double _rawY;
        private double _filteredX;
        private double _filteredY;
        private double _velocityX;
        private double _velocityY;
        private Point _position;

        internal void Begin(double x, double y, Point cursor, Size scale, double timestamp)
        {
            _scale = scale;
            Rebase(x, y, cursor, timestamp);
        }

        internal void Rebase(double x, double y, Point cursor, double timestamp)
        {
            _lastX = x;
            _lastY = y;
            _timestamp = timestamp;
            _rawX = _filteredX = cursor.X;
            _rawY = _filteredY = cursor.Y;
            _velocityX = _velocityY = 0;
            _position = cursor;
        }

        internal Point Update(double x, double y, double sensitivity, double timestamp,
            Rectangle desktop)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) ||
                !double.IsFinite(sensitivity) || sensitivity <= 0 ||
                !double.IsFinite(timestamp))
                return _position;

            double elapsed = Math.Clamp(timestamp - _timestamp, 0.001, 0.05);
            _timestamp = Math.Max(timestamp, _timestamp);
            double nextX = Math.Clamp(_rawX + (x - _lastX) * _scale.Width * sensitivity,
                desktop.Left, desktop.Right - 1);
            double nextY = Math.Clamp(_rawY + (y - _lastY) * _scale.Height * sensitivity,
                desktop.Top, desktop.Bottom - 1);
            _lastX = x;
            _lastY = y;

            // One Euro filtering in desktop pixels, with bounded lag for precise placement.
            double derivativeAlpha = Alpha(DerivativeCutoff, elapsed);
            _velocityX += derivativeAlpha * ((nextX - _rawX) / elapsed - _velocityX);
            _velocityY += derivativeAlpha * ((nextY - _rawY) / elapsed - _velocityY);
            double speed = Math.Sqrt(_velocityX * _velocityX + _velocityY * _velocityY);
            double alpha = Alpha(MinimumCutoff + SpeedCoefficient * speed, elapsed);
            _rawX = nextX;
            _rawY = nextY;
            _filteredX = Filter(nextX, _filteredX, alpha);
            _filteredY = Filter(nextY, _filteredY, alpha);
            _position = new Point(Quantize(_filteredX, _position.X),
                Quantize(_filteredY, _position.Y));
            _position.X = Math.Clamp(_position.X, desktop.Left, desktop.Right - 1);
            _position.Y = Math.Clamp(_position.Y, desktop.Top, desktop.Bottom - 1);
            return _position;
        }

        private static double Alpha(double cutoff, double elapsed)
        {
            double factor = 2 * Math.PI * cutoff * elapsed;
            return factor / (1 + factor);
        }

        private static double Filter(double raw, double previous, double alpha)
        {
            return Math.Clamp(previous + alpha * (raw - previous),
                raw - MaximumFilterLagPixels, raw + MaximumFilterLagPixels);
        }

        private static int Quantize(double value, int previous)
        {
            return Math.Abs(value - previous) <= PixelHysteresis
                ? previous
                : (int)Math.Round(value);
        }
    }
}

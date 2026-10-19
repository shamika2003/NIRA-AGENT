using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace NIRAAgent.UI.Typing;

// Animated generative-image glyph for NIRA's journey surface.
// It scales cleanly from a compact inline indicator up to a larger
// placeholder panel and never exposes a clickable toolbar/action of its own.
public sealed class NIRAImageCreationIndicator : FrameworkElement
{
    private readonly Stopwatch _clock = new();
    private bool _rendering;

    public NIRAImageCreationIndicator()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => UpdateSubscription();
    }

    private void UpdateSubscription()
    {
        if (!IsLoaded || !IsVisible)
        {
            Stop();
            return;
        }

        if (_rendering)
            return;

        _rendering = true;
        _clock.Restart();
        CompositionTarget.Rendering += Frame;
    }

    private void Frame(object? sender, EventArgs e)
    {
        if (IsVisible)
            InvalidateVisual();
        else
            Stop();
    }

    private void Stop()
    {
        if (!_rendering)
            return;

        CompositionTarget.Rendering -= Frame;
        _rendering = false;
        _clock.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 12 || h < 12)
            return;

        double t = _clock.Elapsed.TotalSeconds;
        Rect card = new(w * 0.10, h * 0.12, w * 0.80, h * 0.68);
        double radius = Math.Max(6, Math.Min(card.Width, card.Height) * 0.08);

        var outer = new SolidColorBrush(Color.FromArgb(36, 132, 198, 255)); outer.Freeze();
        var rim = new Pen(new SolidColorBrush(Color.FromArgb(205, 92, 186, 255)), Math.Max(1.3, Math.Min(w, h) * 0.012));
        rim.Freeze();
        var inner = new LinearGradientBrush(
            Color.FromArgb(210, 9, 21, 43),
            Color.FromArgb(155, 23, 58, 102),
            new Point(0, 0), new Point(1, 1));
        inner.Freeze();

        dc.DrawRoundedRectangle(outer, null, new Rect(card.X - 3, card.Y - 3, card.Width + 6, card.Height + 6), radius + 2, radius + 2);
        dc.DrawRoundedRectangle(inner, rim, card, radius, radius);

        // moving scan light
        double beamWidth = Math.Max(card.Width * 0.16, 18);
        double beamTravel = card.Width + beamWidth * 2;
        double beamX = card.X - beamWidth + (t * 120 % beamTravel);
        var beam = new LinearGradientBrush();
        beam.StartPoint = new Point(0, 0);
        beam.EndPoint = new Point(1, 0);
        beam.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        beam.GradientStops.Add(new GradientStop(Color.FromArgb(80, 163, 228, 255), 0.5));
        beam.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        beam.Freeze();
        dc.PushClip(new RectangleGeometry(card, radius, radius));
        dc.DrawRectangle(beam, null, new Rect(beamX, card.Y, beamWidth, card.Height));

        // stylized landscape line
        var landscape = new StreamGeometry();
        using (var ctx = landscape.Open())
        {
            double left = card.X + card.Width * 0.13;
            double right = card.Right - card.Width * 0.10;
            double baseY = card.Bottom - card.Height * 0.23;
            ctx.BeginFigure(new Point(left, baseY), false, false);
            ctx.LineTo(new Point(card.X + card.Width * 0.35, card.Y + card.Height * 0.46), true, false);
            ctx.LineTo(new Point(card.X + card.Width * 0.50, card.Y + card.Height * 0.61), true, false);
            ctx.LineTo(new Point(card.X + card.Width * 0.69, card.Y + card.Height * 0.37), true, false);
            ctx.LineTo(new Point(right, baseY), true, false);
        }
        landscape.Freeze();
        var landscapePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 182, 232, 255)), Math.Max(1.1, Math.Min(w, h) * 0.011));
        landscapePen.Freeze();
        dc.DrawGeometry(null, landscapePen, landscape);

        // sun / focus point
        Point sun = new(card.X + card.Width * 0.73, card.Y + card.Height * 0.30);
        double sunRadius = Math.Max(2.6, Math.Min(w, h) * 0.034);
        var sunBrush = new RadialGradientBrush(Color.FromArgb(225, 255, 236, 174), Color.FromArgb(0, 255, 236, 174));
        sunBrush.RadiusX = sunBrush.RadiusY = 1;
        sunBrush.Freeze();
        dc.DrawEllipse(sunBrush, null, sun, sunRadius * 1.9, sunRadius * 1.9);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(235, 255, 230, 160)), null, sun, sunRadius, sunRadius);

        dc.Pop();

        // orbiting creation particles around the card
        Point center = new(card.X + card.Width / 2, card.Y + card.Height / 2);
        for (int i = 0; i < 6; i++)
        {
            double theta = t * 1.9 + i * (Math.PI * 2 / 6);
            double rx = card.Width * 0.58;
            double ry = card.Height * 0.54;
            Point p = new(center.X + Math.Cos(theta) * rx, center.Y + Math.Sin(theta) * ry);
            byte alpha = (byte)(110 + (Math.Sin(t * 3.2 + i) + 1) * 55);
            byte red = i % 2 == 0 ? (byte)112 : (byte)184;
            byte green = i % 2 == 0 ? (byte)212 : (byte)170;
            double size = i % 3 == 0 ? Math.Max(2.3, w * 0.018) : Math.Max(1.6, w * 0.013);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, 255));
            brush.Freeze();
            dc.DrawEllipse(brush, null, p, size, size);
        }

        // lower pulse dots to connect visually with NIRA's journey motif
        double dotY = h * 0.92;
        for (int i = 0; i < 3; i++)
        {
            double pulse = (Math.Sin(t * 4.5 - i * 0.55) + 1) / 2;
            double size = Math.Max(1.6, Math.Min(w, h) * (0.020 + pulse * 0.013));
            byte alpha = (byte)(120 + pulse * 115);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, i == 1 ? (byte)160 : (byte)110,
                i == 1 ? (byte)231 : (byte)201, 255));
            brush.Freeze();
            double dx = w * (0.40 + i * 0.10);
            dc.DrawEllipse(brush, null, new Point(dx, dotY), size, size);
        }
    }
}

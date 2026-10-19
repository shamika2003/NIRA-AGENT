using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace NIRAAgent.UI.Typing;

// Tiny animated image-frame + orbiting blue sparkles alongside NIRA's three-dot journey.
// It is ONLY visible when trusted runtime dispatches image.generate, never a toolbar button.
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
        if (!IsLoaded || !IsVisible) { Stop(); return; }
        if (_rendering) return;
        _rendering = true;
        _clock.Restart();
        CompositionTarget.Rendering += Frame;
    }
    private void Frame(object? sender, EventArgs e) { if (IsVisible) InvalidateVisual(); else Stop(); }
    private void Stop()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= Frame;
        _rendering = false;
        _clock.Stop();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double t = _clock.Elapsed.TotalSeconds;
        double x = ActualWidth / 2, y = ActualHeight / 2;
        if (x < 4 || y < 4) return;
        var border = new Pen(new SolidColorBrush(Color.FromArgb(200, 67, 177, 255)), 1.2);
        border.Freeze();
        var shade = new SolidColorBrush(Color.FromArgb(34, 78, 170, 255)); shade.Freeze();
        var image = new StreamGeometry();
        using (var ctx = image.Open())
        {
            ctx.BeginFigure(new Point(x - 6.2, y - 4.8), true, true);
            ctx.LineTo(new Point(x + 6.2, y - 4.8), true, false);
            ctx.LineTo(new Point(x + 6.2, y + 4.8), true, false);
            ctx.LineTo(new Point(x - 6.2, y + 4.8), true, false);
        }
        image.Freeze();
        dc.DrawGeometry(shade, border, image);
        var peak = new StreamGeometry();
        using (var ctx = peak.Open())
        {
            ctx.BeginFigure(new Point(x - 4.5, y + 2.5), false, false);
            ctx.LineTo(new Point(x - 1, y - 0.5), true, false);
            ctx.LineTo(new Point(x + 1, y + 1.1), true, false);
            ctx.LineTo(new Point(x + 3.7, y - 2.0), true, false);
            ctx.LineTo(new Point(x + 4.9, y + 2.5), true, false);
        }
        peak.Freeze();
        dc.DrawGeometry(null, new Pen(Brushes.LightSkyBlue, 1.0), peak);
        for (int i = 0; i < 3; i++)
        {
            double theta = t * 2.8 + i * (Math.PI * 2 / 3);
            double sparkleX = x + Math.Cos(theta) * 10.0;
            double sparkleY = y + Math.Sin(theta) * 7.2;
            byte alpha = (byte)(155 + 80 * (1 + Math.Sin(t * 4 + i * 2)) / 2);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, i == 1 ? (byte)190 : (byte)105,
                i == 1 ? (byte)125 : (byte)219, 255));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(sparkleX, sparkleY), i == 0 ? 1.7 : 1.25, i == 0 ? 1.7 : 1.25);
        }
    }
}

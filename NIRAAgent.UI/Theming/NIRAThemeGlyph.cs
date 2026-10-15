using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace NIRAAgent.UI.Theming;

/// <summary>
/// The same crisp, font-independent Halo / Eclipse glyph is used in
/// the main window and Settings. Only appearance is changed here.
/// </summary>
internal static class NIRAThemeGlyph
{
    // Orbital phase marks. The matching concentric-ring housing is drawn by
    // the main-window and Settings XAML; these marks are the moving accents.
    // No sun rays, font icons, or generic crescent placeholder.
    private static readonly Geometry HaloGlyph = Geometry.Parse(
        "M12,2.2 C17.6,2.2 21.8,6.5 21.8,12 " +
        "C21.8,17.5 17.6,21.8 12,21.8 " +
        "M12,21.8 C6.5,21.8 2.2,17.5 2.2,12 " +
        "M7.2,12 L16.8,12 M12,7.2 L12,16.8");

    private static readonly Geometry EclipseGlyph = Geometry.Parse(
        "M12,2.2 C17.6,2.2 21.8,6.5 21.8,12 " +
        "C21.8,17.5 17.6,21.8 12,21.8 " +
        "M12,21.8 C6.5,21.8 2.2,17.5 2.2,12 " +
        "M7.1,8.4 C8.8,15.0 15.0,17.1 17.5,16.4 " +
        "M7.1,8.4 C10.2,8.8 15.8,8.3 17.5,16.4");

    public static void ApplyBackdrop(Image eclipse, Image haloLayer, bool halo, bool animate)
    {
        double eclipseTarget = halo ? 0.0 : 1.0;
        double haloTarget = halo ? 1.0 : 0.0;
        if (!animate)
        {
            eclipse.BeginAnimation(UIElement.OpacityProperty, null);
            haloLayer.BeginAnimation(UIElement.OpacityProperty, null);
            eclipse.Opacity = eclipseTarget;
            haloLayer.Opacity = haloTarget;
            return;
        }

        var easing = new SineEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(390);
        eclipse.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(eclipse.Opacity, eclipseTarget, duration)
            {
                EasingFunction = easing
            }, HandoffBehavior.SnapshotAndReplace);
        haloLayer.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(haloLayer.Opacity, haloTarget, duration)
            {
                EasingFunction = easing
            }, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Apply(Path icon, bool halo, bool animate)
    {
        icon.BeginAnimation(UIElement.OpacityProperty, null);
        icon.Data = halo ? HaloGlyph : EclipseGlyph;
        icon.Opacity = 1.0;
        icon.RenderTransformOrigin = new Point(0.5, 0.5);

        RotateTransform rotation = icon.RenderTransform as RotateTransform
            ?? new RotateTransform();
        icon.RenderTransform = rotation;
        rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        rotation.Angle = 0;
        if (!animate) return;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        icon.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.24, 1.0, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = easing
            }, HandoffBehavior.SnapshotAndReplace);
        rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(halo ? -60.0 : 60.0, 0.0,
                TimeSpan.FromMilliseconds(490))
            {
                EasingFunction = easing
            }, HandoffBehavior.SnapshotAndReplace);
    }
}

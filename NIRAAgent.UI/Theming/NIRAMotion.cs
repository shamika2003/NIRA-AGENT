using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NIRAAgent.UI.Theming;

public static class NIRAMotion
{
    public static void AnimateEntrance(
        FrameworkElement element,
        double distance = 12.0,
        int durationMs = 280,
        int delayMs = 0)
    {
        if (element == null)
        {
            return;
        }

        element.Opacity = 0.0;

        TranslateTransform translate = element.RenderTransform as TranslateTransform
            ?? new TranslateTransform();

        element.RenderTransform = translate;

        translate.Y = distance;

        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(
                0.0,
                1.0,
                TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(
                distance,
                0.0,
                TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    public static void BeginPulseOpacity(
        UIElement element,
        double fromOpacity,
        double toOpacity,
        double seconds,
        int delayMs = 0)
    {
        if (element == null)
        {
            return;
        }

        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(
                fromOpacity,
                toOpacity,
                TimeSpan.FromSeconds(seconds))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
    }

    public static void BeginBreathingScale(
        FrameworkElement element,
        double fromScale,
        double toScale,
        double seconds,
        int delayMs = 0)
    {
        if (element == null)
        {
            return;
        }

        element.RenderTransformOrigin = new Point(0.5, 0.5);

        ScaleTransform scale = element.RenderTransform as ScaleTransform
            ?? new ScaleTransform(1.0, 1.0);

        element.RenderTransform = scale;

        DoubleAnimation animation = new(
            fromScale,
            toScale,
            TimeSpan.FromSeconds(seconds))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
}


using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SegaAgent.UI;

// Purposefully restrained UI motion. Particle effects are reserved for
// branch/commitment lifecycle changes so the interface stays professional.
internal static class SegaUiParticleTransition
{
    private sealed class ParticleHost
    {
        public required FrameworkElement Content { get; init; }
        public required Canvas Layer { get; init; }
        public required Color Accent { get; init; }
    }

    private const int MaterializeParticleCount = 10;
    private const int DissolveParticleCount = 18;

    public static bool Enabled { get; set; } = true;

    public static void Attach(Border card, FrameworkElement content, Color accent)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(content);

        Canvas layer = new()
        {
            IsHitTestVisible = false,
            ClipToBounds = false
        };

        Grid host = new()
        {
            ClipToBounds = false
        };

        host.Children.Add(content);
        host.Children.Add(layer);

        card.Child = host;
        card.Tag = new ParticleHost
        {
            Content = content,
            Layer = layer,
            Accent = accent
        };
    }

    public static void PlayMaterialize(Border card, int staggerIndex = 0)
    {
        if (!Enabled)
        {
            card.Opacity = 1.0;
            return;
        }

        if (card.Tag is not ParticleHost host)
            return;

        card.Opacity = 0.0;

        TimeSpan delay = TimeSpan.FromMilliseconds(Math.Min(staggerIndex, 6) * 24);

        card.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(230))
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);

        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            card.Loaded -= loaded;
            PlayMaterializeParticles(card, host, delay);
        };

        if (card.IsLoaded)
            PlayMaterializeParticles(card, host, delay);
        else
            card.Loaded += loaded;
    }

    // Used for a status/text update on an existing card. No particles here:
    // the card is the same object and should feel stable rather than respawn.
    public static void PlaySoftRefresh(Border card)
    {
        if (!Enabled)
            return;

        if (card.Tag is not ParticleHost host)
            return;

        host.Content.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    public static void PlayDissolve(Border card, Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        if (!Enabled)
        {
            completed();
            return;
        }

        if (card.Tag is not ParticleHost host ||
            !card.IsLoaded ||
            card.ActualWidth <= 1.0 ||
            card.ActualHeight <= 1.0)
        {
            completed();
            return;
        }

        host.Layer.Children.Clear();

        double width = card.ActualWidth;
        double height = card.ActualHeight;
        Random random = Random.Shared;

        for (int index = 0; index < DissolveParticleCount; index++)
        {
            double x = 4.0 + random.NextDouble() * Math.Max(1.0, width - 8.0);
            double y = 4.0 + random.NextDouble() * Math.Max(1.0, height - 8.0);
            double size = 1.2 + random.NextDouble() * 2.0;

            Ellipse particle = new()
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(
                    Blend(host.Accent, Color.FromRgb(232, 244, 255), random.NextDouble() * 0.30)),
                Opacity = 0.0
            };

            TranslateTransform drift = new();
            particle.RenderTransform = drift;
            Canvas.SetLeft(particle, x);
            Canvas.SetTop(particle, y);
            host.Layer.Children.Add(particle);

            double dx = (random.NextDouble() - 0.5) * 26.0;
            double dy = -4.0 + (random.NextDouble() - 0.5) * 22.0;
            TimeSpan begin = TimeSpan.FromMilliseconds(random.Next(0, 65));
            TimeSpan duration = TimeSpan.FromMilliseconds(300 + random.Next(0, 120));

            particle.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimationUsingKeyFrames
                {
                    BeginTime = begin,
                    Duration = duration,
                    KeyFrames =
                    {
                        new DiscreteDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)),
                        new EasingDoubleKeyFrame(0.78, KeyTime.FromPercent(0.18)),
                        new EasingDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0))
                    }
                });

            drift.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(0.0, dx, duration)
                {
                    BeginTime = begin,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });

            drift.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0.0, dy, duration)
                {
                    BeginTime = begin,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        host.Content.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            },
            HandoffBehavior.SnapshotAndReplace);

        DoubleAnimation fade = new(1.0, 0.0, TimeSpan.FromMilliseconds(190))
        {
            BeginTime = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fade.Completed += (_, _) =>
        {
            host.Layer.Children.Clear();
            completed();
        };

        card.BeginAnimation(
            UIElement.OpacityProperty,
            fade,
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void PlayMaterializeParticles(Border card, ParticleHost host, TimeSpan delay)
    {
        host.Layer.Children.Clear();

        if (card.ActualWidth <= 1.0 || card.ActualHeight <= 1.0)
            return;

        double width = card.ActualWidth;
        double height = card.ActualHeight;
        Random random = Random.Shared;

        for (int index = 0; index < MaterializeParticleCount; index++)
        {
            double x = 5.0 + random.NextDouble() * Math.Max(1.0, width - 10.0);
            double y = 5.0 + random.NextDouble() * Math.Max(1.0, height - 10.0);
            double size = 1.1 + random.NextDouble() * 1.8;

            Ellipse particle = new()
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(
                    Blend(host.Accent, Color.FromRgb(232, 244, 255), random.NextDouble() * 0.26)),
                Opacity = 0.0
            };

            TranslateTransform movement = new();
            particle.RenderTransform = movement;
            Canvas.SetLeft(particle, x);
            Canvas.SetTop(particle, y);
            host.Layer.Children.Add(particle);

            double angle = random.NextDouble() * Math.PI * 2.0;
            double travel = 8.0 + random.NextDouble() * 18.0;
            TimeSpan particleDelay = delay + TimeSpan.FromMilliseconds(random.Next(0, 60));
            TimeSpan duration = TimeSpan.FromMilliseconds(200 + random.Next(0, 90));

            movement.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(Math.Cos(angle) * travel, 0.0, duration)
                {
                    BeginTime = particleDelay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            movement.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(Math.Sin(angle) * travel, 0.0, duration)
                {
                    BeginTime = particleDelay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            particle.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimationUsingKeyFrames
                {
                    BeginTime = particleDelay,
                    Duration = duration,
                    KeyFrames =
                    {
                        new DiscreteDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)),
                        new EasingDoubleKeyFrame(0.70, KeyTime.FromPercent(0.26)),
                        new EasingDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0))
                    }
                });
        }

        System.Windows.Threading.DispatcherTimer timer = new()
        {
            Interval = delay + TimeSpan.FromMilliseconds(380)
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            host.Layer.Children.Clear();
        };

        timer.Start();
    }

    private static Color Blend(Color left, Color right, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        byte BlendByte(byte a, byte b) =>
            (byte)Math.Round(a + (b - a) * amount);

        return Color.FromRgb(
            BlendByte(left.R, right.R),
            BlendByte(left.G, right.G),
            BlendByte(left.B, right.B));
    }
}

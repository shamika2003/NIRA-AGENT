using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace NIRAAgent.UI.Typing;

public enum NIRAImageTileSkin
{
    Original,
    AetherGlass,
    PrismVeil,
    LumenPanel,
    AuroraMatte
}

/// <summary>
/// NIRA D4 + D1: the original 7x5 pixel cascade and a traveling spectrum wave
/// are applied to the SAME tiles. Each tile has an independently staggered D4
/// cycle, so there is no synchronized full-grid disappearance or sudden reset.
/// This control is shared without modification by Chat and the offline Image UI Lab.
/// </summary>
public sealed class NIRAImageCreationIndicator : FrameworkElement
{
    private const int Columns = 7;
    private const int Rows = 5;
    private const double CascadeSeconds = 4.4; // D4 original reference
    private const double SpectrumSeconds = 2.3; // D1 original reference
    private const double FrameIntervalSeconds = 1.0 / 30.0;

    // Purely visual setting. The timing and sample motion stay identical for
    // all skins. Production now uses Prism Veil as the selected permanent tile skin.
    public static readonly DependencyProperty TileSkinProperty = DependencyProperty.Register(
        nameof(TileSkin), typeof(NIRAImageTileSkin), typeof(NIRAImageCreationIndicator),
        new FrameworkPropertyMetadata(NIRAImageTileSkin.PrismVeil,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public NIRAImageTileSkin TileSkin
    {
        get => (NIRAImageTileSkin)GetValue(TileSkinProperty);
        set => SetValue(TileSkinProperty, value);
    }

    private static readonly (Color Start, Color End)[] Palette =
    {
        (Rgb(0x6D, 0xE0, 0xF7), Rgb(0x55, 0x9A, 0xEA)),
        (Rgb(0xAD, 0xBC, 0xF5), Rgb(0x80, 0x6E, 0xE7)),
        (Rgb(0xFF, 0xD8, 0xA2), Rgb(0xE8, 0xA0, 0xB9)),
        (Rgb(0x9B, 0xE7, 0xF8), Rgb(0x70, 0xBD, 0xDE)),
        (Rgb(0xA5, 0xAD, 0xEF), Rgb(0xCF, 0xBA, 0xF5)),
        (Rgb(0xE3, 0xC7, 0xFF), Rgb(0x93, 0xA4, 0xEE)),
        (Rgb(0x5F, 0xC2, 0xF2), Rgb(0x55, 0xA5, 0xDF)),
        (Rgb(0xFF, 0xD9, 0x9B), Rgb(0xFF, 0xB6, 0xBB)),
        (Rgb(0x86, 0xBD, 0xF2), Rgb(0x8E, 0x8C, 0xEA)),
        (Rgb(0x95, 0xEA, 0xDD), Rgb(0x76, 0xB3, 0xEC))
    };

    private static readonly Brush[] TilePaints = BuildTileBrushes();
    private static readonly Brush[] TileShadows = BuildTileShadows();
    private static readonly Brush[] WavePaints = BuildWaveBrushes();
    private static readonly Pen TileOutline = MakeOutline();
    private static readonly Brush TileShine = Freeze(Color.FromArgb(44, 255, 255, 255));

    // The four new skins are cached once; no per-frame brush creation and no
    // extra timers, compositor subscribers, Ollama calls or Cloudflare work.
    private static readonly TileVisual[][] SkinVisuals = BuildSkinVisuals();

    private readonly Stopwatch _clock = new();
    private bool _subscribed;
    private double _lastRenderSeconds = -1;

    public NIRAImageCreationIndicator()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => RefreshSubscription();
        Unloaded += (_, _) => StopRendering();
        IsVisibleChanged += (_, _) => RefreshSubscription();
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static Brush Freeze(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    private static Pen MakeOutline()
    {
        Pen pen = new(Freeze(Color.FromArgb(108, 255, 255, 255)), 0.7);
        pen.Freeze();
        return pen;
    }

    private static Brush[] BuildTileBrushes()
    {
        Brush[] result = new Brush[Rows * Columns];
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                int k = (c * 3 + r * 2 + (c + r) / 3) % Palette.Length;
                var gradient = new LinearGradientBrush(Palette[k].Start,
                    Palette[(k + 3) % Palette.Length].End,
                    new Point(0, 0), new Point(1, 1));
                gradient.Freeze();
                result[r * Columns + c] = gradient;
            }
        }
        return result;
    }

    private static Brush[] BuildTileShadows()
    {
        Brush[] result = new Brush[Rows * Columns];
        for (int i = 0; i < result.Length; i++)
        {
            Color color = Palette[(i * 7) % Palette.Length].End;
            result[i] = Freeze(Color.FromArgb(34, color.R, color.G, color.B));
        }
        return result;
    }

    private static Brush[] BuildWaveBrushes()
    {
        Brush[] result = new Brush[Rows * Columns];
        for (int i = 0; i < result.Length; i++)
        {
            Color color = Palette[(i * 3 + 1) % Palette.Length].Start;
            result[i] = Freeze(Color.FromArgb(125, color.R, color.G, color.B));
        }
        return result;
    }

    private void RefreshSubscription()
    {
        if (!IsLoaded || !IsVisible)
        {
            StopRendering();
            return;
        }
        if (_subscribed)
            return;

        _clock.Restart();
        _lastRenderSeconds = -1;
        _subscribed = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_subscribed)
            return;
        CompositionTarget.Rendering -= OnRendering;
        _subscribed = false;
        _clock.Stop();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            StopRendering();
            return;
        }
        double now = _clock.Elapsed.TotalSeconds;
        if (_lastRenderSeconds >= 0 && now - _lastRenderSeconds < FrameIntervalSeconds)
            return;
        _lastRenderSeconds = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 35 || height < 35)
            return;

        // Faithful 7x5 tile proportion, aspect-preserving and frameless.
        const double designTileSize = 22.0;
        const double gapRatio = 0.19;
        double tile = Math.Min(designTileSize,
            Math.Min((width - 14) / (Columns + (Columns - 1) * gapRatio),
                     (height - 14) / (Rows + (Rows - 1) * gapRatio)));
        if (tile < 3)
            return;

        double gap = tile * gapRatio;
        double gridW = Columns * tile + (Columns - 1) * gap;
        double gridH = Rows * tile + (Rows - 1) * gap;
        double x0 = (width - gridW) / 2;
        double y0 = (height - gridH) / 2;
        double seconds = _clock.Elapsed.TotalSeconds;

        // D4's light tilt, not D1's separate perspective stage.
        dc.PushTransform(new RotateTransform(3.0, width / 2, height / 2));
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                int index = r * Columns + c;
                Motion motion = SampleMotion(seconds, r, c);
                if (motion.Opacity <= 0.003)
                    continue;

                double size = tile * motion.Scale;
                double x = x0 + c * (tile + gap) + (tile - size) / 2;
                double y = y0 + r * (tile + gap) + (tile - size) / 2 + motion.Y * tile;
                double rounding = Math.Max(1.5, size * 0.15);
                Rect square = new(x, y, size, size);

                dc.PushOpacity(motion.Opacity);
                dc.PushTransform(new RotateTransform(motion.Rotation, x + size / 2, y + size / 2));
                if (TileSkin == NIRAImageTileSkin.Original)
                {
                    // Exact R43 original drawing preserved for production.
                    dc.DrawRoundedRectangle(TileShadows[index], null,
                        new Rect(x, y + 1.5, size, size), rounding, rounding);
                    dc.DrawRoundedRectangle(TilePaints[index], TileOutline, square, rounding, rounding);
                    if (motion.Wave > 0.005)
                    {
                        dc.PushOpacity(motion.Wave * 0.45);
                        dc.DrawRoundedRectangle(WavePaints[index], null, square, rounding, rounding);
                        dc.Pop();
                    }
                    dc.DrawRoundedRectangle(TileShine, null,
                        new Rect(x + size * 0.12, y + size * 0.1,
                                 size * 0.72, Math.Max(1.0, size * 0.085)),
                        rounding * 0.42, rounding * 0.42);
                }
                else
                {
                    DrawSkinTile(dc, (int)TileSkin - 1, index, square, rounding, motion.Wave);
                }
                dc.Pop();
                dc.Pop();
            }
        }
        dc.Pop();
    }


    // Material rendering is intentionally separate from SampleMotion: the D4
    // staggered cascade and D1 spectrum wave are unchanged by tile selection.
    private static void DrawSkinTile(DrawingContext dc, int skinIndex, int index,
        Rect square, double rounding, double wave)
    {
        TileVisual v = SkinVisuals[skinIndex][index];
        double x = square.X;
        double y = square.Y;
        double size = square.Width;

        switch (skinIndex)
        {
            case 0: // Aether Glass: translucent faces, layered edge, fine highlight
                dc.DrawRoundedRectangle(v.Halo, null, Grow(square, size * 0.12),
                    rounding * 1.5, rounding * 1.5);
                dc.DrawRoundedRectangle(v.Shadow, null, new Rect(x, y + size * 0.07, size, size), rounding, rounding);
                dc.DrawRoundedRectangle(v.Fill, v.Border, square, rounding, rounding);
                dc.DrawRoundedRectangle(v.Gloss, null,
                    new Rect(x + size * 0.08, y + size * 0.09, size * 0.84, size * 0.19),
                    rounding * 0.8, rounding * 0.8);
                break;

            case 1: // Prism Veil: iridescent diagonal facets, refracted highlights
                dc.DrawRoundedRectangle(v.Halo, null, Grow(square, size * 0.09),
                    rounding * 1.4, rounding * 1.4);
                dc.DrawRoundedRectangle(v.Fill, v.Border, square, rounding, rounding);
                dc.PushClip(new RectangleGeometry(square, rounding, rounding));
                double bandWidth = size * 0.24;
                double bandX = x - size * 0.20 + wave * size * 0.90;
                dc.DrawRectangle(v.Gloss, null,
                    new Rect(bandX, y - size * 0.06, bandWidth, size * 1.18));
                dc.DrawLine(v.AccentPen, new Point(x + size * 0.10, y + size * 0.76),
                    new Point(x + size * 0.78, y + size * 0.12));
                dc.Pop();
                break;

            case 2: // Lumen Panel: flat luminous micro-panel, sharper geometry
                double crispRadius = Math.Max(1.4, rounding * 0.60);
                dc.DrawRoundedRectangle(v.Halo, null, Grow(square, size * 0.10),
                    crispRadius * 1.6, crispRadius * 1.6);
                dc.DrawRoundedRectangle(v.Fill, v.Border, square, crispRadius, crispRadius);
                dc.DrawRoundedRectangle(v.Gloss, null,
                    new Rect(x + size * 0.13, y + size * 0.13, size * 0.74,
                        Math.Max(1.0, size * 0.095)), size * 0.04, size * 0.04);
                dc.DrawLine(v.AccentPen,
                    new Point(x + size * 0.15, y + size * 0.85),
                    new Point(x + size * 0.50, y + size * 0.85));
                break;

            case 3: // Aurora Matte: diffuse, velvety pastel, no hard shine
                dc.DrawRoundedRectangle(v.Shadow, null,
                    new Rect(x, y + size * 0.05, size, size), rounding, rounding);
                dc.DrawRoundedRectangle(v.Fill, v.Border, square,
                    rounding * 1.25, rounding * 1.25);
                dc.DrawRoundedRectangle(v.Gloss, null,
                    new Rect(x + size * 0.08, y + size * 0.12, size * 0.84, size * 0.68),
                    rounding, rounding);
                break;
        }

        // All four retain R43's D1 traveling wave, changing only its material
        // treatment. No phase, duration, or motion value is altered.
        if (wave > 0.005)
        {
            dc.PushOpacity(wave * (skinIndex == 3 ? 0.23 : 0.40));
            dc.DrawRoundedRectangle(v.Wave, null, square, rounding, rounding);
            dc.Pop();
        }
    }

    private static Rect Grow(Rect r, double extra) =>
        new(r.X - extra / 2, r.Y - extra / 2, r.Width + extra, r.Height + extra);

    private static TileVisual[][] BuildSkinVisuals()
    {
        TileVisual[][] skins = new TileVisual[4][];
        for (int skin = 0; skin < skins.Length; skin++)
        {
            skins[skin] = new TileVisual[Rows * Columns];
            for (int index = 0; index < Rows * Columns; index++)
            {
                int r = index / Columns;
                int c = index % Columns;
                int pi = (c * 3 + r * 2 + (c + r) / 3) % Palette.Length;
                Color first = Palette[pi].Start;
                Color last = Palette[(pi + 3) % Palette.Length].End;
                Color secondary = Palette[(pi + 6) % Palette.Length].Start;

                Brush fill;
                Brush shadow;
                Brush gloss;
                Brush halo;
                Brush wave;
                Pen border;
                Pen accentPen;

                switch (skin)
                {
                    case 0: // Aether Glass
                        fill = Gradient(176, first, 146, last, 0.05, 0.95);
                        shadow = Freeze(From(40, last));
                        gloss = Gradient(112, Colors.White, 0, Colors.White, 0, 1);
                        halo = Freeze(From(26, first));
                        border = MakePen(From(176, Colors.White), 0.78);
                        accentPen = MakePen(From(110, Colors.White), 0.7);
                        wave = Gradient(140, first, 95, secondary, 0, 1);
                        break;
                    case 1: // Prism Veil
                        fill = PrismGradient(first, secondary, last);
                        shadow = Freeze(From(45, secondary));
                        gloss = Gradient(0, Colors.White, 116, Colors.White, 0, 1);
                        halo = Freeze(From(31, secondary));
                        border = MakePen(From(190, Colors.White), 0.80);
                        accentPen = MakePen(From(100, Colors.White), 0.65);
                        wave = Gradient(160, secondary, 128, first, 0, 1);
                        break;
                    case 2: // Lumen Panel
                        fill = Gradient(238, first, 222, last, 0, 1);
                        shadow = Freeze(From(35, last));
                        gloss = Freeze(From(175, Colors.White));
                        halo = Freeze(From(35, first));
                        border = MakePen(From(220, first), 1.05);
                        accentPen = MakePen(From(215, Colors.White), 0.82);
                        wave = Gradient(130, first, 125, secondary, 0, 1);
                        break;
                    default: // Aurora Matte
                        fill = AuroraGradient(first, secondary, last);
                        shadow = Freeze(From(22, last));
                        gloss = Freeze(From(15, Colors.White));
                        halo = Freeze(From(0, first));
                        border = MakePen(From(58, Colors.White), 0.55);
                        accentPen = MakePen(From(0, Colors.White), 0.50);
                        wave = Gradient(90, secondary, 70, first, 0, 1);
                        break;
                }

                skins[skin][index] = new TileVisual(fill, shadow, gloss, halo, wave, border, accentPen);
            }
        }
        return skins;
    }

    private static Color From(byte alpha, Color color) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Pen MakePen(Color color, double thickness)
    {
        Pen pen = new(Freeze(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush Gradient(byte alpha1, Color first, byte alpha2, Color last,
        double start, double end)
    {
        var brush = new LinearGradientBrush();
        brush.StartPoint = new Point(0, start);
        brush.EndPoint = new Point(1, end);
        brush.GradientStops.Add(new GradientStop(From(alpha1, first), 0));
        brush.GradientStops.Add(new GradientStop(From(alpha2, last), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush PrismGradient(Color first, Color middle, Color last)
    {
        var brush = new LinearGradientBrush();
        brush.StartPoint = new Point(0, 0);
        brush.EndPoint = new Point(1, 1);
        brush.GradientStops.Add(new GradientStop(From(234, first), 0));
        brush.GradientStops.Add(new GradientStop(From(246, middle), 0.46));
        brush.GradientStops.Add(new GradientStop(From(232, last), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush AuroraGradient(Color first, Color middle, Color last)
    {
        var brush = new RadialGradientBrush();
        brush.Center = new Point(0.23, 0.20);
        brush.GradientOrigin = new Point(0.15, 0.15);
        brush.RadiusX = 0.92;
        brush.RadiusY = 1.05;
        brush.GradientStops.Add(new GradientStop(From(223, first), 0));
        brush.GradientStops.Add(new GradientStop(From(222, middle), 0.54));
        brush.GradientStops.Add(new GradientStop(From(218, last), 1));
        brush.Freeze();
        return brush;
    }

    private sealed record TileVisual(Brush Fill, Brush Shadow, Brush Gloss, Brush Halo,
        Brush Wave, Pen Border, Pen AccentPen);

    // The HTML source uses staggered *negative* CSS animation delays.
    // Therefore each tile advances along its own 4.4-second cycle rather
    // than waiting for a global row-by-row exit and hard reset.
    internal static Motion SampleMotion(double timeSeconds, int row, int col)
    {
        double cascadeDelay = row * 0.30 + col * 0.095;
        double phase = PositiveModulo(timeSeconds + cascadeDelay, CascadeSeconds) / CascadeSeconds;

        double opacity;
        double y;
        double scale;
        double rotation;

        if (phase < 0.05)
        {
            opacity = 0;
            y = -1.85;
            scale = 0.68;
            rotation = -12;
        }
        else if (phase < 0.26)
        {
            double p = Smooth((phase - 0.05) / 0.21);
            opacity = p;
            y = Lerp(-1.85, 0, p);
            scale = Lerp(0.68, 1.0, p);
            rotation = Lerp(-12, 0, p);
        }
        else if (phase < 0.76)
        {
            opacity = 1.0;
            y = 0;
            scale = 1;
            rotation = 0;
        }
        else if (phase < 0.92)
        {
            double p = Smooth((phase - 0.76) / 0.16);
            opacity = 1.0 - p;
            y = Lerp(0, 0.40, p);
            scale = Lerp(1.0, 0.94, p);
            rotation = 0;
        }
        else
        {
            opacity = 0;
            y = 0.40;
            scale = 0.94;
            rotation = 0;
        }

        // D1 phase: sequential columns/rows; wave runs THROUGH already visible
        // pixels, lifting and brightening them without changing D4's fade cadence.
        double spectrumDelay = col * 0.15 + row * 0.055;
        double wavePhase = PositiveModulo(timeSeconds + spectrumDelay, SpectrumSeconds) / SpectrumSeconds;
        double wave;
        if (wavePhase <= 0.43)
            wave = Smooth(wavePhase / 0.43);
        else if (wavePhase <= 0.70)
            wave = Lerp(1.0, 0.26, Smooth((wavePhase - 0.43) / 0.27));
        else
            wave = Lerp(0.26, 0, Smooth((wavePhase - 0.70) / 0.30));

        // Blend D1 motion in at a restrained amplitude to preserve D4's falling pixels.
        // The extra lift is applied only after the tile has become visible.
        double visible = Smooth(opacity);
        y -= wave * visible * 0.16;
        scale *= 1.0 + wave * visible * 0.055;
        opacity *= 0.87 + 0.13 * wave;

        return new Motion(opacity, y, scale, rotation, wave * visible);
    }

    internal readonly record struct Motion(double Opacity, double Y, double Scale, double Rotation, double Wave);

    internal static bool VerifyMotionInvariants(out string reason)
    {
        // Verify the four keyframes, staggered tiles, and D1 wave. This is
        // deterministic: no UI, Ollama, Cloudflare, network or randomness.
        double d4s = 0.0;
        Motion hidden = SampleMotion(d4s, 0, 0);
        Motion entering = SampleMotion(CascadeSeconds * 0.155, 0, 0);
        Motion settled = SampleMotion(CascadeSeconds * 0.40, 0, 0);
        Motion exiting = SampleMotion(CascadeSeconds * 0.84, 0, 0);
        Motion end = SampleMotion(CascadeSeconds * 0.97, 0, 0);
        if (hidden.Opacity > 0.01 || entering.Opacity <= 0.01 ||
            settled.Opacity < 0.85 || exiting.Opacity >= 0.85 || end.Opacity > 0.01 ||
            entering.Y >= 0 || exiting.Y <= 0)
        {
            reason = "D4 keyframe or fall direction check failed";
            return false;
        }

        Motion staggerA = SampleMotion(0.0, 0, 0);
        Motion staggerB = SampleMotion(0.0, 4, 6);
        if (Math.Abs(staggerA.Opacity - staggerB.Opacity) < 0.08)
        {
            reason = "D4 stagger check failed";
            return false;
        }

        Motion waveA = SampleMotion(1.55, 0, 0);
        Motion waveB = SampleMotion(1.55, 0, 4);
        if (Math.Abs(waveA.Wave - waveB.Wave) < 0.06)
        {
            reason = "D1 traveling wave check failed";
            return false;
        }

        reason = "D4 staggered cascade, fade-out, and D1 spectrum wave: PASS";
        return true;
    }

    private static double PositiveModulo(double a, double b) => ((a % b) + b) % b;
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double Smooth(double p)
    {
        double t = Math.Clamp(p, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace NIRAAgent.UI.Typing;

/// <summary>
/// Final NIRA three-dot typing indicator.
/// Built from the S3 direction: living drift + random slot exchange,
/// with front/back perception driven mostly by size, glow and opacity.
/// Added user-requested vertical cue:
/// - back-going dots rise slightly
/// - front-coming dots lower slightly
/// - only one dot may be back, one front, and one neutral per round
/// while keeping the rest of the movement calm and smooth.
/// </summary>
public sealed class NIRATypingIndicator : FrameworkElement
{
    // Match NIRA's original chat dots: 5px diameter, compact spacing.
    private const double DotSpacing = 13.0;
    private const double BaseRadius = 2.5;
    private const double FrameSeconds = 1.0 / 60.0;

    private static readonly DotMaterial[] Palette =
    {
        CreateMaterial(Color.FromRgb(91, 233, 255)),
        CreateMaterial(Color.FromRgb(102, 157, 255)),
        CreateMaterial(Color.FromRgb(189, 126, 255))
    };

    private readonly Stopwatch _clock = new();
    private readonly Random _random = new(Random.Shared.Next());
    private readonly int[] _startSlots = { 0, 1, 2 };
    private readonly int[] _endSlots = { 0, 1, 2 };
    private readonly int[] _previousSlots = { 0, 1, 2 };
    private readonly double[] _travelDepth = new double[3];
    private readonly double[] _laneDrift = new double[3];
    private bool _attached;
    private bool _planned;
    private double _stepTime;
    private double _runningTime;
    private double _lastClockTime;
    private double _lastRenderTime = -1;
    private int _lastPlanKind = -1;

    public TypingStyle IndicatorStyle { get; set; }
    public double SpeedMultiplier { get; set; } = 1.0;

    public NIRATypingIndicator()
    {
        IsHitTestVisible = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnVisibilityChanged;
    }

    private double CycleSeconds => 2.08;
    private double TravelFraction => 0.85;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        if (!_planned)
            PlanNextExchange();

        double cx = ActualWidth / 2.0;
        double cy = ActualHeight / 2.0;
        double travelDuration = CycleSeconds * TravelFraction;
        double u = Math.Clamp(_stepTime / travelDuration, 0.0, 1.0);
        double move = SmootherStep(u);
        double motionWave = Math.Sin(Math.PI * move);

        DrawState[] states = new DrawState[3];
        for (int dot = 0; dot < 3; dot++)
        {
            double source = _startSlots[dot] - 1.0;
            double destination = _endSlots[dot] - 1.0;
            double delta = destination - source;

            double x = cx + (source + delta * move) * DotSpacing;

            // Keep the living drift from S3.
            double idleDriftX = Math.Sin(_runningTime * 2.0 * Math.PI / 2.55 + dot * 1.97) * 0.85;
            double idleDriftY = Math.Sin(_runningTime * 2.0 * Math.PI / 2.82 + dot * 2.31) * 0.65;
            x += idleDriftX + _laneDrift[dot] * motionWave * 0.17;

            double depth = _travelDepth[dot] * motionWave;

            // User-requested small vertical cue:
            // back-going dot -> rise slightly, front-going dot -> lower slightly.
            // This stays subtle so the effect reads as depth rather than big hopping.
            double depthYOffset = depth * 1.85;
            double y = cy + idleDriftY + depthYOffset;

            double breathing = (1.0 + Math.Sin(_runningTime * 2.0 * Math.PI / 2.10 + dot * 1.72)) * 0.5;

            double radius = (BaseRadius + breathing * 0.11) * (1.0 + depth * 0.17);
            double opacity = Math.Clamp(0.77 + breathing * 0.10 + depth * 0.25, 0.50, 1.0);
            double haloBoost = Math.Max(0.0, depth) * 0.40;
            double highlightBoost = Math.Max(0.0, depth) * 0.16;

            states[dot] = new DrawState(
                dot,
                x,
                y,
                radius,
                opacity,
                haloBoost,
                highlightBoost,
                depth);
        }

        Array.Sort(states, (a, b) => a.Depth.CompareTo(b.Depth));
        foreach (DrawState state in states)
        {
            DrawDot(dc, state, Palette[state.DotIndex]);
        }
    }

    private void PlanNextExchange()
    {
        _planned = true;
        bool threeCycle = _random.NextDouble() < 0.58;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            Array.Copy(_startSlots, _endSlots, 3);
            int kind;
            if (threeCycle)
            {
                int direction = _random.Next(2) == 0 ? 1 : -1;
                kind = direction == 1 ? 3 : 4;
                for (int dot = 0; dot < 3; dot++)
                    _endSlots[dot] = (_startSlots[dot] + direction + 3) % 3;
            }
            else
            {
                int a = _random.Next(3);
                int b = (a + 1 + _random.Next(2)) % 3;
                kind = a * 3 + b + 5;
                (_endSlots[a], _endSlots[b]) = (_endSlots[b], _endSlots[a]);
            }

            bool sameTarget = _endSlots.AsSpan().SequenceEqual(_previousSlots);
            if ((!sameTarget && kind != _lastPlanKind) || attempt == 11)
            {
                _lastPlanKind = kind;
                break;
            }
            threeCycle = _random.NextDouble() < 0.55;
        }

        Array.Copy(_endSlots, _previousSlots, 3);

        // Exactly one dot may go "back", exactly one may come "front",
        // and the remaining dot stays neutral for that round.
        // This avoids the bug where two dots felt like they were both going
        // back or both coming forward at the same time.
        Array.Fill(_travelDepth, 0.0);
        Array.Fill(_laneDrift, 0.0);

        List<int> movers = new();
        List<int> stationary = new();
        for (int dot = 0; dot < 3; dot++)
        {
            if (_endSlots[dot] == _startSlots[dot])
                stationary.Add(dot);
            else
                movers.Add(dot);
        }

        int frontDot;
        int backDot;
        int neutralDot;

        if (movers.Count == 2)
        {
            // Pair swap: the untouched dot is always the neutral one.
            neutralDot = stationary[0];
            if (_random.Next(2) == 0)
            {
                frontDot = movers[0];
                backDot = movers[1];
            }
            else
            {
                frontDot = movers[1];
                backDot = movers[0];
            }
        }
        else
        {
            // Three-way exchange: choose one front, one back, one neutral.
            List<int> order = new() { 0, 1, 2 };
            for (int i = order.Count - 1; i > 0; i--)
            {
                int swapIndex = _random.Next(i + 1);
                (order[i], order[swapIndex]) = (order[swapIndex], order[i]);
            }

            frontDot = order[0];
            backDot = order[1];
            neutralDot = order[2];
        }

        _travelDepth[frontDot] = 1.04;
        _travelDepth[backDot] = -1.04;
        _travelDepth[neutralDot] = 0.0;

        foreach (int dot in movers)
        {
            _laneDrift[dot] = (_random.NextDouble() - 0.5) * 2.8;
        }
    }

    // WPF keeps old chat bubbles in the visual tree. Subscribe to the
    // composition loop only while this particular empty-message indicator
    // is actually visible; once text arrives its visibility collapses.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateRenderingSubscription();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopRendering();
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateRenderingSubscription();
    }

    private void UpdateRenderingSubscription()
    {
        if (!IsLoaded || !IsVisible)
        {
            StopRendering();
            return;
        }

        if (_attached)
            return;

        _attached = true;
        _lastClockTime = _clock.Elapsed.TotalSeconds;
        _clock.Start();
        _lastRenderTime = -1;
        CompositionTarget.Rendering += OnFrame;
        InvalidateVisual();
    }

    private void StopRendering()
    {
        if (!_attached)
            return;

        _attached = false;
        CompositionTarget.Rendering -= OnFrame;
        _clock.Stop();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double delta = Math.Clamp(now - _lastClockTime, 0.0, 0.10);
        _lastClockTime = now;

        if (_lastRenderTime >= 0 && now - _lastRenderTime < FrameSeconds * 0.85)
            return;
        _lastRenderTime = now;

        double advance = delta * Math.Clamp(SpeedMultiplier, 0.85, 1.20);
        _runningTime += advance;
        _stepTime += advance;

        while (_stepTime >= CycleSeconds)
        {
            _stepTime -= CycleSeconds;
            Array.Copy(_endSlots, _startSlots, 3);
            PlanNextExchange();
        }

        InvalidateVisual();
    }

    private static double SmootherStep(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
    }

    private static void DrawDot(DrawingContext dc, DrawState state, DotMaterial material)
    {
        Point point = new(state.X, state.Y);

        double haloOpacity = (0.34 + state.HaloBoost) * state.Opacity;
        dc.PushOpacity(Math.Clamp(haloOpacity, 0.14, 0.92));
        dc.DrawEllipse(material.Halo, null, point, state.Radius * 2.45, state.Radius * 2.45);
        dc.Pop();

        dc.PushOpacity(Math.Clamp(state.Opacity, 0.46, 1.0));
        dc.DrawEllipse(material.Core, null, point, state.Radius, state.Radius);
        dc.DrawEllipse(
            material.Highlight,
            null,
            new Point(state.X - state.Radius * 0.21, state.Y - state.Radius * 0.23),
            state.Radius * (0.18 + state.HighlightBoost),
            state.Radius * (0.18 + state.HighlightBoost));
        dc.Pop();
    }

    private static DotMaterial CreateMaterial(Color color)
    {
        SolidColorBrush core = new(color);
        core.Freeze();

        SolidColorBrush highlight = new(Color.FromArgb(155, 231, 245, 255));
        highlight.Freeze();

        RadialGradientBrush halo = new()
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(88, color.R, color.G, color.B), 0.0));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(26, color.R, color.G, color.B), 0.48));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0));
        halo.Freeze();
        return new DotMaterial(core, halo, highlight);
    }

    private readonly record struct DrawState(
        int DotIndex,
        double X,
        double Y,
        double Radius,
        double Opacity,
        double HaloBoost,
        double HighlightBoost,
        double Depth);

    private sealed record DotMaterial(Brush Core, Brush Halo, Brush Highlight);
}

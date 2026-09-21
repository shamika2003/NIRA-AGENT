/*
 * filename: NIRAVisualToastPlacementService.cs
 */

using NIRAAgent.PC.Awareness;

namespace NIRAAgent.UI.Visuals;

// =============================================================
// LOW-DISRUPTION VISUAL PEEK PLACEMENT
//
// Chooses a physical-pixel rectangle inside the active monitor work area.
// It prefers genuinely free strips around the current foreground window,
// then falls back to the least-overlapping corner while avoiding NIRA's
// body and the user's current pointer when possible.
// =============================================================

public sealed class NIRAVisualToastPlacementService
{
    private const int Margin =
        18;


    private readonly PcWorldStateService
        _worldState;


    public NIRAVisualToastPlacementService(
        PcWorldStateService worldState)
    {
        _worldState =
            worldState
            ?? throw new ArgumentNullException(
                nameof(worldState));
    }


    public PcWorldState CurrentWorld =>
        _worldState.Current;


    public bool ShouldShowDesktopPeek()
    {
        PcWorldState world =
            _worldState.Current;

        // A full-screen foreground surface is usually the strongest signal
        // that an unsolicited overlay would be distracting. The artifact still
        // remains available in chat.
        return !world.ForegroundWindow.IsFullscreen;
    }


    public PcRectangle Resolve(
        int width,
        int height)
    {
        PcWorldState world =
            _worldState.Current;

        PcRectangle work =
            world.Display.WorkArea;

        if (work.IsEmpty)
        {
            work =
                world.Display.MonitorBounds;
        }

        width =
            Math.Clamp(
                width,
                220,
                Math.Max(220, work.Width - Margin * 2));

        height =
            Math.Clamp(
                height,
                160,
                Math.Max(160, work.Height - Margin * 2));

        PcRectangle foreground =
            world.ForegroundWindow.Bounds;

        PcRectangle NIRA =
            world.NIRA.WindowBounds;

        List<PcRectangle> candidates =
            new();

        AddFreeStripCandidates(
            candidates,
            work,
            foreground,
            width,
            height);

        AddCornerCandidates(
            candidates,
            work,
            width,
            height);

        if (candidates.Count == 0)
        {
            return Clamp(
                new PcRectangle(
                    work.Right - width - Margin,
                    work.Top + Margin,
                    work.Right - Margin,
                    work.Top + Margin + height),
                work,
                width,
                height);
        }

        return candidates
            .Select(
                candidate =>
                    new
                    {
                        Candidate = Clamp(
                            candidate,
                            work,
                            width,
                            height),
                        Score = Score(
                            candidate,
                            foreground,
                            NIRA,
                            world.Mouse.X,
                            world.Mouse.Y)
                    })
            .OrderBy(item => item.Score)
            .ThenByDescending(
                item => item.Candidate.Left)
            .ThenBy(
                item => item.Candidate.Top)
            .First()
            .Candidate;
    }


    private static void AddFreeStripCandidates(
        ICollection<PcRectangle> candidates,
        PcRectangle work,
        PcRectangle foreground,
        int width,
        int height)
    {
        if (foreground.IsEmpty)
        {
            return;
        }

        // Right of foreground.
        if (work.Right - foreground.Right >=
            width + Margin * 2)
        {
            candidates.Add(
                FromTopLeft(
                    work.Right - width - Margin,
                    work.Top + Margin,
                    width,
                    height));

            candidates.Add(
                FromTopLeft(
                    work.Right - width - Margin,
                    work.Bottom - height - Margin,
                    width,
                    height));
        }

        // Left of foreground.
        if (foreground.Left - work.Left >=
            width + Margin * 2)
        {
            candidates.Add(
                FromTopLeft(
                    work.Left + Margin,
                    work.Top + Margin,
                    width,
                    height));

            candidates.Add(
                FromTopLeft(
                    work.Left + Margin,
                    work.Bottom - height - Margin,
                    width,
                    height));
        }

        // Above foreground.
        if (foreground.Top - work.Top >=
            height + Margin * 2)
        {
            candidates.Add(
                FromTopLeft(
                    work.Right - width - Margin,
                    work.Top + Margin,
                    width,
                    height));
        }

        // Below foreground.
        if (work.Bottom - foreground.Bottom >=
            height + Margin * 2)
        {
            candidates.Add(
                FromTopLeft(
                    work.Right - width - Margin,
                    work.Bottom - height - Margin,
                    width,
                    height));
        }
    }


    private static void AddCornerCandidates(
        ICollection<PcRectangle> candidates,
        PcRectangle work,
        int width,
        int height)
    {
        candidates.Add(
            FromTopLeft(
                work.Right - width - Margin,
                work.Top + Margin,
                width,
                height));

        candidates.Add(
            FromTopLeft(
                work.Right - width - Margin,
                work.Bottom - height - Margin,
                width,
                height));

        candidates.Add(
            FromTopLeft(
                work.Left + Margin,
                work.Top + Margin,
                width,
                height));

        candidates.Add(
            FromTopLeft(
                work.Left + Margin,
                work.Bottom - height - Margin,
                width,
                height));

        candidates.Add(
            FromTopLeft(
                work.Right - width - Margin,
                work.Top + Math.Max(
                    Margin,
                    (work.Height - height) / 2),
                width,
                height));

        candidates.Add(
            FromTopLeft(
                work.Left + Margin,
                work.Top + Math.Max(
                    Margin,
                    (work.Height - height) / 2),
                width,
                height));
    }


    private static double Score(
        PcRectangle candidate,
        PcRectangle foreground,
        PcRectangle NIRA,
        int mouseX,
        int mouseY)
    {
        double score =
            0.0;

        if (!foreground.IsEmpty)
        {
            score +=
                OverlapRatio(
                    candidate,
                    foreground)
                *
                10000.0;
        }

        if (!NIRA.IsEmpty)
        {
            score +=
                OverlapRatio(
                    candidate,
                    NIRA)
                *
                18000.0;
        }

        double centerX =
            (candidate.Left + candidate.Right) /
            2.0;

        double centerY =
            (candidate.Top + candidate.Bottom) /
            2.0;

        double distance =
            Math.Sqrt(
                Math.Pow(centerX - mouseX, 2.0)
                +
                Math.Pow(centerY - mouseY, 2.0));

        // Prefer being farther from the user's current pointer.
        score +=
            1200.0 /
            Math.Max(
                60.0,
                distance);

        return score;
    }


    private static double OverlapRatio(
        PcRectangle a,
        PcRectangle b)
    {
        int left =
            Math.Max(a.Left, b.Left);

        int top =
            Math.Max(a.Top, b.Top);

        int right =
            Math.Min(a.Right, b.Right);

        int bottom =
            Math.Min(a.Bottom, b.Bottom);

        if (right <= left
            || bottom <= top)
        {
            return 0.0;
        }

        double overlap =
            (right - left)
            *
            (double)(bottom - top);

        double area =
            Math.Max(
                1.0,
                a.Width * (double)a.Height);

        return overlap /
            area;
    }


    private static PcRectangle FromTopLeft(
        int left,
        int top,
        int width,
        int height)
    {
        return new PcRectangle(
            left,
            top,
            left + width,
            top + height);
    }


    private static PcRectangle Clamp(
        PcRectangle rectangle,
        PcRectangle work,
        int width,
        int height)
    {
        int left =
            Math.Clamp(
                rectangle.Left,
                work.Left + Margin,
                Math.Max(
                    work.Left + Margin,
                    work.Right - width - Margin));

        int top =
            Math.Clamp(
                rectangle.Top,
                work.Top + Margin,
                Math.Max(
                    work.Top + Margin,
                    work.Bottom - height - Margin));

        return FromTopLeft(
            left,
            top,
            width,
            height);
    }
}


/*
 * filename: SegaVisualObservationContracts.cs
 */

using SegaAgent.PC.Awareness;

namespace SegaAgent.Vision;


// =============================================================
// STRUCTURED VISUAL OBSERVATION
//
// These records contain model-derived visual observations that are
// grounded to one immutable SegaVisualEvidence capture. They are
// evidence, not OS authority and not executable UI instructions.
// =============================================================

public sealed record SegaVisualNormalizedRectangle
{
    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }

    public bool IsValid =>
        Right > Left &&
        Bottom > Top;
}


public sealed record SegaVisualElementObservation
{
    public string Kind { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public double Confidence { get; init; }

    // Coordinates are normalized to the captured image in a 0..1000 space.
    public SegaVisualNormalizedRectangle? BoundsNormalized { get; init; }

    // Runtime-derived screen-space projection of BoundsNormalized.
    public PcRectangle? ScreenBounds { get; init; }
}


public sealed record SegaVisualObservation
{
    public Guid ObservationId { get; init; }
    public Guid EvidenceId { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
    public string Model { get; init; } = string.Empty;
    public string Question { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;
    public string ApplicationContent { get; init; } = string.Empty;

    public IReadOnlyList<string> VisibleText { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<SegaVisualElementObservation> Elements { get; init; } =
        Array.Empty<SegaVisualElementObservation>();

    public IReadOnlyList<string> Issues { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> Limitations { get; init; } =
        Array.Empty<string>();

    public double Confidence { get; init; }

    // Copy of the capture provenance used for this observation.
    public long WorldVersion { get; init; }
    public string ImageSha256 { get; init; } = string.Empty;
    public PcRectangle CaptureBounds { get; init; }
    public string ForegroundProcessName { get; init; } = string.Empty;
    public int ForegroundProcessId { get; init; }
    public string ForegroundWindowTitle { get; init; } = string.Empty;
}

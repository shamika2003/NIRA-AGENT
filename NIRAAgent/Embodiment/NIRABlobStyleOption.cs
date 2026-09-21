/*
 * filename: NIRABlobStyleOption.cs
 */

namespace NIRAAgent.Embodiment;

public sealed record NIRABlobStyleOption(
    NIRABlobStyleId Id,
    string Title,
    string Subtitle,
    string Description,
    NIRABlobPresetId PreviewPreset);


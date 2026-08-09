/*
 * filename: PerceptionEvent.cs
 */

using SegaAgent.PC.Awareness;

namespace SegaAgent.Perception;

public sealed class PerceptionEvent
{

    public string Type { get; init; } =
        string.Empty;


    public string Description { get; init; } =
        string.Empty;


    public PcState CurrentState { get; init; } = null!;

}

/*
 * filename: SegaSocialHistorySnapshot.cs
 */

namespace SegaAgent.Character.History;

public sealed record SegaSocialHistorySnapshot
{
    public long Version
    {
        get;
        init;
    }


    public DateTimeOffset UpdatedAt
    {
        get;
        init;
    }


    public IReadOnlyList<
        SegaSocialEvent>
        Events
    {
        get;
        init;
    } =
        Array.Empty<
            SegaSocialEvent>();


    public int Count =>
        Events.Count;
}
/*
 * filename: NIRASocialHistorySnapshot.cs
 */

namespace NIRAAgent.Character.History;

public sealed record NIRASocialHistorySnapshot
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
        NIRASocialEvent>
        Events
    {
        get;
        init;
    } =
        Array.Empty<
            NIRASocialEvent>();


    public int Count =>
        Events.Count;
}

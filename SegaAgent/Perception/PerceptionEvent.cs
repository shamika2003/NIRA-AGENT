/*
 * filename: PerceptionEvent.cs
 */

using SegaAgent.PC.Awareness;

namespace SegaAgent.Perception;

public sealed class PerceptionEvent
{
    public string Type
    {
        get;
        init;
    } =
        string.Empty;


    public string TopicKey
    {
        get;
        init;
    } =
        string.Empty;


    public string Description
    {
        get;
        init;
    } =
        string.Empty;


    public IReadOnlyDictionary<
        string,
        string>
        Metadata
    {
        get;
        init;
    } =
        new Dictionary<
            string,
            string>();


    public PcWorldState CurrentState
    {
        get;
        init;
    } =
        null!;


    /*
     * Connects an accepted perception/proactive request back
     * to the exact social-history event that created it.
     */

    public Guid? SocialEventId
    {
        get;
        set;
    }
}
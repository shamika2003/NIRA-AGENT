namespace NIRAAgent.Mind;

/// <summary>A single agentic execution owner for user, application and internal events.</summary>
public interface INIRAAgentExecutionHost
{
    IAsyncEnumerable<NIRAOutputChunk> RunAsync(NIRAMindEvent mindEvent,
        CancellationToken cancellationToken);
}

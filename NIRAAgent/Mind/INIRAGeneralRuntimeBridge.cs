using System.Collections.Generic;
using System.Threading;

namespace NIRAAgent.Mind;

/// <summary>
/// One host-owned migration seam between NIRA's existing identity/UI pipeline
/// and the checkpointed general-agent runtime. This is not a second identity.
/// A bridge may handle only explicitly authorized user turns.
/// </summary>
public interface INIRAGeneralRuntimeBridge
{
    bool CanHandle(NIRAMindEvent mindEvent);
    IAsyncEnumerable<NIRAOutputChunk> RunAsync(NIRAMindEvent mindEvent,
        CancellationToken cancellationToken);
}

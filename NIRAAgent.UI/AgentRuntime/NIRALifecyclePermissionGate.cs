using System.IO;
using System.Text.Json;
using NIRAAgent.Runtime.Contracts;
using NIRAAgent.Runtime.HostAdapters;
using NIRAAgent.Runtime.Runtime.AgentLoop;

namespace NIRAAgent.UI.AgentRuntime;

/// <summary>
/// A saved goal does not grant PC authority. This gate permits only an
/// internal-event-bound lifecycle proposal; queued work is independently
/// authorized when the existing branch runner executes that capability.
/// </summary>
internal sealed class NIRALifecyclePermissionGate : IAgentPermissionGate
{
    private readonly IAgentPermissionGate _next;
    private readonly AgentPayloadVault _vault;
    private readonly NIRALifecycleCapabilities.Change _tool;
    public NIRALifecyclePermissionGate(IAgentPermissionGate next,
        AgentPayloadVault vault, NIRALifecycleCapabilities.Change tool)
    { _next = next; _vault = vault; _tool = tool; }

    public async Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec spec,
        string reference, CancellationToken ct)
    {
        if (spec.Id != _tool.Spec.Id)
            return await _next.IsAuthorizedAsync(runId, spec, reference, ct);
        if (runId == Guid.Empty || spec != _tool.Spec || spec.Access != AccessLevel.Modify)
            return false;
        try
        {
            if (_vault.RunIdFromArgument(reference) != runId) return false;
            JsonElement args = await _vault.ReadToolArgumentAsync(spec.Id, reference, ct);
            return _tool.IsBoundAndSafe(args);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException or ArgumentException)
        { return false; }
    }
}

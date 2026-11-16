using System.IO;
using System.Text.Json;
using NIRAAgent.Runtime.Contracts;
using NIRAAgent.Runtime.HostAdapters;
using NIRAAgent.Runtime.Runtime.AgentLoop;

namespace NIRAAgent.UI.AgentRuntime;

/// <summary>
/// An INTERNAL work-intention write, not an external action grant.
/// Separate per-launch consent is mandatory. This never authorizes other tools.
/// The existing goal owner still verifies the state before/after commit.
/// </summary>
internal sealed class NIRASelfGoalPermissionGate : IAgentPermissionGate
{
    private readonly IAgentPermissionGate _previous;
    private readonly AgentPayloadVault _vault;
    private readonly NIRASelfGoalCapability _tool;

    public NIRASelfGoalPermissionGate(IAgentPermissionGate previous,
        AgentPayloadVault vault, NIRASelfGoalCapability tool)
    {
        _previous = previous ?? throw new ArgumentNullException(nameof(previous));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
    }

    public async Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec spec,
        string reference, CancellationToken ct)
    {
        if (spec.Id != _tool.Spec.Id)
            return await _previous.IsAuthorizedAsync(runId, spec, reference, ct);
        if (runId == Guid.Empty || spec != _tool.Spec ||
            spec.Access != AccessLevel.Modify) return false;
        try
        {
            if (_vault.RunIdFromArgument(reference) != runId) return false;
            JsonElement args = await _vault.ReadToolArgumentAsync(spec.Id, reference, ct);
            return _tool.IsSafeToPlan(args);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException) { return false; }
    }
}

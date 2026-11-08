using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NIRAAgent.Authorization;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.LegacyAdapters;
using Nira.V2.Runtime.AgentLoop;

namespace NIRAAgent.UI.V2;

/// <summary>Fresh, one-use owner approval for an exact current-turn goal/branch update.</summary>
internal sealed class NIRAV2LifecycleMutationPermissionGate : IAgentPermissionGate
{
    private readonly IAgentPermissionGate _previous;
    private readonly AgentPayloadVault _vault;
    private readonly LegacyLifecycleMutationCapability _tool;
    private readonly NIRACapabilityApprovalBroker _broker;

    public NIRAV2LifecycleMutationPermissionGate(IAgentPermissionGate previous,
        AgentPayloadVault vault, LegacyLifecycleMutationCapability tool,
        NIRACapabilityApprovalBroker broker)
    {
        _previous = previous ?? throw new ArgumentNullException(nameof(previous));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    public async Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool,
        string reference, CancellationToken ct)
    {
        if (tool.Id != _tool.Spec.Id)
            return await _previous.IsAuthorizedAsync(runId, tool, reference, ct);
        if (runId == Guid.Empty || tool != _tool.Spec ||
            tool.Access != AccessLevel.Modify || !_tool.IsStillAllowed()) return false;
        try
        {
            if (_vault.RunIdFromArgument(reference) != runId ||
                !_tool.ValidateArguments(await _vault.ReadToolArgumentAsync(
                    tool.Id, reference, ct))) return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException) { return false; }

        var req = _tool.ApprovalRequest;
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            runId.ToString("N") + ":" + req.Arguments.GetRawText())));
        string type = req.Arguments.GetProperty("itemType").GetString()!;
        string id = req.Arguments.GetProperty("itemId").GetString()!;
        var operation = new NIRAAuthorityOperation
        {
            Request = req,
            Risk = NIRACapabilityRisk.Modify,
            Fingerprint = digest,
            Target = type + ":" + id,
            CanRemember = false,
            RequiresExplicitAuthorization = true,
            Notice = "Phase 4H.4: Allow ONCE to record the displayed EXACT change to " +
                "this existing goal/branch. No activation, completion or execution. " +
                "Inspect the item ID, operation and evidence. Remember is disabled."
        };
        var approval = await _broker.RequestAsync(operation, ct);
        return approval.Choice == NIRAApprovalChoice.AllowOnce && _tool.IsStillAllowed();
    }
}

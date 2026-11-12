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

/// <summary>One-use explicit owner approval; never a remembered permission.</summary>
internal sealed class NIRAV2BranchCompletionPermissionGate : IAgentPermissionGate
{
    private readonly IAgentPermissionGate _previous;
    private readonly AgentPayloadVault _vault;
    private readonly LegacyBranchCompletionCapability _tool;
    private readonly NIRACapabilityApprovalBroker _broker;

    public NIRAV2BranchCompletionPermissionGate(IAgentPermissionGate previous,
        AgentPayloadVault vault, LegacyBranchCompletionCapability tool,
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
        if (runId == Guid.Empty || tool != _tool.Spec || tool.Access != AccessLevel.Modify)
            return false;
        try
        {
            if (_vault.RunIdFromArgument(reference) != runId ||
                !_tool.ValidateArguments(await _vault.ReadToolArgumentAsync(tool.Id, reference, ct)) ||
                !await _tool.CanApproveAsync(ct)) return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException or InvalidOperationException) { return false; }

        var req = _tool.ApprovalRequest;
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            runId.ToString("N") + ":" + req.Arguments.GetRawText())));
        string id = req.Arguments.GetProperty("branchId").GetString()!;
        var operation = new NIRAAuthorityOperation
        {
            Request = req,
            Risk = NIRACapabilityRisk.Modify,
            Fingerprint = digest,
            Target = "branch:" + id,
            CanRemember = false,
            RequiresExplicitAuthorization = true,
            Notice = "Phase 4H.6: Allow ONCE to mark this exact branch COMPLETED. " +
                "Review its GUID, goal, result and evidence. This is terminal; " +
                "the evidence text is user-attested. Remember is disabled."
        };
        var approval = await _broker.RequestAsync(operation, ct);
        return approval.Choice == NIRAApprovalChoice.AllowOnce &&
            await _tool.CanApproveAsync(ct);
    }
}

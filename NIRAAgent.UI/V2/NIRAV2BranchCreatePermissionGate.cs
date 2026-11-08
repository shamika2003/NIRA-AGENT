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

/// <summary>Fresh owner approval; one observed parent goal and exact immutable branch payload.</summary>
internal sealed class NIRAV2BranchCreatePermissionGate : IAgentPermissionGate
{
    private readonly IAgentPermissionGate _previous;
    private readonly AgentPayloadVault _vault;
    private readonly LegacyBranchCreateCapability _branch;
    private readonly NIRACapabilityApprovalBroker _broker;

    public NIRAV2BranchCreatePermissionGate(IAgentPermissionGate previous,
        AgentPayloadVault vault, LegacyBranchCreateCapability branch,
        NIRACapabilityApprovalBroker broker)
    {
        _previous = previous ?? throw new ArgumentNullException(nameof(previous));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _branch = branch ?? throw new ArgumentNullException(nameof(branch));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    public async Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool,
        string reference, CancellationToken ct)
    {
        if (tool.Id != _branch.Spec.Id)
            return await _previous.IsAuthorizedAsync(runId, tool, reference, ct);
        if (runId == Guid.Empty || tool != _branch.Spec ||
            tool.Access != AccessLevel.Modify || !_branch.IsStillAllowed()) return false;
        try
        {
            if (_vault.RunIdFromArgument(reference) != runId ||
                !_branch.ValidateArguments(await _vault.ReadToolArgumentAsync(
                    tool.Id, reference, ct))) return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException)
        { return false; }

        NIRACapabilityRequest req = _branch.ApprovalRequest;
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            runId.ToString("N") + ":" + req.Arguments.GetRawText())));
        var operation = new NIRAAuthorityOperation
        {
            Request = req,
            Risk = NIRACapabilityRisk.Modify,
            Fingerprint = digest,
            Target = req.Arguments.GetProperty("goalId").GetString()! + ":" +
                     req.Arguments.GetProperty("objective").GetString()!,
            CanRemember = false,
            RequiresExplicitAuthorization = true,
            Notice = "Phase 4H.3: Allow ONCE to create this exact REQUIRED branch under " +
                "the displayed parent goal. Starts WAITING; no work dispatched. " +
                "Review the objective and success criterion. Remember is disabled."
        };
        NIRAApprovalResponse approval = await _broker.RequestAsync(operation, ct);
        return approval.Choice == NIRAApprovalChoice.AllowOnce && _branch.IsStillAllowed();
    }
}

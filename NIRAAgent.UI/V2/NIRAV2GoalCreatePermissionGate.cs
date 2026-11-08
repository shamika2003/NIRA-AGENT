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

/// <summary>Composes with existing file/DOCX/desktop permissions.
/// The model cannot grant permission or select the goal: only the trusted
/// host selects the exact objective and the existing WPF broker approves it.</summary>
internal sealed class NIRAV2GoalCreatePermissionGate : IAgentPermissionGate
{
    private readonly IAgentPermissionGate _previous;
    private readonly AgentPayloadVault _vault;
    private readonly LegacyGoalCreateCapability _goal;
    private readonly NIRACapabilityApprovalBroker _broker;

    public NIRAV2GoalCreatePermissionGate(IAgentPermissionGate previous,
        AgentPayloadVault vault, LegacyGoalCreateCapability goal,
        NIRACapabilityApprovalBroker broker)
    {
        _previous = previous ?? throw new ArgumentNullException(nameof(previous));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _goal = goal ?? throw new ArgumentNullException(nameof(goal));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    public async Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool,
        string reference, CancellationToken cancellationToken)
    {
        if (tool.Id != _goal.Spec.Id)
            return await _previous.IsAuthorizedAsync(runId, tool, reference, cancellationToken);
        if (runId == Guid.Empty || tool != _goal.Spec ||
            tool.Access != AccessLevel.Modify || !_goal.IsSafeToCreate()) return false;
        try
        {
            if (_vault.RunIdFromArgument(reference) != runId ||
                !_goal.ValidateArguments(await _vault.ReadToolArgumentAsync(
                    tool.Id, reference, cancellationToken))) return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException)
        { return false; }

        NIRACapabilityRequest request = _goal.ApprovalRequest;
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            runId.ToString("N") + ":" + request.Arguments.GetRawText())));
        var operation = new NIRAAuthorityOperation
        {
            Request = request,
            Risk = NIRACapabilityRisk.Modify,
            Fingerprint = digest,
            Target = request.Arguments.GetProperty("objective").GetString()!,
            CanRemember = false,
            RequiresExplicitAuthorization = true,
            Notice = "Phase 4H.2: Allow ONCE only to create this exact saved goal and " +
                "success criterion. Initial state is WAITING (no automatic work). " +
                "This is a persistent personal-state change; never use Remember."
        };
        NIRAApprovalResponse response = await _broker.RequestAsync(operation, cancellationToken);
        return response.Choice == NIRAApprovalChoice.AllowOnce && _goal.IsSafeToCreate();
    }
}

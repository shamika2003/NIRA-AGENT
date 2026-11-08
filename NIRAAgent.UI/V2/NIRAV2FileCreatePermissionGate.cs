using System.IO;
using System.Security.Cryptography;
using System.Text;
using NIRAAgent.Authorization;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.LegacyAdapters;
using Nira.V2.Runtime.AgentLoop;

namespace NIRAAgent.UI.V2;

/// <summary>Independent explicit approval boundary, even when legacy owner autonomy
/// would otherwise authorize a direct create. The trusted existing WPF approval toast
/// displays the exact path, content and create-only request. Never Remember.</summary>
internal sealed class NIRAV2FileCreatePermissionGate : IAgentPermissionGate
{
    private readonly AgentPayloadVault _vault;
    private readonly LegacyTextCreateCapability _create;
    private readonly NIRACapabilityApprovalBroker _broker;

    public NIRAV2FileCreatePermissionGate(AgentPayloadVault vault,
        LegacyTextCreateCapability create, NIRACapabilityApprovalBroker broker)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    public async Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool,
        string argumentReference, CancellationToken cancellationToken)
    {
        if (tool.Access == AccessLevel.Observe) return true;
        if (runId == Guid.Empty || tool != _create.Spec ||
            tool.Id != "legacy.filesystem.create_text_exact") return false;
        try
        {
            if (_vault.RunIdFromArgument(argumentReference) != runId ||
                !_create.ValidateArguments(await _vault.ReadToolArgumentAsync(
                    tool.Id, argumentReference, cancellationToken)) ||
                !_create.IsSafeToCreate(cancellationToken)) return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or System.Text.Json.JsonException)
        { return false; }

        NIRACapabilityRequest request = _create.ApprovalRequest;
        string fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(runId.ToString("N") + ":" +
                request.Arguments.GetRawText())));
        var operation = new NIRAAuthorityOperation
        {
            Request = request,
            Risk = NIRACapabilityRisk.Modify,
            Fingerprint = fingerprint,
            Paths = [request.Arguments.GetProperty("path").GetString()!],
            Target = request.Arguments.GetProperty("path").GetString()!,
            CanRemember = false,
            RequiresExplicitAuthorization = true,
            Notice = "Phase 4F: Approve ONCE only if the exact new path and content " +
                     "in Details match your request. Existing files must never be replaced."
        };
        NIRAApprovalResponse answer = await _broker.RequestAsync(operation, cancellationToken);
        return answer.Choice == NIRAApprovalChoice.AllowOnce &&
               _create.IsSafeToCreate(cancellationToken);
    }
}

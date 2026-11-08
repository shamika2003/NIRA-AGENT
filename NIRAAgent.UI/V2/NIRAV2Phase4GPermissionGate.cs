using System.IO;
using System.Security.Cryptography;
using System.Text;
using NIRAAgent.Authorization;
using Nira.V2.Contracts;
using Nira.V2.LegacyAdapters;
using Nira.V2.Runtime.AgentLoop;

namespace NIRAAgent.UI.V2;

/// <summary>Approval remains the existing NIRA WPF broker, never a V2 permission store.
/// The already-verified Phase 4F gate is preserved for .txt writes.
/// DOCX creation cannot proceed without an independent Allow Once decision.</summary>
internal sealed class NIRAV2Phase4GPermissionGate : IAgentPermissionGate
{
    private readonly IAgentPermissionGate _textCreateGate;
    private readonly AgentPayloadVault _vault;
    private readonly LegacyPhase4GDocxCreateCapability? _docx;
    private readonly LegacyPhase4GDesktopActivateCapability? _desktop;
    private readonly NIRACapabilityApprovalBroker _broker;

    // Distinguish a missing trusted HWND from a real owner Deny/timeout.
    // This does not grant authorization or change the broker's decision.
    public bool DesktopObservationMissing { get; private set; }

    public NIRAV2Phase4GPermissionGate(IAgentPermissionGate textCreateGate,
        AgentPayloadVault vault, LegacyPhase4GDocxCreateCapability? docx,
        LegacyPhase4GDesktopActivateCapability? desktop, NIRACapabilityApprovalBroker broker)
    {
        _textCreateGate = textCreateGate ?? throw new ArgumentNullException(nameof(textCreateGate));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _docx = docx;
        _desktop = desktop;
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    public async Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool,
        string reference, CancellationToken ct)
    {
        if (tool.Access == AccessLevel.Observe) return true;
        if (tool.Id == "legacy.filesystem.create_text_exact")
            return await _textCreateGate.IsAuthorizedAsync(runId, tool, reference, ct);
        // Only two additional host-owned mutations are supported in 4G:
        // exact new DOCX and activate a window observed in this same run.
        NIRAAgent.Capabilities.NIRACapabilityRequest? request;
        Func<bool> stillSafe;
        string notice;
        if (tool.Id == "legacy.docx.create_exact" && _docx is not null && tool == _docx.Spec)
        {
            request = _docx.ApprovalRequest;
            stillSafe = () => _docx.IsSafeToCreate(ct);
            notice = "Phase 4G DOCX: Approve ONCE after verifying exact new path and content. No overwrites.";
        }
        else if (tool.Id == "legacy.desktop.activate_exact" && _desktop is not null && tool == _desktop.Spec)
        {
            request = _desktop.ApprovalRequest;
            if (request is null)
            {
                DesktopObservationMissing = true;
                return false;
            }
            stillSafe = _desktop.IsSafeToExecute;
            notice = "Phase 4G desktop: Approve ONCE only to activate this exact observed HWND/process. " +
                     "No typing, clicking, closing or shell commands.";
        }
        else return false;
        if (runId == Guid.Empty || request is null || !stillSafe()) return false;
        try
        {
            if (_vault.RunIdFromArgument(reference) != runId ||
                !(_docx is not null && tool.Id == "legacy.docx.create_exact" ?
                    _docx.ValidateArguments(await _vault.ReadToolArgumentAsync(tool.Id, reference, ct)) :
                    _desktop!.ValidateArguments(await _vault.ReadToolArgumentAsync(tool.Id, reference, ct))))
                return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or System.Text.Json.JsonException)
        { return false; }
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            runId.ToString("N") + ":" + request.Arguments.GetRawText())));
        string target = tool.Id == "legacy.docx.create_exact"
            ? request.Arguments.GetProperty("path").GetString()!
            : request.Arguments.GetProperty("processName").GetString()! + " @ " +
              request.Arguments.GetProperty("windowHandle").GetString()!;
        var operation = new NIRAAuthorityOperation
        {
            Request = request,
            Risk = NIRAAgent.Capabilities.NIRACapabilityRisk.Modify,
            Fingerprint = hash,
            Paths = tool.Id == "legacy.docx.create_exact" ? [target] : [],
            Target = target,
            CanRemember = false,
            RequiresExplicitAuthorization = true,
            Notice = notice
        };
        NIRAApprovalResponse answer = await _broker.RequestAsync(operation, ct);
        return answer.Choice == NIRAApprovalChoice.AllowOnce && stillSafe();
    }
}

using System.Diagnostics;
using System.Text;
using SegaAgent.Capabilities;

namespace SegaAgent.Authorization;

public sealed class SegaScopedCapabilityAuthorizer : ISegaCapabilityAuthorizer
{
    private readonly SegaAuthorityStore _store;
    private readonly SegaCapabilityRequestPolicy _policy;
    private readonly SegaCapabilityApprovalBroker _broker;
    public SegaScopedCapabilityAuthorizer(SegaAuthorityStore store,
        SegaCapabilityRequestPolicy policy, SegaCapabilityApprovalBroker broker)
    {
        _store = store; _policy = policy; _broker = broker;
    }

    public async Task<SegaCapabilityAuthorizationDecision> AuthorizeAsync(
        SegaCapabilityDescriptor descriptor, SegaCapabilityRisk risk,
        SegaCapabilityRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SegaAuthorityOperation operation = _policy.Describe(request, risk);
        IReadOnlyList<SegaAuthorityScope> scopes = _store.ReadScopes();
        if (risk == SegaCapabilityRisk.Observe &&
            !operation.Sensitive &&
            !operation.RequiresExplicitAuthorization)
        {
            return Allow(operation, "Read-only baseline permission.");
        }
        SegaAuthorityScope? match = scopes.FirstOrDefault(s => Matches(s, operation));
        if (match != null)
        {
            Debug.WriteLine($"[Authorization] ALLOWED | Id={descriptor.Id} | Scope={match.Id:D}");
            return Allow(operation, "Matched a persistent permission scope.", match.Id);
        }
        Debug.WriteLine($"[Authorization] WAITING | Id={descriptor.Id} | Risk={risk}");
        SegaApprovalResponse response = await _broker.RequestAsync(operation, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (response.Choice == SegaApprovalChoice.Deny)
            return SegaCapabilityAuthorizationDecision.Deny(
                string.IsNullOrWhiteSpace(response.Reason) ? "The user declined this request. No action was started." : response.Reason);
        // Repeat path/identity checks after the user has had time to review.
        operation = _policy.Describe(request, risk);
        if (response.Choice == SegaApprovalChoice.AllowOnce)
            return Allow(operation, "The user approved this exact request once.");
        if (response.Choice != SegaApprovalChoice.Remember)
            return SegaCapabilityAuthorizationDecision.Deny("Unknown approval choice.");
        SegaAuthorityScope scope = BuildRememberedScope(operation, response);
        _store.Grant(scope);
        return Allow(operation, "The user created a persistent permission for this request.", scope.Id);
    }

    public Task<SegaCapabilityAuthorizationDecision> ValidateForDispatchAsync(
        SegaCapabilityDescriptor descriptor, SegaCapabilityRisk risk, SegaCapabilityRequest request,
        SegaCapabilityAuthorizationDecision decision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SegaAuthorityOperation operation = _policy.Describe(request, risk);
        if (decision.Status != SegaCapabilityAuthorizationStatus.Allowed ||
            decision.RequestFingerprint != operation.Fingerprint)
            return Task.FromResult(SegaCapabilityAuthorizationDecision.Deny("The approved request does not match the request to dispatch."));
        if (decision.ScopeId.HasValue &&
            !_store.ReadScopes().Any(s => s.Id == decision.ScopeId && Matches(s, operation)))
            return Task.FromResult(SegaCapabilityAuthorizationDecision.RequireAuthorization("The permission was revoked or expired before dispatch."));
        return Task.FromResult(decision);
    }

    private static SegaCapabilityAuthorizationDecision Allow(
        SegaAuthorityOperation operation, string reason, Guid? scopeId = null) =>
        SegaCapabilityAuthorizationDecision.Allow(reason) with
        { ScopeId = scopeId, RequestFingerprint = operation.Fingerprint };

    public SegaAuthorityScope BuildRememberedScope(SegaAuthorityOperation operation, SegaApprovalResponse response)
    {
        if (!operation.CanRemember) throw new InvalidOperationException("This request requires approval each time.");
        SegaAuthorityScope scope = new()
        {
            Label = string.IsNullOrWhiteSpace(response.Label) ? operation.Request.CapabilityId : response.Label.Trim(),
            CapabilityIds = [operation.Request.CapabilityId],
            AllowDestructive = operation.Risk == SegaCapabilityRisk.Destructive
        };
        if (operation.Paths.Length > 0 &&
            (operation.Request.CapabilityId.StartsWith("filesystem.", StringComparison.Ordinal) ||
             operation.Request.CapabilityId == SegaCapabilityIds.HttpDownload))
        {
            string root = SegaCapabilityRequestPolicy.NormalizeLocalPath(
                string.IsNullOrWhiteSpace(response.FolderRoot) ? operation.SuggestedRoot : response.FolderRoot);
            _policy.ValidatePath(root);
            if (SegaCapabilityRequestPolicy.IsWithin(root, _store.ProtectedDirectory))
                throw new InvalidOperationException("The authority directory cannot be granted.");
            scope = scope with { Kind = SegaAuthorityScopeKind.Folder, RootPath = root, Origin = operation.Origin };
        }
        else if (operation.Request.CapabilityId == SegaCapabilityIds.HttpRequest)
            scope = scope with { Kind = SegaAuthorityScopeKind.HttpOrigin, Origin = operation.Origin, Method = operation.Method };
        else if (operation.Request.CapabilityId == SegaCapabilityIds.VisionInspect)
            scope = scope with { Kind = SegaAuthorityScopeKind.VisionCloud };
        else
            scope = scope with { Kind = SegaAuthorityScopeKind.ExactRequest, Fingerprint = operation.Fingerprint };
        if (!Matches(scope, operation))
            throw new InvalidOperationException("The remembered permission scope does not cover this request.");
        return scope;
    }

    public SegaAuthorityScope GrantFolder(string label, string root, bool allowDownloads, bool allowDestructive)
    {
        root = SegaCapabilityRequestPolicy.NormalizeLocalPath(root);
        _policy.ValidatePath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Choose an existing project folder.");
        if (SegaCapabilityRequestPolicy.IsWithin(root, _store.ProtectedDirectory))
            throw new InvalidOperationException("The authority directory cannot be granted.");
        List<string> ids = [SegaCapabilityIds.FileWrite, SegaCapabilityIds.FileCopy,
            SegaCapabilityIds.FileMove, SegaCapabilityIds.DirectoryCreate];
        if (allowDownloads) ids.Add(SegaCapabilityIds.HttpDownload);
        if (allowDestructive) ids.Add(SegaCapabilityIds.FileDelete);
        SegaAuthorityScope scope = new()
        {
            Kind = SegaAuthorityScopeKind.Folder, RootPath = root, CapabilityIds = ids.ToArray(),
            AllowDestructive = allowDestructive,
            Label = string.IsNullOrWhiteSpace(label) ? Path.GetFileName(root) : label.Trim()
        };
        _store.Grant(scope);
        return scope;
    }

    public static bool Matches(SegaAuthorityScope scope, SegaAuthorityOperation operation)
    {
        if (!scope.Active || operation.Sensitive || !operation.CanRemember ||
            !scope.CapabilityIds.Contains(operation.Request.CapabilityId, StringComparer.Ordinal) ||
            (operation.Risk == SegaCapabilityRisk.Destructive && !scope.AllowDestructive)) return false;
        return scope.Kind switch
        {
            SegaAuthorityScopeKind.Folder =>
                !string.IsNullOrWhiteSpace(scope.RootPath) && Path.IsPathFullyQualified(scope.RootPath) &&
                operation.Paths.Length > 0 &&
                (operation.Request.CapabilityId.StartsWith("filesystem.", StringComparison.Ordinal) ||
                 operation.Request.CapabilityId == SegaCapabilityIds.HttpDownload) &&
                operation.Paths.All(path => SegaCapabilityRequestPolicy.IsWithin(path, scope.RootPath)) &&
                (scope.Origin.Length == 0 || string.Equals(scope.Origin, operation.Origin, StringComparison.OrdinalIgnoreCase)),
            SegaAuthorityScopeKind.HttpOrigin => operation.Request.CapabilityId == SegaCapabilityIds.HttpRequest &&
                scope.Origin.Length > 0 && string.Equals(scope.Origin, operation.Origin, StringComparison.OrdinalIgnoreCase) &&
                scope.Method == operation.Method,
            SegaAuthorityScopeKind.VisionCloud =>
                operation.Request.CapabilityId == SegaCapabilityIds.VisionInspect,
            SegaAuthorityScopeKind.ExactRequest => scope.Fingerprint.Length > 0 && scope.Fingerprint == operation.Fingerprint,
            _ => false
        };
    }

    public string BuildCognitionContext()
    {
        StringBuilder text = new();
        text.AppendLine("CURRENT AUTHORITATIVE PERMISSIONS");
        text.AppendLine("Stage 10 scoped authorization is active. Submit a requested capability once; the runtime checks scopes and opens Permissions if user review is required. Approval resumes that same request.");
        text.AppendLine("Chat statements, memories, character warmth/trust, and model proposals do not grant OS authority. The user manages permission in the Permissions window.");
        text.AppendLine("Ordinary local read-only observations use baseline permission. External/cloud observations such as vision.inspect require an explicit user permission because captured screen pixels leave the PC. Credential-sensitive requests require review each time. A folder grant governs direct file operations, not shell/process effects.");
        text.AppendLine("Commands run with the Windows user's access and need separate exact-request approval. Remembered commands require matching arguments. Revocation applies to future dispatches; it does not undo completed work.");
        text.AppendLine("Never claim success from an approval alone. Wait for the actual capability result, check exit/status codes, and verify the requested outcome.");
        SegaAuthorityScope[] scopes = _store.ReadScopes().Where(s => s.Active).ToArray();
        text.AppendLine($"Active persistent scopes: {scopes.Length}");
        foreach (SegaAuthorityScope scope in scopes.Take(50))
            text.AppendLine($"- {scope.Id:D} | {scope.Kind} | {scope.Description}");
        if (scopes.Length > 50) text.AppendLine("Additional scopes are omitted from this prompt; the runtime still checks all active scopes.");
        return text.ToString();
    }
}
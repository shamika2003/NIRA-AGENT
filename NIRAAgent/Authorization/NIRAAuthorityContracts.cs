using NIRAAgent.Capabilities;

namespace NIRAAgent.Authorization;

// Keep numeric ordinals stable because authority scopes are serialized as JSON.
public enum NIRAAuthorityScopeKind
{
    Folder = 0,
    ExactRequest = 1,
    HttpOrigin = 2,
    VisionCloud = 3,
    BrowserOrigin = 4,
    Task = 5
}

public enum NIRAApprovalChoice
{
    Deny = 0,
    AllowOnce = 1,
    Remember = 2,
    AllowTask = 3
}

public sealed record NIRAAuthorityScope
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public NIRAAuthorityScopeKind Kind { get; init; }
    public string Label { get; init; } = string.Empty;
    public string[] CapabilityIds { get; init; } = [];
    public string RootPath { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
    public Guid? GoalId { get; init; }
    public string[] ExecutionProfileIds { get; init; } = [];
    public bool AllowDestructive { get; init; }
    public bool AllowCredentialUse { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public DateTimeOffset? RevokedAtUtc { get; init; }

    public bool Active =>
        RevokedAtUtc == null &&
        (ExpiresAtUtc == null || ExpiresAtUtc > DateTimeOffset.UtcNow);

    public string Description => Kind switch
    {
        NIRAAuthorityScopeKind.Folder =>
            $"{RootPath} | {string.Join(", ", CapabilityIds)} | destructive={AllowDestructive}" +
            (ExecutionProfileIds.Length == 0
                ? string.Empty
                : $" | execution=[{string.Join(", ", ExecutionProfileIds)}]"),

        NIRAAuthorityScopeKind.HttpOrigin =>
            $"{Method} {Origin} | {string.Join(", ", CapabilityIds)}",

        NIRAAuthorityScopeKind.BrowserOrigin =>
            $"Browser {Origin} | {string.Join(", ", CapabilityIds)}" +
            (AllowCredentialUse ? " | credential-use=true" : string.Empty) +
            (string.IsNullOrWhiteSpace(Method) ? string.Empty : $" | method={Method}"),

        NIRAAuthorityScopeKind.VisionCloud =>
            $"Cloud visual interpretation | {string.Join(", ", CapabilityIds)}",

        NIRAAuthorityScopeKind.Task =>
            $"Task {GoalId?.ToString("D") ?? "-"} | " +
            (RootPath.Length > 0 ? $"root={RootPath} | " : string.Empty) +
            (Origin.Length > 0 ? $"origin={Origin} | " : string.Empty) +
            (Fingerprint.Length > 0 ? $"exact={Fingerprint[..Math.Min(12, Fingerprint.Length)]} | " : string.Empty) +
            $"{string.Join(", ", CapabilityIds)}" +
            (ExecutionProfileIds.Length == 0
                ? string.Empty
                : $" | execution=[{string.Join(", ", ExecutionProfileIds)}]") +
            (AllowCredentialUse ? " | credential-use=true" : string.Empty) +
            (AllowDestructive ? " | destructive=true" : string.Empty),

        _ =>
            $"{Label} | exact request {Fingerprint[..Math.Min(12, Fingerprint.Length)]}"
    };
}

public sealed record NIRAAuthorityOperation
{
    public required NIRACapabilityRequest Request { get; init; }
    public required NIRACapabilityRisk Risk { get; init; }
    public required string Fingerprint { get; init; }
    public string[] Paths { get; init; } = [];
    public string Origin { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string Notice { get; init; } = string.Empty;
    public bool Sensitive { get; init; }
    public bool RequiresCredentialUse { get; init; }
    public bool RequiresExplicitAuthorization { get; init; }
    public bool CanRemember { get; init; } = true;
    public string SuggestedRoot { get; init; } = string.Empty;
    public NIRAAuthorityExecutionContext ExecutionContext { get; init; } =
        NIRAAuthorityExecutionContext.Empty;
    public string Preview => NIRACapabilityRequestPolicy.BuildPreview(Request);
}

public sealed record NIRAApprovalRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required NIRAAuthorityOperation Operation { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Display => $"{Operation.Request.CapabilityId} | {Operation.Target}";
}

public sealed record NIRAApprovalResponse
{
    public NIRAApprovalChoice Choice { get; init; }
    public string FolderRoot { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool AllowDestructive { get; init; }
    public string[] ExecutionProfileIds { get; init; } = [];
}

public sealed record NIRAAuthorityAuditRow
{
    public string Id { get; init; } = string.Empty;
    public string TimeUtc { get; init; } = string.Empty;
    public string Capability { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ScopeId { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}


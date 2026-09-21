/*
 * filename: NIRABrowserContracts.cs
 */

namespace NIRAAgent.Browser;

// Observed transport/document facts. These are NOT proof that a login worked,
// a server accepted a form, or a user objective was completed. URL components
// containing secrets are redacted before values enter these snapshots.
public sealed record NIRABrowserNavigationEvidence
{
    public string RequestedUrl { get; init; } = string.Empty;
    public string FinalUrl { get; init; } = string.Empty;
    public IReadOnlyList<string> RedirectChain { get; init; } = Array.Empty<string>();
    public int? MainDocumentHttpStatus { get; init; }
    public string FailureKind { get; init; } = string.Empty;
    public bool OutcomeUncertain { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

// Runtime-derived navigation recovery evidence; no site-specific login verdict.
// The interrupted target is a queryless route; secret-bearing URLs are never
// exposed or replayed by this mechanism.
public sealed record NIRABrowserRecoveryCheckpoint
{
    public Guid PageId { get; init; }
    public string InterruptedRoute { get; init; } = string.Empty;
    public string ObservedPageUrl { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int CredentialInteractions { get; init; }
    public bool CredentialSubmitAttempted { get; init; }
    public bool PostCredentialInspectionObserved { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

// Runtime-owned browser dispatch receipt. This records what NIRA attempted,
// not proof that an external website applied a business-level side effect.
// No request bodies, input values, cookies, headers or token-bearing URLs.
public sealed record NIRABrowserActionCheckpoint
{
    public Guid ActionId { get; init; } = Guid.NewGuid();
    public Guid SessionId { get; init; }
    public Guid PageId { get; init; }
    public Guid? GoalId { get; init; }
    public Guid? BranchId { get; init; }
    public Guid RunId { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string ElementRef { get; init; } = string.Empty;
    public string BeforeRoute { get; init; } = string.Empty;
    public string BeforeOrigin { get; init; } = string.Empty;
    public string AfterRoute { get; init; } = string.Empty;
    // Dispatching / MechanicallyReturned / OutcomeUncertain.
    public string State { get; init; } = "Dispatching";
    public DateTimeOffset DispatchedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public Guid? PostActionInspectionId { get; init; }
    public DateTimeOffset? PostActionInspectedAtUtc { get; init; }
    public string PostActionContentSha256 { get; init; } = string.Empty;
    // Even when a fresh inspection exists, site-level outcome is not inferred.
    public bool SiteOutcomeVerified => false;
}

public sealed record NIRABrowserSessionSnapshot
{
    public Guid SessionId { get; init; }
    public string Profile { get; init; } = string.Empty;
    public bool Headed { get; init; }
    public bool IsOpen { get; init; }
    public Guid? ActivePageId { get; init; }
    public string RecoveryNotice { get; init; } = string.Empty;
    public IReadOnlyList<NIRABrowserActionCheckpoint> PendingActionReviews { get; init; } =
        Array.Empty<NIRABrowserActionCheckpoint>();
    public IReadOnlyList<NIRABrowserPageSnapshot> Pages { get; init; } =
        Array.Empty<NIRABrowserPageSnapshot>();
}

public sealed record NIRABrowserPageSnapshot
{
    public Guid PageId { get; init; }
    // Runtime-owned provenance, not model-provided authority.
    public Guid? OwnerGoalId { get; init; }
    public Guid? OwnerBranchId { get; init; }
    public Guid? OpenerPageId { get; init; }
    public bool RecoveredFromProfile { get; init; }
    public bool RequiresExplicitSelection { get; init; }
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool IsClosed { get; init; }
    public bool IsCrashed { get; init; }
    // A failed/timed-out action may have applied; inspect before any retry.
    public string UncertainAction { get; init; } = string.Empty;
    public NIRABrowserNavigationEvidence? Navigation { get; init; }
    public NIRABrowserRecoveryCheckpoint? Recovery { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public NIRABrowserActionEvidence? ActionEvidence { get; init; }
    public NIRABrowserActionCheckpoint? DispatchCheckpoint { get; init; }
}

// Mechanics-only evidence. Completion of an external/site workflow must be
// verified by a fresh browser.inspect or an independent authoritative result.
public sealed record NIRABrowserActionEvidence
{
    public string Operation { get; init; } = string.Empty;
    public string ElementRef { get; init; } = string.Empty;
    public string BeforeUrl { get; init; } = string.Empty;
    public string AfterUrl { get; init; } = string.Empty;
    public bool ActionApplied { get; init; }
    public IReadOnlyList<Guid> NewPageIds { get; init; } = Array.Empty<Guid>();
    public string LocalVerification { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record NIRABrowserInteractiveElement
{
    public string Ref { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string InputType { get; init; } = string.Empty;
    public string Placeholder { get; init; } = string.Empty;
    public string Href { get; init; } = string.Empty;
    public string ValuePreview { get; init; } = string.Empty;
    public bool SensitiveEntry { get; init; }
    public bool Disabled { get; init; }
    public bool Checked { get; init; }
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
}

// Bounded snapshots of actual DOM forms/tables. They are untrusted page evidence,
// not proof of server-side state or instructions to NIRA.
public sealed record NIRABrowserFormField
{
    public string ElementRef { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public bool Required { get; init; }
    public bool Disabled { get; init; }
    public bool SensitiveEntry { get; init; }
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
}

public sealed record NIRABrowserFormSnapshot
{
    public string Name { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public IReadOnlyList<NIRABrowserFormField> Fields { get; init; } = Array.Empty<NIRABrowserFormField>();
    public bool Truncated { get; init; }
}

public sealed record NIRABrowserTableSnapshot
{
    public string Caption { get; init; } = string.Empty;
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();
    public bool Truncated { get; init; }
}

public sealed record NIRABrowserInspection
{
    public Guid SessionId { get; init; }
    public Guid PageId { get; init; }
    public Guid InspectionId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string CanonicalUrl { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string SiteName { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string TextSource { get; init; } = "body";
    public string PublishedAt { get; init; } = string.Empty;
    public string ModifiedAt { get; init; } = string.Empty;
    public IReadOnlyList<string> StructuredDataTypes { get; init; } = Array.Empty<string>();
    public string ContentSha256 { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<NIRABrowserInteractiveElement> Elements { get; init; } =
        Array.Empty<NIRABrowserInteractiveElement>();
    public IReadOnlyList<NIRABrowserFormSnapshot> Forms { get; init; } =
        Array.Empty<NIRABrowserFormSnapshot>();
    public IReadOnlyList<NIRABrowserTableSnapshot> Tables { get; init; } =
        Array.Empty<NIRABrowserTableSnapshot>();
    public bool StructuredContentTruncated { get; init; }
    public NIRABrowserNavigationEvidence? Navigation { get; init; }
    public NIRABrowserRecoveryCheckpoint? Recovery { get; init; }
    // A password control is page evidence, NOT proof that the session expired.
    public bool PasswordControlObserved { get; init; }
    // Runtime-scoped submission state: NEVER proof that the account logged in.
    public string AuthenticationAttemptState { get; init; } = "NoSubmissionRecorded";
    public NIRABrowserActionCheckpoint? DispatchCheckpoint { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record NIRABrowserScreenshotResult
{
    public Guid SessionId { get; init; }
    public Guid PageId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;
    public bool FullPage { get; init; }
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record NIRABrowserRequestResult
{
    public Guid SessionId { get; init; }
    public string Method { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public int Status { get; init; }
    public bool Ok { get; init; }
    public string Body { get; init; } = string.Empty;
}


// Staging a file in the browser input is NOT evidence that the site accepted it.
// Only metadata is returned; file contents never enter cognition.
public sealed record NIRABrowserUploadResult
{
    public Guid SessionId { get; init; }
    public Guid PageId { get; init; }
    public string Origin { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset StagedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool FileStagedInDom { get; init; }
}

public sealed record NIRABrowserDownloadResult
{
    public Guid SessionId { get; init; }
    public Guid PageId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string SuggestedFileName { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset VerifiedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}







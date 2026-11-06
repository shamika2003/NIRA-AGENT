using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Nira.V2.Evidence;

/// <summary>
/// Phase 2A: in-memory, typed claim/source/review ledger. It deliberately does NOT
/// verify semantic entailment. A review is an auditable assessment, not proof of truth.
/// Never store private research in this prototype; encrypted persistence comes later.
/// </summary>
public sealed class EvidenceLedger
{
    private readonly Dictionary<string, EvidenceSource> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClaimEntry> _claims = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IndependentReview> _reviews = new(StringComparer.Ordinal);

    public EvidenceLedger(Guid runId, EvidenceRequirement requirement)
    {
        if (runId == Guid.Empty) throw new ArgumentException("Run ID required.", nameof(runId));
        RunId = runId;
        Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
    }

    public Guid RunId { get; }
    public EvidenceRequirement Requirement { get; }
    public IReadOnlyCollection<EvidenceSource> Sources => _sources.Values.ToArray();
    public IReadOnlyCollection<ClaimEntry> Claims => _claims.Values.ToArray();
    public IReadOnlyCollection<IndependentReview> Reviews => _reviews.Values.ToArray();

    public EvidenceSource AddObservedSource(string id, string url, string publisher, string title,
        string body, EvidenceMaterialKind kind, string observationReference, DateTimeOffset retrievedUtc)
    {
        ValidateId(id);
        if (_sources.ContainsKey(id)) throw new InvalidOperationException("Duplicate source ID.");
        if (string.IsNullOrWhiteSpace(observationReference) || observationReference.Length > 160)
            throw new ArgumentException("Actual tool-observation reference required.", nameof(observationReference));
        if (!IsPublicHttpsUrl(url, out Uri? uri))
            throw new ArgumentException("Source URL must be public HTTPS with a DNS hostname.", nameof(url));
        if (string.IsNullOrWhiteSpace(body) || body.Length > 200_000)
            throw new ArgumentException("Source material must be nonempty and bounded.", nameof(body));
        if (string.IsNullOrWhiteSpace(title) || title.Length > 300 ||
            string.IsNullOrWhiteSpace(publisher) || publisher.Length > 200)
            throw new ArgumentException("Source title and publisher required.");
        if (retrievedUtc == default)
            throw new ArgumentException("Retrieval time required.", nameof(retrievedUtc));
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var result = new EvidenceSource(id, uri!, publisher.Trim(), title.Trim(), body, kind,
            observationReference.Trim(), retrievedUtc, digest,
            Requirement.IsAllowedDomain(uri!.Host));
        _sources.Add(id, result);
        return result;
    }

    public ClaimEntry AddClaim(string id, string text, string draftedBy,
        IReadOnlyList<string> citedSourceIds)
    {
        ValidateId(id);
        if (_claims.ContainsKey(id)) throw new InvalidOperationException("Duplicate claim ID.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 3000)
            throw new ArgumentException("Nonempty bounded claim required.", nameof(text));
        if (string.IsNullOrWhiteSpace(draftedBy) || draftedBy.Length > 100)
            throw new ArgumentException("Draft author must be identified.", nameof(draftedBy));
        ArgumentNullException.ThrowIfNull(citedSourceIds);
        if (citedSourceIds.Count == 0 || citedSourceIds.Count > 20 ||
            citedSourceIds.Any(sourceId => !_sources.ContainsKey(sourceId)) ||
            citedSourceIds.Count != citedSourceIds.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Every citation must resolve to a unique observed source.", nameof(citedSourceIds));
        var entry = new ClaimEntry(id, text.Trim(), draftedBy.Trim(), citedSourceIds.ToArray());
        _claims.Add(id, entry);
        return entry;
    }

    public IndependentReview RecordReview(string claimId, string reviewerId, ReviewOutcome outcome,
        IReadOnlyList<PassageAnchor> anchors, string? qualification = null, string? rationale = null)
    {
        if (!_claims.TryGetValue(claimId, out ClaimEntry? claim))
            throw new KeyNotFoundException("Claim is not registered.");
        if (_reviews.ContainsKey(claimId))
            throw new InvalidOperationException("A new review version needs a new ledger revision.");
        if (string.IsNullOrWhiteSpace(reviewerId) || reviewerId.Length > 100 ||
            string.Equals(reviewerId.Trim(), claim.DraftedBy, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Reviewer must be identified and separate from drafter.", nameof(reviewerId));
        ArgumentNullException.ThrowIfNull(anchors);
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));

        if (outcome is ReviewOutcome.ReviewerSupported or ReviewOutcome.ReviewerQualified)
        {
            if (anchors.Count == 0 || anchors.Count > 20)
                throw new ArgumentException("A favorable assessment needs passage anchors.", nameof(anchors));
            foreach (PassageAnchor anchor in anchors)
            {
                if (!claim.CitedSourceIds.Contains(anchor.SourceId, StringComparer.Ordinal) ||
                    !_sources.TryGetValue(anchor.SourceId, out EvidenceSource? source) ||
                    !source.MatchesRequestedDomains ||
                    source.MaterialKind != EvidenceMaterialKind.RetrievedDocument ||
                    anchor.Start < 0 || anchor.Length <= 0 || anchor.Length > 1500 ||
                    anchor.Start > source.Body.Length - anchor.Length ||
                    !string.Equals(source.Body.Substring(anchor.Start, anchor.Length),
                        anchor.ExactQuote, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Review requires an exact passage from a retrieved, domain-eligible document.");
            }
            if (outcome == ReviewOutcome.ReviewerQualified && string.IsNullOrWhiteSpace(qualification))
                throw new ArgumentException("Qualified assessments must state the limitation.", nameof(qualification));
            if (string.IsNullOrWhiteSpace(rationale))
                throw new ArgumentException("Favorable reviewer assessments need rationale.", nameof(rationale));
        }
        else if (anchors.Count > 0)
            throw new ArgumentException("Pending/unsupported/disputed reviews must not certify passages.", nameof(anchors));

        var review = new IndependentReview(claimId, reviewerId.Trim(), outcome,
            anchors.ToArray(), qualification?.Trim(), rationale?.Trim(), DateTimeOffset.UtcNow);
        _reviews.Add(claimId, review);
        return review;
    }

    public PublicationGateResult EvaluatePublication(string claimId)
    {
        if (!_claims.TryGetValue(claimId, out ClaimEntry? claim))
            throw new KeyNotFoundException("Unknown claim.");
        if (!_reviews.TryGetValue(claimId, out IndependentReview? review))
            return new(false, "CLAIM_NOT_REVIEWED", null);
        return review.Outcome switch
        {
            ReviewOutcome.ReviewerSupported => new(true, "REVIEWED_SUPPORT_RECORDED", claim.Text),
            ReviewOutcome.ReviewerQualified => new(true, "REVIEWED_QUALIFICATION_RECORDED",
                claim.Text + " (Qualification: " + review.Qualification + ")"),
            ReviewOutcome.Disputed => new(false, "CLAIM_DISPUTED", null),
            ReviewOutcome.Unsupported => new(false, "CLAIM_UNSUPPORTED", null),
            _ => new(false, "CLAIM_NOT_REVIEWED", null)
        };
    }

    public EvidenceLedgerSummary Summarize() => new(_sources.Count, _claims.Count,
        _reviews.Count, _claims.Keys.Count(id => EvaluatePublication(id).Publishable),
        _sources.Values.Count(x => !x.MatchesRequestedDomains));

    public static bool IsPublicHttpsUrl(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps || !candidate.IsDefaultPort ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            candidate.HostNameType != UriHostNameType.Dns || !candidate.Host.Contains('.', StringComparison.Ordinal) ||
            candidate.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(candidate.Host, out _)) return false;
        uri = candidate;
        return true;
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80 ||
            id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            throw new ArgumentException("ID must be a bounded host-controlled identifier.", nameof(id));
    }
}

public enum EvidenceMaterialKind { SearchExcerpt, RetrievedDocument, UserProvidedDocument }
public enum ReviewOutcome { Pending, ReviewerSupported, ReviewerQualified, Unsupported, Disputed }

public sealed record EvidenceRequirement(IReadOnlyList<string> RequestedDomains)
{
    public bool IsAllowedDomain(string host)
    {
        if (RequestedDomains is null || RequestedDomains.Count == 0) return true;
        foreach (string domain in RequestedDomains)
        {
            string normalized = domain.Trim().TrimStart('.').ToLowerInvariant();
            if (normalized.Length == 0 || !normalized.Contains('.', StringComparison.Ordinal)) continue;
            if (host.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + normalized, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

public sealed record EvidenceSource(string Id, Uri Url, string Publisher, string Title,
    string Body, EvidenceMaterialKind MaterialKind, string ObservationReference,
    DateTimeOffset RetrievedUtc, string BodySha256, bool MatchesRequestedDomains);
public sealed record ClaimEntry(string Id, string Text, string DraftedBy, IReadOnlyList<string> CitedSourceIds);
public sealed record PassageAnchor(string SourceId, int Start, int Length, string ExactQuote);
public sealed record IndependentReview(string ClaimId, string ReviewerId, ReviewOutcome Outcome,
    IReadOnlyList<PassageAnchor> Anchors, string? Qualification, string? Rationale, DateTimeOffset AssessedUtc);
public sealed record PublicationGateResult(bool Publishable, string Code, string? ReviewedText);
public sealed record EvidenceLedgerSummary(int Sources, int Claims, int ReviewedClaims,
    int PublicationEligibleClaims, int SourcesOutsideRequestedDomains);

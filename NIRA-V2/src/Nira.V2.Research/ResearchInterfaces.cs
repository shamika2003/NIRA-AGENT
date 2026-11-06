using Nira.V2.Contracts;

namespace Nira.V2.Research;

// Adapters for providers, retrieval, claim extraction, critical review, media,
// and answer writing are deliberately distinct. Retrieval output is untrusted.
public interface IEvidenceRetriever
{
    Task<IReadOnlyList<EvidenceRecord>> RetrieveAsync(string question, CancellationToken ct);
}
public interface IClaimResearcher
{
    Task<IReadOnlyList<ResearchSectionInput>> BuildClaimsAsync(string question,
        IReadOnlyList<EvidenceRecord> evidence, CancellationToken ct);
}
public interface IClaimReviewer
{
    Task<IReadOnlyList<ResearchSectionInput>> VerifyAsync(
        IReadOnlyList<ResearchSectionInput> sections,
        IReadOnlyList<EvidenceRecord> evidence, CancellationToken ct);
}
public interface IMediaProvenanceVerifier
{
    Task<MediaRecord> ReviewAsync(MediaRecord candidate,
        IReadOnlyList<EvidenceRecord> evidence, CancellationToken ct);
}

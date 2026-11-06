using Nira.V2.Contracts;

namespace Nira.V2.Research;

// This is a trust/assembly boundary, NOT an LLM or a search engine. It cannot
// certify that a claim is true: claim assessors and primary source retrievers
// are separate services to be implemented and evaluated in later milestones.
public sealed class ResearchDocumentBuilder
{
    public ResponseDocument Build(string title, IReadOnlyList<EvidenceRecord> evidence,
        IReadOnlyList<ResearchSectionInput> requestedSections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var known = evidence.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var uniqueImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sections = new List<DocumentSection>();
        var omissions = new List<string>();
        int claimCount = 0;

        foreach (ResearchSectionInput input in requestedSections)
        {
            var blocks = new List<DocumentBlock>();
            foreach (ClaimRecord claim in input.Claims)
            {
                if (claim.SectionId != input.Id || claim.Assessment == ClaimAssessment.Unsupported ||
                    claim.EvidenceIds.Count == 0 || claim.EvidenceIds.Any(id => !known.ContainsKey(id)))
                {
                    omissions.Add($"CLAIM_NOT_PUBLISHED:{claim.Id}");
                    continue;
                }
                string text = claim.Text.Trim();
                if (text.Length == 0)
                {
                    omissions.Add($"EMPTY_CLAIM:{claim.Id}");
                    continue;
                }
                if (claim.Assessment == ClaimAssessment.Qualified)
                {
                    if (string.IsNullOrWhiteSpace(claim.Qualification))
                    {
                        omissions.Add($"MISSING_QUALIFICATION:{claim.Id}");
                        continue;
                    }
                    text += " (Qualification: " + claim.Qualification.Trim() + ")";
                }
                blocks.Add(new ParagraphBlock(text, claim.EvidenceIds));
                claimCount++;
            }

            foreach (MediaRecord media in input.Media)
            {
                if (media.SectionId != input.Id || !known.ContainsKey(media.EvidenceId) ||
                    !media.ProvenanceReviewed || !media.VisualMatchReviewed ||
                    media.SyntheticOrIllustration || string.IsNullOrWhiteSpace(media.Caption) ||
                    !ValidSha256(media.ContentSha256) ||
                    !uniqueImages.Add(media.ContentSha256))
                {
                    omissions.Add($"IMAGE_NOT_PUBLISHED:{media.Id}");
                    continue;
                }
                blocks.Add(new FigureBlock(media.Id, media.Caption, media.EvidenceId));
            }
            if (blocks.Count > 0)
                sections.Add(new DocumentSection(input.Id, input.Heading, blocks));
        }

        // Never replace the user's requested research with a dump of search snippets.
        if (claimCount == 0)
            return new ResponseDocument(title, DocumentStatus.InsufficientEvidence,
                new[] { new DocumentSection("limitations", "Research status",
                    new DocumentBlock[] { new ParagraphBlock(
                        "I couldn't verify enough factual claims to give a reliable explanation yet.",
                        Array.Empty<string>()) }) }, omissions);

        return new ResponseDocument(title,
            omissions.Count == 0 ? DocumentStatus.Complete : DocumentStatus.Partial,
            sections, omissions);
    }

    private static bool ValidSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
}

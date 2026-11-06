using System.Text.Json.Serialization;

namespace Nira.V2.Contracts;

public enum ClaimAssessment { Supported, Qualified, Unsupported }
public enum DocumentStatus { Complete, Partial, InsufficientEvidence }

public sealed record EvidenceRecord(string Id, Uri SourceUri, string Publisher,
    string Extract, DateTimeOffset RetrievedUtc);
public sealed record ClaimRecord(string Id, string SectionId, string Text,
    ClaimAssessment Assessment, IReadOnlyList<string> EvidenceIds,
    string? Qualification = null);
public sealed record MediaRecord(string Id, string SectionId, string EvidenceId,
    string Title, string ContentSha256, string Caption,
    bool ProvenanceReviewed, bool VisualMatchReviewed, bool SyntheticOrIllustration);
public sealed record ResearchSectionInput(string Id, string Heading,
    IReadOnlyList<ClaimRecord> Claims, IReadOnlyList<MediaRecord> Media);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ParagraphBlock), "paragraph")]
[JsonDerivedType(typeof(FigureBlock), "figure")]
[JsonDerivedType(typeof(TableBlock), "table")]
public abstract record DocumentBlock;
public sealed record ParagraphBlock(string Text, IReadOnlyList<string> EvidenceIds) : DocumentBlock;
public sealed record FigureBlock(string MediaId, string Caption, string EvidenceId) : DocumentBlock;
public sealed record TableBlock(IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows) : DocumentBlock;
public sealed record DocumentSection(string Id, string Heading, IReadOnlyList<DocumentBlock> Blocks);
public sealed record ResponseDocument(string Title, DocumentStatus Status,
    IReadOnlyList<DocumentSection> Sections, IReadOnlyList<string> Omissions);

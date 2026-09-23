using System.Globalization;

namespace NIRAAgent.Conversation;

// Describes an information need. The runtime, not the language model, owns SQL and ranking.
public sealed record NIRAConversationSearchRequest
{
    public string Query { get; init; } = string.Empty;
    public int MaximumResults { get; init; } = 8;
    public string? FromUtc { get; init; }
    public string? ToUtc { get; init; }
    public bool CurrentSessionOnly { get; init; }
    public string? SessionId { get; init; }
    public bool IncludeEpisodes { get; init; } = true;

    public NIRAConversationSearchRequest Normalize() => this with
    {
        Query = (Query ?? string.Empty).Trim()[..Math.Min((Query ?? string.Empty).Trim().Length, 600)],
        MaximumResults = Math.Clamp(MaximumResults, 1, 12),
        FromUtc = ParseUtc(FromUtc),
        ToUtc = ParseUtc(ToUtc),
        SessionId = Guid.TryParse(SessionId, out var id) ? id.ToString("D") : null
    };

    private static string? ParseUtc(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var date)
            ? date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : null;

    public string BuildSignature() => string.Join("|", Query.ToUpperInvariant(),
        FromUtc ?? "", ToUtc ?? "", CurrentSessionOnly, SessionId ?? "", IncludeEpisodes);
}

public sealed record NIRAArchivedConversationHit(
    Guid MessageId, Guid SessionId, Guid? SourceEventId, string Role,
    string Content, DateTimeOffset OccurredAtUtc, double Similarity,
    string? EpisodeAppraisal, string? AssociatedReply);

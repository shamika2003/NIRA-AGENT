using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nira.V2.Ollama;

/// <summary>
/// Pure protocol boundary. Tool proposals come only from the Ollama tool_calls field
/// (or a strictly validated Phase 1C legacy envelope); public answers are ordinary text.
/// This component never executes a tool, makes an HTTP call, or trusts model citations.
/// </summary>
internal static class ResearchModelDecisionProtocol
{
    internal sealed record Result(string Protocol, string? ToolQuery = null,
        string? Answer = null, string? ErrorCode = null,
        int CandidateAnswerChars = 0, int CitationIdMentions = 0, int UrlMentions = 0);

    private const string NativeFunction = "web_search";
    private const string LegacyTool = "web.search";
    private const int MaxAnswerChars = 16_000;
    private static readonly Regex CitationIds = new(@"\[S[0-9]+\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));
    private static readonly Regex BareCitationIds = new(@"\[S[0-9]+\](?!\()",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));
    private static readonly Regex Urls = new(@"https?://[^\s\)\]\>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    public static Result Decode(JsonElement message, IReadOnlySet<string> observedUrls,
        IReadOnlyDictionary<string, string>? citationUrls = null)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return new("invalid", ErrorCode: "MODEL_RESPONSE_SCHEMA_INVALID");

        if (message.TryGetProperty("tool_calls", out var calls))
        {
            if (calls.ValueKind != JsonValueKind.Array)
                return new("native-tool", ErrorCode: "MODEL_TOOL_CALLS_INVALID");
            if (calls.GetArrayLength() > 0)
            {
                // One tool per decision: checkpoint, execute, observe, then decide again.
                if (calls.GetArrayLength() != 1)
                    return new("native-tool", ErrorCode: "MULTIPLE_TOOL_CALLS_UNSUPPORTED");
                var call = calls[0];
                if (call.ValueKind != JsonValueKind.Object ||
                    !call.TryGetProperty("function", out var function) ||
                    function.ValueKind != JsonValueKind.Object || !Unique(function) ||
                    !function.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String ||
                    name.GetString() != NativeFunction ||
                    !function.TryGetProperty("arguments", out var arguments))
                    return new("native-tool", ErrorCode: "MODEL_TOOL_ARGUMENTS_INVALID");

                return ParseToolArguments(arguments, "native-tool");
            }
        }

        if (!message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
            return new("plain-text", ErrorCode: "MODEL_RESPONSE_EMPTY");

        string output = (content.GetString() ?? "").Trim().TrimStart('\uFEFF');
        if (output.Length == 0)
            return new("plain-text", ErrorCode: "MODEL_RESPONSE_EMPTY");
        if (output.Length > 100_000)
            return new("plain-text", ErrorCode: "MODEL_DECISION_SIZE_INVALID");

        if (output.StartsWith("```", StringComparison.Ordinal))
        {
            // Backwards compatibility: a single fenced JSON envelope, not arbitrary prose.
            int newline = output.IndexOf('\n');
            if (newline < 0 || !output[..newline].Trim().Equals("```json", StringComparison.OrdinalIgnoreCase) ||
                !output.EndsWith("```", StringComparison.Ordinal))
                return new("legacy-json", ErrorCode: "INVALID_PROVIDER_DECISION");
            output = output[(newline + 1)..^3].Trim();
        }

        if (output.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var parsed = JsonDocument.Parse(output);
                return DecodeLegacy(parsed.RootElement, observedUrls, citationUrls);
            }
            catch (JsonException)
            {
                // Never repair broken JSON by regex, extracting braces, or guessing a tool call.
                return new("legacy-json", ErrorCode: "INVALID_PROVIDER_DECISION");
            }
        }

        // Native-tool protocol doesn't require the user-facing explanation to be JSON escaped.
        return CheckAnswer(output, observedUrls, "plain-text", citationUrls);
    }

    private static Result DecodeLegacy(JsonElement root, IReadOnlySet<string> observedUrls,
        IReadOnlyDictionary<string, string>? citationUrls)
    {
        if (root.ValueKind != JsonValueKind.Object || !Unique(root) ||
            !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
            return new("legacy-json", ErrorCode: "MODEL_DECISION_SCHEMA_INVALID");

        switch (kind.GetString())
        {
            case "invoke_tool":
                if (root.EnumerateObject().Count() != 3 ||
                    !root.TryGetProperty("tool_id", out var id) ||
                    id.ValueKind != JsonValueKind.String || id.GetString() != LegacyTool ||
                    !root.TryGetProperty("arguments", out var arguments))
                    return new("legacy-json", ErrorCode: "MODEL_TOOL_ARGUMENTS_INVALID");
                return ParseToolArguments(arguments, "legacy-json");
            case "answer":
                if (root.EnumerateObject().Count() != 2 ||
                    !root.TryGetProperty("answer", out var answer) ||
                    answer.ValueKind != JsonValueKind.String)
                    return new("legacy-json", ErrorCode: "MODEL_ANSWER_SCHEMA_INVALID");
                return CheckAnswer(answer.GetString() ?? "", observedUrls, "legacy-json", citationUrls);
            case "cannot_complete":
                return root.EnumerateObject().Count() == 2 &&
                    root.TryGetProperty("reason_code", out var reason) &&
                    reason.ValueKind == JsonValueKind.String
                    ? new("legacy-json", ErrorCode: "MODEL_RESEARCH_INCOMPLETE")
                    : new("legacy-json", ErrorCode: "MODEL_DECISION_SCHEMA_INVALID");
            default:
                return new("legacy-json", ErrorCode: "UNKNOWN_MODEL_DECISION");
        }
    }

    private static Result ParseToolArguments(JsonElement arguments, string protocol)
    {
        if (arguments.ValueKind == JsonValueKind.String)
        {
            // Some tool-call transports encode arguments as a JSON string.
            string raw = arguments.GetString() ?? "";
            if (raw.Length > 2048)
                return new(protocol, ErrorCode: "MODEL_TOOL_ARGUMENTS_INVALID");
            try
            {
                using var parsed = JsonDocument.Parse(raw);
                return ParseToolArguments(parsed.RootElement, protocol);
            }
            catch (JsonException)
            {
                return new(protocol, ErrorCode: "MODEL_TOOL_ARGUMENTS_INVALID");
            }
        }
        if (arguments.ValueKind != JsonValueKind.Object || !Unique(arguments) ||
            arguments.EnumerateObject().Count() != 1 ||
            !arguments.TryGetProperty("query", out var query) ||
            query.ValueKind != JsonValueKind.String)
            return new(protocol, ErrorCode: "MODEL_TOOL_ARGUMENTS_INVALID");
        string proposed = (query.GetString() ?? "").Trim();
        if (proposed.Length is < 3 or > 240 || proposed.Any(char.IsControl))
            return new(protocol, ErrorCode: "MODEL_TOOL_ARGUMENTS_INVALID");
        return new(protocol, ToolQuery: proposed);
    }

    // Citation IDs are created by the host from actually presented search results.
    // The model chooses which evidence supports a sentence; the host resolves that ID.
    // This is only a provenance/link-identity check, NOT factual entailment verification.
    private static Result CheckAnswer(string text, IReadOnlySet<string> observedUrls,
        string protocol, IReadOnlyDictionary<string, string>? citationUrls)
    {
        string answer = text.Trim();
        int length = answer.Length;
        if (length is < 1 or > MaxAnswerChars)
            return new(protocol, ErrorCode: "MODEL_ANSWER_SIZE_INVALID", CandidateAnswerChars: length);
        if (observedUrls.Count == 0)
            return new(protocol, ErrorCode: "RESEARCH_EVIDENCE_REQUIRED", CandidateAnswerChars: length);

        MatchCollection ids = CitationIds.Matches(answer);
        MatchCollection matches = Urls.Matches(answer);
        int markerCount = ids.Count;
        int urlCount = matches.Count;
        // Unobserved URLs are rejected even when valid source IDs are also provided.
        foreach (Match match in matches)
        {
            string value = match.Value.TrimEnd('.', ',', ';', ':', '"', '\'', '`');
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !observedUrls.Contains(uri.AbsoluteUri))
                return new(protocol, ErrorCode: "UNOBSERVED_SOURCE_URL", CandidateAnswerChars: length,
                    CitationIdMentions: markerCount, UrlMentions: urlCount);
        }

        if (markerCount == 0 && urlCount == 0)
            return new(protocol, ErrorCode: "RESEARCH_CITATION_REQUIRED", CandidateAnswerChars: length);

        foreach (Match id in ids)
        {
            string key = id.Value[1..^1].ToUpperInvariant();
            if (citationUrls is null || !citationUrls.TryGetValue(key, out string? url) ||
                !observedUrls.Contains(url))
                return new(protocol, ErrorCode: "UNOBSERVED_CITATION_ID", CandidateAnswerChars: length,
                    CitationIdMentions: markerCount, UrlMentions: urlCount);
        }
        // Model never provides the link destination for a citation ID. This is a
        // deterministic presentation transform, not evidence inferred by the host.
        if (markerCount > 0)
            answer = BareCitationIds.Replace(answer, m =>
            {
                string key = m.Value[1..^1].ToUpperInvariant();
                string url = citationUrls![key].Replace("(", "%28", StringComparison.Ordinal)
                    .Replace(")", "%29", StringComparison.Ordinal);
                return "[" + key + "](" + url + ")";
            });
        return new(protocol, Answer: answer, CandidateAnswerChars: length,
            CitationIdMentions: markerCount, UrlMentions: urlCount);
    }

    private static bool Unique(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return element.ValueKind == JsonValueKind.Object &&
            element.EnumerateObject().All(field => names.Add(field.Name));
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

/// <summary>
/// Preserves the identity of an explicitly supplied public URL across search
/// planning and source-only recovery. Never establishes a publisher claim or
/// substitutes an external web page for the target itself.
/// </summary>
public static class NIRAWebEvidenceIdentityPolicy
{
    private static readonly Regex PublicLink = new(
        @"https?://[^\s<>""'\]\)]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static Uri? FirstRequestedUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (Match m in PublicLink.Matches(text))
        {
            string raw = m.Value.TrimEnd('.', ',', ';', ':', '!');
            if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                !uri.IsLoopback && !string.IsNullOrWhiteSpace(uri.Host))
                return uri;
        }
        return null;
    }

    // The first discovery must identify the actual linked resource, not
    // hypothesize its title or subject from unrelated conversation history.
    // Only one existing read-only web.search proposal is changed, and its
    // remaining provider, freshness, source and authorization rules survive.
    public static NIRACapabilityRequest AnchorInitialSearch(
        NIRACapabilityRequest request, string? objective,
        bool hasPriorSourceReceipt, out bool changed)
    {
        changed = false;
        if (hasPriorSourceReceipt || request.CapabilityId != NIRACapabilityIds.WebSearch)
            return request;
        Uri? target = FirstRequestedUrl(objective);
        if (target is null || request.Arguments.ValueKind != JsonValueKind.Object)
            return request;
        string raw = request.Arguments.GetRawText();
        using JsonDocument document = JsonDocument.Parse(raw);
        JsonElement args = document.RootElement;
        if (args.TryGetProperty("query", out var one) && one.ValueKind == JsonValueKind.String &&
            ContainsTarget(one.GetString(), target)) return request;
        if (args.TryGetProperty("queries", out var many) && many.ValueKind == JsonValueKind.Array &&
            many.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String &&
                ContainsTarget(x.GetString(), target))) return request;
        JsonObject? node = JsonNode.Parse(raw) as JsonObject;
        if (node is null) return request;
        node.Remove("query");
        node.Remove("queries");
        node["query"] = target.AbsoluteUri;
        changed = true;
        return request with { Arguments = JsonSerializer.SerializeToElement(node) };
    }

    private static bool ContainsTarget(string? query, Uri target)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        // Full URL is a safe, user-supplied identity. Never accept a general
        // topic word or a title guessed by the model as equivalent evidence.
        if (query.Contains(target.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static bool MatchesRequestedUrl(string? objective, JsonElement source)
    {
        Uri? target = FirstRequestedUrl(objective);
        if (target is null) return true;
        if (source.ValueKind != JsonValueKind.Object) return false;
        if (!source.TryGetProperty("url", out JsonElement link) ||
            link.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(link.GetString(), UriKind.Absolute, out Uri? sourceUrl))
            return false;
        // Exact origin/path/query comparison, tolerant only of fragments.
        if (sourceUrl.Scheme == target.Scheme &&
            sourceUrl.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase) &&
            sourceUrl.AbsolutePath.Equals(target.AbsolutePath, StringComparison.Ordinal) &&
            sourceUrl.Query.Equals(target.Query, StringComparison.Ordinal)) return true;
        // Search APIs sometimes return a publisher metadata page using a
        // canonical URL. Require the entire observed source to explicitly
        // include the user-supplied URI; topical resemblance is insufficient.
        return source.TryGetProperty("extract", out JsonElement extract) &&
            extract.ValueKind == JsonValueKind.String &&
            (extract.GetString() ?? string.Empty).Contains(
                target.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    public static JsonElement RelevantSourcePayload(JsonElement payload, string? objective)
    {
        if (FirstRequestedUrl(objective) is null ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("sources", out JsonElement sources) ||
            sources.ValueKind != JsonValueKind.Array) return payload;
        JsonElement[] matching = sources.EnumerateArray()
            .Where(s => MatchesRequestedUrl(objective, s)).ToArray();
        return JsonSerializer.SerializeToElement(new {
            evidenceType = "UntrustedPublicWebSearchExtracts",
            completed = true,
            sourceCount = matching.Length,
            sources = matching
        });
    }
}

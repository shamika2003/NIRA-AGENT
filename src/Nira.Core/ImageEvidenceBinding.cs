using System.Text.Json;

namespace Nira.Core;

/// <summary>Only successful discovery observations in this task can authorize a retrieval proposal.</summary>
public static class ImageEvidenceBinding
{
    public static IEnumerable<JsonElement> Candidates(ConversationState state)
    {
        foreach (var operation in state.Operations.Where(o => o.ToolName == "image.search" && o.Status == OperationStatus.Succeeded && o.ResultJson is not null))
        {
            JsonElement[] values;
            try
            {
                using var document = JsonDocument.Parse(operation.ResultJson!);
                var root = document.RootElement;
                if (!root.TryGetProperty("Success", out var success) || success.ValueKind != JsonValueKind.True ||
                    !root.TryGetProperty("Data", out var data) || !data.TryGetProperty("candidates", out var candidates) ||
                    candidates.ValueKind != JsonValueKind.Array) continue;
                values = candidates.EnumerateArray().Select(c => c.Clone()).ToArray();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { continue; }
            foreach (var value in values) yield return value;
        }
    }

    public static string Field(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";

    // Recognize a standalone image argument object only in a turn with actual
    // discovery evidence. This is a protocol error, never a dispatch instruction.
    public static bool IsBareRetrievalArguments(ConversationState state, string text)
    {
        if (!Candidates(state).Any()) return false;
        string candidate = text.Trim();
        if (candidate.StartsWith("```", StringComparison.Ordinal))
        {
            int start = candidate.IndexOf('\n');
            int end = candidate.LastIndexOf("```", StringComparison.Ordinal);
            if (start < 0 || end <= start || candidate[(end + 3)..].Trim().Length != 0) return false;
            string language = candidate[3..start].Trim();
            if (language.Length != 0 && !language.Equals("json", StringComparison.OrdinalIgnoreCase)) return false;
            candidate = candidate[(start + 1)..end].Trim();
        }
        if (candidate.Length is < 2 or > 65536 || candidate[0] != '{') return false;
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            var root = doc.RootElement;
            return TrySelection(root, out _, out _);
        }
        catch (JsonException) { return false; }
    }

    // Candidate IDs are references to observed metadata, not URLs or authorization.
    // Resolve before approval, never strip query strings or guess new addresses.
    public static string? BindRetrieval(ConversationState state, JsonElement arguments, out JsonElement bound, out string error)
    {
        bound = arguments;
        error = "Select exact candidateIds or URLs from successful image discovery in this task.";
        if (!TrySelection(arguments, out bool byId, out var selection))
        {
            error = "Supply one nonempty selection array (candidateIds or urls); the unused alternative may be omitted or empty. Never mix two nonempty arrays or unknown fields. No download occurred.";
            return "IMAGE_NOT_OBSERVED";
        }
        var candidates = Candidates(state).ToArray();
        static string[] Addresses(JsonElement c) => new[] { Field(c, "ThumbnailUrl"), Field(c, "MediaUrl") }
            .Where(url => url.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var attempted = new HashSet<string>(StringComparer.Ordinal);
        var retrieved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in state.Operations.Where(o => o.ToolName == "image.retrieve" &&
                     o.Status is OperationStatus.Succeeded or OperationStatus.Failed))
        {
            try
            {
                using var args = JsonDocument.Parse(op.ArgumentsJson);
                if (args.RootElement.TryGetProperty("urls", out var urls) && urls.ValueKind == JsonValueKind.Array)
                    foreach (var url in urls.EnumerateArray())
                        if (url.ValueKind == JsonValueKind.String) attempted.Add(url.GetString()!);
                if (op.Status != OperationStatus.Succeeded || op.ResultJson is null) continue;
                using var result = JsonDocument.Parse(op.ResultJson);
                if (result.RootElement.GetProperty("Success").ValueKind != JsonValueKind.True ||
                    !result.RootElement.GetProperty("Data").TryGetProperty("previews", out var previews) ||
                    previews.ValueKind != JsonValueKind.Array) continue;
                foreach (var preview in previews.EnumerateArray())
                    if (Field(preview, "path").Length > 0 && Field(preview, "sha256") is { Length: 64 } hash && hash.All(Uri.IsHexDigit))
                        retrieved.Add(Field(preview, "url"));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { }
        }
        var pending = new List<string>();
        foreach (var item in selection.EnumerateArray())
        {
            string value = item.GetString()!;
            var matches = candidates.Where(c => byId ? Field(c, "CandidateId") == value : Addresses(c).Contains(value, StringComparer.Ordinal)).ToArray();
            if (matches.Length == 0 || (byId && matches.Length != 1)) return "IMAGE_NOT_OBSERVED";
            // Do not fetch another rendition of a candidate already retrieved.
            if (matches.SelectMany(Addresses).Any(retrieved.Contains)) continue;
            string? next = byId ? Addresses(matches[0]).FirstOrDefault(url => !attempted.Contains(url)) :
                attempted.Contains(value) ? null : value;
            if (next is not null && !pending.Contains(next, StringComparer.Ordinal)) pending.Add(next);
        }
        if (pending.Count == 0)
        {
            error = "These candidates or URLs have already been retrieved or attempted. Use the existing successful previews; select a different observed candidate if needed, or report the unavailable images. Do not repeat the same request.";
            return "IMAGE_ALREADY_ATTEMPTED";
        }
        bound = JsonSerializer.SerializeToElement(new { urls = pending });
        if (!Allows(state, bound)) return "IMAGE_NOT_OBSERVED";
        error = "";
        return null;
    }

    private static bool TrySelection(JsonElement arguments, out bool byId, out JsonElement selection)
    {
        byId = false; selection = default;
        if (arguments.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in arguments.EnumerateObject())
        {
            if (field.Name is not ("candidateIds" or "urls") || !seen.Add(field.Name) ||
                field.Value.ValueKind != JsonValueKind.Array || field.Value.GetArrayLength() > 6 ||
                field.Value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)) return false;
            if (field.Value.GetArrayLength() == 0) continue;
            if (selection.ValueKind != JsonValueKind.Undefined) return false;
            selection = field.Value;
            byId = field.Name == "candidateIds";
        }
        return selection.ValueKind == JsonValueKind.Array;
    }

    public static bool Allows(ConversationState state, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("urls", out var urls) || urls.ValueKind != JsonValueKind.Array || urls.GetArrayLength() is < 1 or > 6) return false;
        var observed = Candidates(state).SelectMany(c => new[] { Field(c, "ThumbnailUrl"), Field(c, "MediaUrl") })
            .Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
        return urls.EnumerateArray().All(u => u.ValueKind == JsonValueKind.String && observed.Contains(u.GetString()!));
    }
}

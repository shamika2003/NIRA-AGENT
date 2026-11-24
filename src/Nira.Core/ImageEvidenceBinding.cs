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
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 1 &&
                root.TryGetProperty("urls", out var urls) && urls.ValueKind == JsonValueKind.Array &&
                urls.EnumerateArray().All(u => u.ValueKind == JsonValueKind.String);
        }
        catch (JsonException) { return false; }
    }

    public static bool Allows(ConversationState state, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("urls", out var urls) || urls.ValueKind != JsonValueKind.Array || urls.GetArrayLength() is < 1 or > 6) return false;
        var observed = Candidates(state).SelectMany(c => new[] { Field(c, "ThumbnailUrl"), Field(c, "MediaUrl") })
            .Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
        return urls.EnumerateArray().All(u => u.ValueKind == JsonValueKind.String && observed.Contains(u.GetString()!));
    }
}

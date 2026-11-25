using System.Text.Json;

namespace Nira.Core;

// Evidence is a discovered/user-supplied link, not proof of playback or content.
public static class VideoEvidence
{
    public static IReadOnlySet<string> Allowed(ConversationState state) => new HashSet<string>(Titles(state).Keys, StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Titles(ConversationState state) => Collect(state, false);

    // Fallback only to current successful dedicated video search receipts, never guessed URLs.
    public static IReadOnlyList<string> SearchFallback(ConversationState state)
    {
        var candidates = Collect(state, true).Keys;
        if (!candidates.Any()) return [];
        // This is a display bound, not a task classifier or a tool dispatch rule.
        string request = state.Messages.LastOrDefault(m => m.Role == "user")?.UserDisplayText ??
            state.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? state.Objective;
        var match = System.Text.RegularExpressions.Regex.Match(request[..Math.Min(request.Length, 12000)], @"\b(?<count>[1-6]|one|two|three|four|five|six|single)\s+(?:[\p{L}-]+\s+){0,4}videos?\b|\b(?<count>a|an)\s+(?:[\p{L}-]+\s+){0,3}video\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        int count = match.Groups["count"].Value.ToLowerInvariant() switch
        {
            "1" or "one" or "a" or "an" or "single" => 1,
            "2" or "two" => 2, "3" or "three" => 3, "4" or "four" => 4,
            "5" or "five" => 5, "6" or "six" => 6, _ => 6
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return candidates.Where(url => NIRAVideoReferencePolicy.TryParse(url, out var video) && video is not null && seen.Add(video.Key)).Take(count).ToArray();
    }

    private static IReadOnlyDictionary<string, string> Collect(ConversationState state, bool searchOnly)
    {
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!searchOnly)
        foreach (var entry in state.Messages.Where(m => m.Role == "user"))
            foreach (var item in NIRAVideoReferencePolicy.UserSharedReferences(entry.UserDisplayText ?? entry.Content, 6))
                urls.TryAdd(item.OriginalUrl, "");
        foreach (var op in state.Operations.Where(o => o.Status == OperationStatus.Succeeded &&
                     (o.ToolName == "video.search" || (!searchOnly && o.ToolName == "web.search")) && o.ResultJson is not null))
        {
            try
            {
                using var doc = JsonDocument.Parse(op.ResultJson!);
                var root = doc.RootElement;
                if (!root.TryGetProperty("Success", out var success) || success.ValueKind != JsonValueKind.True ||
                    !root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object ||
                    !data.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in results.EnumerateArray().Take(12))
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("url", out var url) &&
                        url.ValueKind == JsonValueKind.String && NIRAVideoReferencePolicy.TryParse(url.GetString(), out var video) && video is not null)
                    {
                        string title = item.TryGetProperty("title", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString() ?? "" : "";
                        title = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
                        if (title.Length > 180) title = title[..180];
                        if (!urls.ContainsKey(video.OriginalUrl) || title.Length > 0) urls[video.OriginalUrl] = title;
                    }
            }
            catch (JsonException) { }
            catch (InvalidOperationException) { }
        }
        return urls;
    }
    public static bool Allows(IReadOnlySet<string>? urls, string url) =>
        NIRAVideoReferencePolicy.TryParse(url, out var reference) && reference is not null && urls?.Contains(reference.OriginalUrl) == true;
}

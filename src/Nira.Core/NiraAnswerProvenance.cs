using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nira.Core;

/// <summary>Non-executing source-provenance notice. Presence in a receipt is not proof of truth.</summary>
internal static class NiraAnswerProvenance
{
    private static readonly Regex Link = new(@"https?://[^\s<>""'`*\[\]]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Fence = new(@"^\s{0,3}(`{3,}|~{3,})([^\r\n]*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex InlineCode = new(@"(`+)[^\r\n]*?\1(?!`)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Regex SessionReference = new(@"【(?<id>[0-9a-fA-F]{32})】", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    // Convert provider-style citation markers only when a unique current-task page URL
    // was actually recorded for that exact browser session. This establishes provenance,
    // not the accuracy of the page or whether it supports the sentence.
    private static string ResolveSessionReferences(ConversationState state, string answer, out int unresolved)
    {
        unresolved = 0;
        if (!SessionReference.IsMatch(answer)) return answer;
        var pages = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in state.Operations.Where(o => o.ToolName.StartsWith("browser.session_", StringComparison.Ordinal) && o.Status == OperationStatus.Succeeded && o.ResultJson is not null))
        {
            try
            {
                using var doc = JsonDocument.Parse(op.ResultJson!);
                var root = doc.RootElement;
                if (!root.TryGetProperty("Success", out var success) || success.ValueKind != JsonValueKind.True ||
                    !root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object ||
                    !data.TryGetProperty("SessionId", out var idValue) || idValue.ValueKind != JsonValueKind.String) continue;
                string? id = idValue.GetString();
                if (string.IsNullOrEmpty(id)) continue;
                // Other session observations can also establish that the session visited a second page.
                JsonElement urlValue;
                if (data.TryGetProperty("Page", out var page) && page.ValueKind == JsonValueKind.Object && page.TryGetProperty("FinalUrl", out urlValue)) { }
                else if (!data.TryGetProperty("PageUrl", out urlValue)) continue;
                if (urlValue.ValueKind != JsonValueKind.String) continue;
                string url = urlValue.GetString() ?? "";
                if (!pages.TryGetValue(id, out var urls)) pages[id] = urls = new HashSet<string>(StringComparer.Ordinal);
                urls.Add(url);
            }
            catch (JsonException) { }
        }
        int missing = 0;
        string? fence = null;
        var lines = answer.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (fence is not null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0])) fence = null;
                continue;
            }
            Match opening = Fence.Match(line);
            if (opening.Success) { fence = opening.Groups[1].Value; continue; }
            Match[] code = InlineCode.Matches(line).Cast<Match>().ToArray();
            lines[i] = SessionReference.Replace(line, match =>
            {
                if (code.Any(c => match.Index >= c.Index && match.Index < c.Index + c.Length)) return match.Value;
                if (pages.TryGetValue(match.Groups["id"].Value, out var urls) && urls.Count == 1)
                {
                    string url = urls.Single();
                    if (url.Length <= 1900 && !url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '(' or ')' or '[' or ']' or '<' or '>' or '|' or '`') &&
                        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo))
                        return " [source](" + url + ")";
                }
                missing++;
                return " (source reference unverified)";
            });
        }
        unresolved = missing;
        return string.Join("\n", lines);
    }

    internal static string Annotate(ConversationState state, string answer)
    {
        try
        {
            answer = ResolveSessionReferences(state, answer, out int unresolved);
            if (unresolved > 0)
                answer = answer.TrimEnd() + "\n\nSome source references could not be linked to a unique observed page; those claims need further checking.";
            var links = new HashSet<string>(StringComparer.Ordinal);
            string? fence = null;
            foreach (string line in answer.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (fence is not null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0])) fence = null;
                    continue;
                }
                Match opening = Fence.Match(line);
                if (opening.Success) { fence = opening.Groups[1].Value; continue; }
                AddLinks(links, WithoutEmbeddedToolExample(InlineCode.Replace(line, "")));
            }
            if (links.Count == 0) return answer;
            var observed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in state.Operations.Where(op => op.Status == OperationStatus.Succeeded && op.ResultJson is not null))
            {
                try
                {
                    using var parsed = JsonDocument.Parse(operation.ResultJson!);
                    if (parsed.RootElement.TryGetProperty("Success", out var success) && success.ValueKind == JsonValueKind.True &&
                        parsed.RootElement.TryGetProperty("Data", out var data)) CollectStrings(data, observed);
                }
                catch (JsonException) { /* Missing evidence cannot verify a link. */ }
            }
            var supplied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in state.Messages.Where(m => m.Role == "user")) AddLinks(supplied, message.Content ?? "");
            int unobserved = links.Count(link => !observed.Contains(link) && !supplied.Contains(link));
            int userOnly = links.Count(link => !observed.Contains(link) && supplied.Contains(link));
            if (unobserved == 0 && userOnly == 0) return answer;
            string notice = "\n\n**Source check (host):** ";
            if (unobserved > 0) notice += $"{unobserved} model-supplied URL(s) were not found in successful tool results or current user input. Treat them as unverified, not established sources. ";
            if (userOnly > 0) notice += $"{userOnly} user-supplied URL(s) were not observed in successful tool results. ";
            return answer.TrimEnd() + notice.TrimEnd();
        }
        catch (RegexMatchTimeoutException)
        {
            return answer.TrimEnd() + "\n\n**Source check (host):** Link provenance could not be evaluated within the parsing limit.";
        }
    }

    // A prose explanation may include a literal, non-executing JSON tool envelope.
    // Only remove a valid envelope from the citation scan, never from the reply.
    // Reuse the existing protocol recognizer, which does not dispatch any tool.
    // A standalone envelope remains subject to the original execution guard.
    private static string WithoutEmbeddedToolExample(string line)
    {
        int start = line.IndexOf('{');
        int end = line.LastIndexOf('}');
        if (start <= 0 || end <= start || end - start >= 65536 ||
            string.IsNullOrWhiteSpace(line[..start])) return line;
        return NiraEngine.TryReadTextToolAttempt(line[start..(end + 1)], out _)
            ? line[..start] + " " + line[(end + 1)..]
            : line;
    }

    private static void CollectStrings(JsonElement value, HashSet<string> result)
    {
        if (result.Count >= 2048) return;
        switch (value.ValueKind)
        {
            case JsonValueKind.String: AddLinks(result, value.GetString() ?? ""); break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in value.EnumerateObject()) CollectStrings(property.Value, result);
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray()) CollectStrings(item, result);
                break;
        }
    }
    private static void AddLinks(HashSet<string> result, string text)
    {
        if (result.Count >= 2048) return;
        foreach (Match match in Link.Matches(text))
        {
            if (result.Count >= 2048) break;
            string value = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '}');
            if (value.Length <= 4096 && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)) result.Add(uri.AbsoluteUri);
        }
    }
}

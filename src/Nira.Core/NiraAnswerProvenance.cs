using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nira.Core;

/// <summary>Non-executing source-provenance notice. Presence in a receipt is not proof of truth.</summary>
internal static class NiraAnswerProvenance
{
    private static readonly Regex Link = new(@"https?://[^\s<>""'`*\[\]]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Fence = new(@"^\s{0,3}(`{3,}|~{3,})([^\r\n]*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex InlineCode = new(@"(`+)[^\r\n]*?\1(?!`)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static string Annotate(ConversationState state, string answer)
    {
        try
        {
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

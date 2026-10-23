using System.Text.RegularExpressions;

namespace NIRAAgent.Presentation;

// This is ONLY a media presentation URL parser. It cannot verify whether the
// video contains the claimed event; research/citation continues to own that.
// Only explicitly supported public video hosts can receive an embedded surface.
public sealed record NIRAVideoReference(
    string OriginalUrl, string EmbedUrl, string Provider, string VideoId)
{
    public string Key => Provider + ":" + VideoId;
}

public static class NIRAVideoReferencePolicy
{
    private static readonly Regex YouTubeId = new(@"^[a-zA-Z0-9_-]{11}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex VimeoId = new(@"^[0-9]{5,12}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MarkdownVideo = new(
        @"\[(?<label>.+)\]\((?<url>https://[^\s<>]+)\)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string? input, out NIRAVideoReference? reference)
    {
        reference = null;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 2048 ||
            !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 ||
            uri.Port != 443 || uri.HostNameType != UriHostNameType.Dns)
            return false;

        string host = uri.IdnHost.ToLowerInvariant();
        string path = uri.AbsolutePath.Trim('/');
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string id = "";
        string provider = "";
        string embed = "";
        if (host is "youtu.be" or "www.youtu.be")
        {
            if (parts.Length != 1) return false;
            id = parts[0];
            provider = "YouTube";
        }
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or
                 "youtube-nocookie.com" or "www.youtube-nocookie.com")
        {
            if (path.Equals("watch", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string entry in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int split = entry.IndexOf('=');
                    if (split <= 0 || !entry[..split].Equals("v", StringComparison.OrdinalIgnoreCase)) continue;
                    id = Uri.UnescapeDataString(entry[(split + 1)..]);
                    break;
                }
            }
            else if (parts.Length == 2 &&
                     parts[0] is ("shorts" or "live" or "embed"))
                id = parts[1];
            provider = "YouTube";
        }
        else if (host is "vimeo.com" or "www.vimeo.com" or "player.vimeo.com")
        {
            if (parts.Length == 1) id = parts[0];
            else if (parts.Length == 2 && parts[0] == "video") id = parts[1];
            provider = "Vimeo";
        }
        else return false;

        if (provider == "YouTube")
        {
            if (!YouTubeId.IsMatch(id)) return false;
            embed = $"https://www.youtube-nocookie.com/embed/{id}?rel=0&playsinline=1";
        }
        else
        {
            if (!VimeoId.IsMatch(id)) return false;
            embed = $"https://player.vimeo.com/video/{id}?dnt=1&title=0&byline=0";
        }
        reference = new NIRAVideoReference(uri.AbsoluteUri, embed, provider, id);
        return true;
    }

    // Standalone source/watch links from normal prose get the same UI as the
    // structured video block. Never promote an arbitrary inline URL to video.
    public static bool TryParseStandaloneLine(string? line, out NIRAVideoReference? reference,
        out string title)
    {
        reference = null;
        title = "";
        if (string.IsNullOrWhiteSpace(line) || line.Length > 2200) return false;
        string trimmed = Regex.Replace(line.Trim(), @"^(?:[-*+]\s+|\d{1,3}[.)]\s+)", "");
        const string prefix = @"^(?:(?:\*\*|__)(?:video|watch|clip)\s*:(?:\*\*|__)\s*|(?:\*\*|__)?(?:video|watch|clip)(?:\*\*|__)?\s*:\s*)";
        bool specificallyLabeled = Regex.IsMatch(trimmed, prefix,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        trimmed = Regex.Replace(trimmed, prefix, "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // NIRA's answer may emphasize a whole verified watch link, e.g.
        // **[Watch flight](https://youtu.be/...)**. The original parser saw
        // the outer '**' and displayed literal Markdown instead of a player.
        // Remove ONLY whole-line presentation wrappers before applying the
        // existing strict HTTPS/provider/video-ID validation. No inference
        // from ordinary inline prose, generic source links, or topic names.
        for (int wrapper = 0; wrapper < 2; wrapper++)
        {
            if (trimmed.Length >= 4 &&
                ((trimmed.StartsWith("**", StringComparison.Ordinal) &&
                  trimmed.EndsWith("**", StringComparison.Ordinal)) ||
                 (trimmed.StartsWith("__", StringComparison.Ordinal) &&
                  trimmed.EndsWith("__", StringComparison.Ordinal))))
                trimmed = trimmed[2..^2].Trim();
            else break;
        }

        Match markdown = MarkdownVideo.Match(trimmed);
        string url;
        if (markdown.Success && markdown.Index == 0)
        {
            url = markdown.Groups["url"].Value;
            title = markdown.Groups["label"].Value.Trim('[', ']', '*', ' ');
        }
        else if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                 !trimmed.Any(char.IsWhiteSpace))
            url = trimmed;
        else if (specificallyLabeled)
        {
            // A grounded URL can survive malformed model Markdown. Only a
            // standalone, explicitly VIDEO-labeled line earns this recovery;
            // never promote an inline prose URL or invent a missing ID.
            var urls = Regex.Matches(trimmed, @"https://[^\s<>\)\]]+");
            if (urls.Count != 1) return false;
            url = urls[0].Value.TrimEnd('.', ',', ';', '!', '?');
            title = trimmed[..urls[0].Index].Trim(' ', '[', ']', '(', ')', '*', ':');
        }
        else return false;
        if (!TryParse(url, out reference)) { title = ""; return false; }
        if (string.IsNullOrWhiteSpace(title)) title = "Watch video";
        if (title.Length > 125) title = title[..125].TrimEnd() + "…";
        return true;
    }
}

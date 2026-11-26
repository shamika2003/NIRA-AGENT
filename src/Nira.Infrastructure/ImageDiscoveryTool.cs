using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>Search metadata only. Candidate URLs are untrusted and are never fetched here.</summary>
public sealed class ImageDiscoveryTool(HttpClient http, Func<string?>? tavilyKey = null) : IAgentTool
{
    private const string Commons = "https://commons.wikimedia.org/w/api.php";
    private const string Tavily = "https://api.tavily.com/search";
    private const int MaximumBytes = 1_200_000;
    public ToolDescriptor Descriptor { get; } = new("image.search",
        "Find real photographs and reference images of people, places, objects and inventions via Wikimedia Commons or Tavily; returns candidate IDs and source attribution, not verified pixels. " +
        "Commons needs no key; Tavily requires NIRA_TAVILY_API_KEY. Choose a provider explicitly. Never claim retrieval or display.",
        ToolRisk.ReadExternal, ToolSchemas.New("""
        {"type":"object","properties":{"query":{"type":"string","minLength":2,"maxLength":160},
        "provider":{"type":"string","enum":["commons","tavily"]}},
        "required":["query","provider"],"additionalProperties":false}
        """));

    public string GetTarget(JsonElement args)
    {
        string provider = ToolSchemas.String(args, "provider");
        string query = ToolSchemas.String(args, "query").Trim();
        if (query.Length is < 2 or > 160 || query.Any(char.IsControl))
            throw new ArgumentException("Image query must contain 2-160 visible characters.");
        return (provider switch { "commons" => Commons, "tavily" => Tavily,
            _ => throw new ArgumentException("Unknown image provider.") }) + " (image query: " + query + ")";
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        _ = GetTarget(args);
        string query = ToolSchemas.String(args, "query").Trim();
        string provider = ToolSchemas.String(args, "provider");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            using var request = provider == "commons" ? CommonsRequest(query) : TavilyRequest(query);
            if (request is null)
                return ToolReceipt.Fail("IMAGE_SEARCH_KEY_MISSING", "Tavily requires NIRA_TAVILY_API_KEY. Commons is a separate key-free search option.");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return ToolReceipt.Fail("IMAGE_SEARCH_HTTP", $"{provider} returned HTTP {(int)response.StatusCode}.");
            using var data = await ReadBoundedAsync(response.Content, timeout.Token);
            JsonElement root = data.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _))
                return ToolReceipt.Fail("IMAGE_SEARCH_INVALID", "Provider did not return a valid image result.");
            List<ImageCandidate>? candidates = provider == "commons" ? ParseCommons(root) : ParseTavily(root);
            if (candidates is null)
                return ToolReceipt.Fail("IMAGE_SEARCH_INVALID", "Provider response lacks expected image metadata.");
            var results = candidates.DistinctBy(c => c.MediaUrl, StringComparer.Ordinal).Take(6).ToArray();
            return ToolReceipt.Ok(results.Length == 0 ? "No usable image candidates returned." : "Image candidate metadata observed; pixels not retrieved or verified.",
                new { provider, query, count = results.Length, candidates = results, pixelsVerified = false, downloaded = false,
                    note = "Provider metadata is untrusted. Relevance, authenticity, licensing and accessibility are not independently verified. No image file was created. Select relevant CandidateId values for image.retrieve; prefer different subjects and match requested media type using descriptions." });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return ToolReceipt.Fail("IMAGE_SEARCH_TIMEOUT", "Image provider timed out."); }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or IOException or JsonException or FormatException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { return ToolReceipt.Fail("IMAGE_SEARCH_UNAVAILABLE", "Image provider response could not be obtained or parsed."); }
    }

    private static HttpRequestMessage CommonsRequest(string query)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Commons + "?action=query&format=json&formatversion=2" +
            "&generator=search&gsrnamespace=6&gsrlimit=18&gsrsearch=" + Uri.EscapeDataString(query) +
            "&prop=imageinfo&iiprop=url%7Cmime%7Csize%7Cextmetadata&iiurlwidth=960" +
            "&iiextmetadatalanguage=en&iiextmetadatafilter=Artist%7CLicenseShortName%7CLicenseUrl%7CImageDescription");
        request.Headers.UserAgent.ParseAdd("NIRA/0.6 (desktop image discovery by ELVARA)");
        return request;
    }

    private HttpRequestMessage? TavilyRequest(string query)
    {
        string? key = tavilyKey is null ? Environment.GetEnvironmentVariable("NIRA_TAVILY_API_KEY") : tavilyKey();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 8192 || key.Trim().Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return null;
        var request = new HttpRequestMessage(HttpMethod.Post, Tavily);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(new {
            query, search_depth = "basic", max_results = 8, include_answer = false,
            include_raw_content = false, include_images = true, include_image_descriptions = true
        }), Encoding.UTF8, "application/json");
        return request;
    }

    private static async Task<JsonDocument> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaximumBytes) throw new IOException("Image metadata too large.");
        await using Stream stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(), ct)) != 0)
        {
            if (buffer.Length + read > MaximumBytes) throw new IOException("Image metadata too large.");
            buffer.Write(chunk, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }

    private static JsonElement Field(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var field) ? field : default;
    private static string Text(JsonElement value, string key) =>
        Field(value, key) is { ValueKind: JsonValueKind.String } field ? field.GetString() ?? "" : "";
    private static string Clip(string value, int max) => new(value.Where(c => !char.IsControl(c)).Take(max).ToArray());
    private static string Metadata(JsonElement info, string field) => Clip(WebUtility.HtmlDecode(
        System.Text.RegularExpressions.Regex.Replace(Text(Field(Field(info, "extmetadata"), field), "value"),
            "<[^>]*>", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100))), 240);
    private static string Url(string value)
    {
        // These checks filter malformed or obvious non-public references, not a fetch authorization.
        if (value.Length is < 1 or > 1800 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.HostNameType != UriHostNameType.Dns ||
            !uri.Host.Contains('.') || uri.IsLoopback || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return "";
        return value;
    }

    private static List<ImageCandidate>? ParseCommons(JsonElement root)
    {
        JsonElement query = Field(root, "query");
        JsonElement pages = Field(query, "pages");
        if (pages.ValueKind != JsonValueKind.Array)
            return query.ValueKind == JsonValueKind.Undefined && Field(root, "batchcomplete").ValueKind == JsonValueKind.True ? [] : null;
        var results = new List<ImageCandidate>();
        foreach (var page in pages.EnumerateArray().Take(18))
        {
            var infos = Field(page, "imageinfo");
            if (infos.ValueKind != JsonValueKind.Array || infos.GetArrayLength() == 0) continue;
            var info = infos[0];
            string mime = Text(info, "mime");
            if (mime is not ("image/jpeg" or "image/png" or "image/gif" or "image/webp")) continue;
            string media = Url(Text(info, "url"));
            string source = Url(Text(info, "descriptionurl"));
            if (media.Length == 0 || source.Length == 0) continue;
            results.Add(new ImageCandidate(Guid.NewGuid().ToString("N"), Clip(Text(page, "title"), 180),
                Metadata(info, "ImageDescription"), source, media, Url(Text(info, "thumburl")),
                Metadata(info, "Artist"), Metadata(info, "LicenseShortName"), Url(Text(Field(Field(info, "extmetadata"), "LicenseUrl"), "value")), mime));
        }
        return results;
    }

    private static List<ImageCandidate>? ParseTavily(JsonElement root)
    {
        var results = new List<ImageCandidate>();
        void Add(JsonElement item, string source)
        {
            string media = Url(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : Text(item, "url"));
            if (media.Length == 0) return;
            string description = Clip(Text(item, "description"), 240);
            string explicitSource = Url(Text(item, "source_url"));
            results.Add(new ImageCandidate(Guid.NewGuid().ToString("N"), description, description,
                explicitSource.Length > 0 ? explicitSource : source, media, "", "", "Rights not verified", "", ""));
        }
        JsonElement pages = Field(root, "results");
        JsonElement images = Field(root, "images");
        bool recognized = images.ValueKind == JsonValueKind.Array;
        if (pages.ValueKind == JsonValueKind.Array)
            foreach (var page in pages.EnumerateArray().Take(8))
            {
                var items = Field(page, "images");
                if (items.ValueKind != JsonValueKind.Array) continue;
                recognized = true;
                foreach (var image in items.EnumerateArray().Take(12)) Add(image, Url(Text(page, "url")));
            }
        if (images.ValueKind == JsonValueKind.Array)
            foreach (var item in images.EnumerateArray().Take(24)) Add(item, "");
        return recognized ? results : null;
    }

    private sealed record ImageCandidate(string CandidateId, string Title, string Description, string SourceUrl,
        string MediaUrl, string ThumbnailUrl, string Author, string License, string LicenseUrl, string MimeType);
}

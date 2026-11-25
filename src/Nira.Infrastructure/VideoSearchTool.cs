using System.Text;
using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

public sealed class VideoSearchTool(HttpClient http, Func<string?>? keyResolver = null) : IAgentTool, IToolReadiness
{
    private string? Key() => keyResolver is null ? Environment.GetEnvironmentVariable("NIRA_TAVILY_API_KEY") : keyResolver();
    public string? UnavailableReason() => string.IsNullOrWhiteSpace(Key()) ? "Video search requires a Tavily API key. You can still share a video link directly." : null;
    public ToolDescriptor Descriptor { get; } = new("video.search", "Find public YouTube/Vimeo video references using Tavily. Returns links and search excerpts, not watched or verified video contents.", ToolRisk.ReadExternal, JsonToolSchema.Schema("""
        {"type":"object","properties":{"query":{"type":"string","minLength":1,"maxLength":400}},"required":["query"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement arguments) => "https://api.tavily.com/search";
    public async Task<ToolReceipt> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        string? key = Key();
        if (string.IsNullOrWhiteSpace(key)) return ToolReceipt.Fail("VIDEO_SEARCH_KEY_MISSING", "Video search requires a Tavily API key.");
        using var request = new HttpRequestMessage(HttpMethod.Post, GetTarget(arguments));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(new {
            query = arguments.GetProperty("query").GetString(), search_depth = "basic", max_results = 8,
            include_domains = new[] { "youtube.com", "youtu.be", "vimeo.com" }, include_answer = false }), Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return ToolReceipt.Fail("VIDEO_SEARCH_HTTP", "Video search provider returned HTTP " + (int)response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            char[] buffer = new char[64001];
            int count = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
            if (count > 64000) return ToolReceipt.Fail("VIDEO_SEARCH_LIMIT", "Video search response exceeded its size limit.");
            using var doc = JsonDocument.Parse(new string(buffer, 0, count));
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return ToolReceipt.Fail("VIDEO_SEARCH_RESPONSE", "No usable video search response was returned.");
            var found = new List<object>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in results.EnumerateArray().Take(12))
            {
                if (!NIRAVideoReferencePolicy.TryParse(Field(item, "url", 2048), out var video) || video is null || !seen.Add(video.Key)) continue;
                found.Add(new { url = video.OriginalUrl, title = Field(item, "title", 180), excerpt = Field(item, "content", 600), provider = video.Provider });
                if (found.Count == 6) break;
            }
            return ToolReceipt.Ok(found.Count == 0 ? "No supported individual video references found." : "Public video references found; playback not verified.", new { results = found });
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException)
        { return ToolReceipt.Fail("VIDEO_SEARCH_FAILED", "Video search could not return usable results."); }
    }
    private static string Field(JsonElement item, string name, int max)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return "";
        string text = value.GetString() ?? ""; return text[..Math.Min(max, text.Length)];
    }
}

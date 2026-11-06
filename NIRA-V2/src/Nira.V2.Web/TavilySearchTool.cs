using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.Web;

/// <summary>
/// Fixed-endpoint, observe-only Tavily Search adapter. Does not fetch arbitrary result URLs,
/// execute instructions inside source snippets, or expose a mutation capability.
/// </summary>
public sealed class TavilySearchTool : IAgentTool
{
    public const string ToolId = "web.search";
    private static readonly Uri Endpoint = new("https://api.tavily.com/search");
    private readonly HttpClient _http;
    private readonly AgentPayloadVault _vault;
    private readonly Func<string?> _key;
    public AgentToolSpec Spec { get; } = new(ToolId,
        "Search the public web for attributed source excerpts (read-only). Arguments: {query: string}; 3-240 characters.",
        AccessLevel.Observe);

    public TavilySearchTool(HttpClient http, AgentPayloadVault vault, Func<string?>? keySource = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _key = keySource ?? ReadKey;
    }

    public static string? ReadKey()
    {
        foreach (string name in new[] { "NIRA_TAVILY_API_KEY", "TAVILY_API_KEY" })
        {
            string? key = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(key)) return key.Trim();
            if (OperatingSystem.IsWindows())
            {
                key = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(key)) return key.Trim();
            }
        }
        return null;
    }

    public async Task<AgentToolReceipt> ExecuteAsync(string argumentReference, CancellationToken ct)
    {
        JsonElement args;
        Guid runId;
        try
        {
            runId = _vault.RunIdFromArgument(argumentReference);
            args = await _vault.ReadToolArgumentAsync(ToolId, argumentReference, ct);
        }
        catch (Exception e) when (e is InvalidDataException or FileNotFoundException or JsonException)
        { return new AgentToolReceipt(false, false, null, "INVALID_SCOPED_ARGUMENT"); }

        if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() != 1 ||
            !args.TryGetProperty("query", out var queryNode) || queryNode.ValueKind != JsonValueKind.String)
            return new AgentToolReceipt(false, false, null, "INVALID_QUERY_SCHEMA");
        string query = (queryNode.GetString() ?? "").Trim();
        if (query.Length is < 3 or > 240 || query.Any(char.IsControl))
            return new AgentToolReceipt(false, false, null, "INVALID_QUERY_LENGTH");
        string? key = _key()?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return new AgentToolReceipt(false, false, null, "TAVILY_API_KEY_MISSING");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { query, max_results = 5, search_depth = "basic",
                include_answer = false, include_images = false })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException) { return new AgentToolReceipt(false, false, null, "SEARCH_NETWORK_ERROR"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new AgentToolReceipt(false, false, null, "SEARCH_TIMEOUT"); }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string code = (int)response.StatusCode switch
                {
                    401 or 403 => "SEARCH_AUTH_REJECTED",
                    429 => "SEARCH_RATE_LIMITED",
                    >= 500 => "SEARCH_PROVIDER_UNAVAILABLE",
                    _ => "SEARCH_HTTP_ERROR"
                };
                return new AgentToolReceipt(false, false, null, code);
            }
            if (response.Content.Headers.ContentLength > 256 * 1024)
                return new AgentToolReceipt(false, false, null, "SEARCH_RESPONSE_TOO_LARGE");
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var bytes = new MemoryStream();
                byte[] chunk = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(chunk, ct)) > 0)
                {
                    if (bytes.Length + count > 256 * 1024)
                        return new AgentToolReceipt(false, false, null, "SEARCH_RESPONSE_TOO_LARGE");
                    bytes.Write(chunk, 0, count);
                }
                bytes.Position = 0;
                using var json = await JsonDocument.ParseAsync(bytes, cancellationToken: ct);
                if (!json.RootElement.TryGetProperty("results", out var records) ||
                    records.ValueKind != JsonValueKind.Array)
                    return new AgentToolReceipt(false, false, null, "SEARCH_SCHEMA_INVALID");
                var sources = new List<Source>();
                var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonElement item in records.EnumerateArray())
                {
                    if (sources.Count >= 5) break;
                    if (item.ValueKind != JsonValueKind.Object ||
                        !item.TryGetProperty("url", out var uriField) ||
                        uriField.ValueKind != JsonValueKind.String ||
                        !SafePublicSource(uriField.GetString(), out string? url) ||
                        !urls.Add(url)) continue;
                    string title = item.TryGetProperty("title", out var name) &&
                        name.ValueKind == JsonValueKind.String ? Clean(name.GetString(), 160) : "Untitled source";
                    string extract = item.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String ? Clean(content.GetString(), 700) : "";
                    if (extract.Length < 25) continue;
                    sources.Add(new Source("S" + (sources.Count + 1), title, url, extract));
                }
                if (sources.Count == 0)
                    return new AgentToolReceipt(false, false, null, "NO_USABLE_SEARCH_RESULTS");
                var structured = JsonSerializer.SerializeToElement(new { provider = "Tavily",
                    query, sources }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                string reference = await _vault.SaveResultAsync(runId, ToolId, structured, ct);
                return AgentToolReceipt.ReadOnlyResult(reference);
            }
            catch (JsonException) { return new AgentToolReceipt(false, false, null, "SEARCH_SCHEMA_INVALID"); }
        }
    }

    private static string Clean(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return text.Length <= length ? text : text[..length];
    }

    private static bool SafePublicSource(string? value, out string url)
    {
        url = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.HostNameType != UriHostNameType.Dns ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
        url = uri.AbsoluteUri;
        return true;
    }

    private sealed record Source(string Id, string Title, string Url, string Extract);
}

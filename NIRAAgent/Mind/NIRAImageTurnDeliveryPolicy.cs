using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

// Mechanical fulfillment invariants only. The model still decides WHETHER images
// are useful and WHAT to search. Never contains subject/product/place keywords.
public static class NIRAImageTurnDeliveryPolicy
{
    // Parse a quantity only when the user explicitly attaches it to a visual
    // deliverable. This is not a tool-intent detector or subject-specific router.
    // Other quantities and unbounded requests remain model-controlled.
    private static readonly Regex ExplicitCount = new(
        @"\b(?<count>[1-3]|one|two|three)\b(?:[^\p{L}\p{N}]+[\p{L}\p{N}]+){0,3}?[^\p{L}\p{N}]+(?:photos?|photographs?|pictures?|images?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static int? ReadExplicitImageCount(string? userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return null;
        Match match = ExplicitCount.Match(userText);
        if (!match.Success) return null;
        return match.Groups["count"].Value.ToLowerInvariant() switch
        {
            "one" or "1" => 1,
            "two" or "2" => 2,
            "three" or "3" => 3,
            _ => null
        };
    }

    public static bool ExplicitPhotographRequest(string? userText) =>
        !string.IsNullOrWhiteSpace(userText) && Regex.IsMatch(userText,
            @"\b(?:photos?|photographs?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static int SearchRetrieveCount(NIRACapabilityRequest request)
    {
        if (request.CapabilityId != NIRACapabilityIds.ImageSearch ||
            request.Arguments.ValueKind != JsonValueKind.Object ||
            (request.Arguments.TryGetProperty("retrieveBest", out var automatic) &&
             automatic.ValueKind == JsonValueKind.False))
            return 0;
        return request.Arguments.TryGetProperty("retrieveCount", out var count) &&
               count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int n)
            ? Math.Clamp(n, 1, 3) : 1;
    }

    public static NIRACapabilityRequest ApplyExplicitCount(
        NIRACapabilityRequest request, int? count, bool photographsOnly = false)
    {
        if (request.CapabilityId != NIRACapabilityIds.ImageSearch ||
            (count is null && !photographsOnly) ||
            SearchRetrieveCount(request) == 0) return request;
        if (count is not null && count.Value is < 1 or > 3) return request;
        if (count == SearchRetrieveCount(request) && !photographsOnly) return request;
        // Schema preflight has already validated the ordinary model request;
        // only the protocol's optional image quantity is corrected.
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            request.Arguments.GetRawText())!;
        if (count.HasValue)
            args["retrieveCount"] = JsonSerializer.SerializeToElement(count.Value);
        if (photographsOnly)
            args["photographsOnly"] = JsonSerializer.SerializeToElement(true);
        return request with { Arguments = JsonSerializer.SerializeToElement(args) };
    }

    // Byte identity survives cache filename changes, alternate candidate IDs,
    // repeated image.search calls and image.retrieve fallback.
    public static string PixelIdentity(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (IOException) { return "PATH:" + path; }
        catch (UnauthorizedAccessException) { return "PATH:" + path; }
        catch (ArgumentException) { return "PATH:" + path; }
    }

    public static string SourceIdentity(string? uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) &&
            parsed.Scheme is "http" or "https")
            return parsed.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return uri ?? string.Empty;
    }
}

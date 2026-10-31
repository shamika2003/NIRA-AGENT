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
        // "one photo OF EACH subject" is a per-subject instruction, NOT a
        // global one-photo quota. The Executive uses the actual independent
        // image.search requests to determine the total deliverable count.
        // The grammar is intentionally subject-neutral.
        if (IsPerSubjectImageQuantity(userText)) return null;
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

    // Per-item quantities cannot be collapsed into a single global budget.
    // "a photo for each", "one image of each", "a picture per item" are
    // distinct from "one photo of the group". Subject counting is delegated
    // to the model's separate image.search proposals, not guessed from nouns.
    private static readonly Regex PerSubjectImageQuantity = new(
        @"\b(?:(?:one|1|a|an)\s+(?:(?:genuine|real|actual|original|clear)\s+){0,3}(?:photo|photograph|picture|image)\s+(?:of|for)\s+each\b|(?:one|1|a|an)\s+(?:(?:genuine|real|actual|original|clear)\s+){0,3}(?:photo|photograph|picture|image)\s+per\s+\w+\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsPerSubjectImageQuantity(string? userText) =>
        !string.IsNullOrWhiteSpace(userText) &&
        PerSubjectImageQuantity.IsMatch(userText);

    // A very narrow, content-agnostic grammar for an image-ONLY delivery.
    // This is NOT an image search trigger: the first cognition call must have
    // already chosen image.search and the real tool must have delivered pixels.
    // Any conjunction or second question falls back to the normal reviewer.
    // The grammar intentionally recognizes no specific subject or example.
    private static readonly Regex SimpleImageOnlyRequest = new(
        @"^\s*(?:show|find|display|give|send|get)\s+(?:(?:me|us)\s+)?" +
        @"(?:[1-3]|one|two|three)\s+" +
        @"(?:(?:real|actual|genuine|different|good|clear|high-quality|official)\s+){0,5}" +
        @"(?:photos?|photographs?|pictures?|images?)\s+(?:of|from|showing)\s+" +
        @"(?<subject>[^\r\n?!:;]{2,160}?)\s*\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsSimpleImageOnlyRequest(string? userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return false;
        Match request = SimpleImageOnlyRequest.Match(userText);
        if (!request.Success || ReadExplicitImageCount(userText) is null) return false;
        string subject = request.Groups["subject"].Value.Trim();
        return subject.Length >= 2 && !Regex.IsMatch(subject,
            @"\b(?:and|then|also|plus|after|before)\b|[.!].*\S",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // Mechanical DELIVERY distinction, not a topic-specific tool router:
    // visual research may support creating a requested saved artifact rather
    // than itself being the user's visible deliverable. The model still decides
    // which tools and source material the task needs. A direct image-only
    // request retains normal inline image delivery.
    private static readonly Regex SavedArtifactRequest = new(
        @"\b(?:create|make|prepare|write|draft|design|generate|produce|build|compile|export|save)\b" +
        @"[\s\S]{0,450}\b(?:assignment|report|essay|document|file|spreadsheet|workbook|presentation|slides|pdf|docx|xlsx|pptx|website|application|project)\b" +
        @"|\b(?:save|export)\b[\s\S]{0,200}\b(?:desktop|downloads|folder|file)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsArtifactFirstRequest(string? userText) =>
        !string.IsNullOrWhiteSpace(userText) &&
        !IsSimpleImageOnlyRequest(userText) &&
        SavedArtifactRequest.IsMatch(userText);

    public static bool ExplicitPhotographRequest(string? userText) =>
        !string.IsNullOrWhiteSpace(userText) && Regex.IsMatch(userText,
            @"\b(?:photos?|photographs?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static int SearchRetrieveCount(NIRACapabilityRequest request)
    {
        if (request.CapabilityId is not (NIRACapabilityIds.ImageSearch or NIRACapabilityIds.WebSearch) ||
            request.Arguments.ValueKind != JsonValueKind.Object ||
            (request.CapabilityId == NIRACapabilityIds.ImageSearch &&
             request.Arguments.TryGetProperty("retrieveBest", out var automatic) &&
             automatic.ValueKind == JsonValueKind.False) ||
            (request.CapabilityId == NIRACapabilityIds.WebSearch &&
             (!request.Arguments.TryGetProperty("includeImages", out var combined) ||
              combined.ValueKind != JsonValueKind.True)))
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

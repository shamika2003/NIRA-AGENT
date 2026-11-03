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
    public const int ImagesPerSearch = 5;
    public const int MaximumImagesPerTurn = 30;

    // Count the requested deliverable, including modifiers such as "four real photographs".
    // This is a quantity parser ONLY; the first model still chooses the tool and subject.
    private static readonly Regex ExplicitCount = new(
        @"\b(?<count>\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten|twelve)\b" +
        @"(?:\s+(?:real|authentic|genuine|actual|different|good|clear|high-quality|historical|original|distinct)){0,5}" +
        @"\s+(?:photos?|photographs?|pictures?|images?)\b",
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
        string raw = match.Groups["count"].Value.ToLowerInvariant();
        int n = raw switch {
            "one" => 1, "two" => 2, "three" => 3, "four" => 4,
            "five" => 5, "six" => 6, "seven" => 7, "eight" => 8,
            "nine" => 9, "ten" => 10, "twelve" => 12,
            _ => int.TryParse(raw, out int numeric) ? numeric : 0
        };
        return n is >= 1 and <= MaximumImagesPerTurn ? n : null;
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
        @"^\s*(?:nira[, ]+)?(?:show|find|display|give|send|get)\s+(?:(?:me|us)\s+)?" +
        @"(?:\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten|twelve)\b" +
        @"[\s\S]{0,85}?\b(?:photos?|photographs?|pictures?|images?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsSimpleImageOnlyRequest(string? userText)
    {
        if (string.IsNullOrWhiteSpace(userText) ||
            ReadExplicitImageCount(userText) is null ||
            !SimpleImageOnlyRequest.IsMatch(userText)) return false;
        // Qualifying sentences about wanting to SEE the subject are still one
        // image-only objective. A separate research, writing or action request is not.
        return !Regex.IsMatch(userText,
            @"\b(?:research|explain|compare|summarize|create|make|save|export|write|build|delete|remove|rename|edit|update|calculate|analyze|analyse|install|run|move\s+files?|download\s+to|tell\s+me\s+about|also\s+find\s+websites?)\b",
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
            ? Math.Clamp(n, 1, ImagesPerSearch) : 1;
    }

    public static NIRACapabilityRequest ApplyExplicitCount(
        NIRACapabilityRequest request, int? count, bool photographsOnly = false)
    {
        if (request.CapabilityId != NIRACapabilityIds.ImageSearch ||
            (count is null && !photographsOnly) ||
            SearchRetrieveCount(request) == 0) return request;
        if (count is not null && count.Value is < 1 or > ImagesPerSearch) return request;
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

    // First-call fulfillment planner. Turns ONE model-selected image need into
    // independent registered read-only requests, before schema preflight.
    // Segments use disjoint candidate offsets, while the provider caches the
    // identical search and therefore avoids redundant paid search calls.
    // Nothing here guesses a subject or grants authority; the Executive still
    // validates and authorizes EACH tool request before execution.
    public static IReadOnlyList<NIRACapabilityRequest> PrepareInitialImageBatch(
        IReadOnlyList<NIRACapabilityRequest> requests, int? requestedCount,
        bool photographsOnly)
    {
        if (requests.Count == 0) return requests;
        var selected = requests.Where(r => r.CapabilityId == NIRACapabilityIds.ImageSearch &&
            r.Arguments.ValueKind == JsonValueKind.Object &&
            (!r.Arguments.TryGetProperty("retrieveBest", out var best) ||
             best.ValueKind != JsonValueKind.False)).ToArray();
        if (selected.Length == 0) return requests;
        if (!requestedCount.HasValue)
            return requests.Select(r => ApplyExplicitCount(r, null, photographsOnly)).ToArray();
        int needed = requestedCount.Value;
        // Divide a global count across the SUBJECTS the model independently
        // selected. A photo "of each" intentionally has no global count.
        int baseCount = needed / selected.Length;
        int extra = needed % selected.Length;
        var batches = new List<NIRACapabilityRequest>();
        int index = 0;
        foreach (var request in requests)
        {
            if (!selected.Contains(request)) { batches.Add(request); continue; }
            int allocated = baseCount + (index++ < extra ? 1 : 0);
            if (allocated <= 0) continue;
            int segments = (allocated + ImagesPerSearch - 1) / ImagesPerSearch;
            int searchDepth = Math.Min(64, Math.Max(12, segments * 8 + 9));
            for (int segment = 0, remaining = allocated; segment < segments; segment++)
            {
                int count = Math.Min(ImagesPerSearch, remaining);
                remaining -= count;
                var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    request.Arguments.GetRawText())!;
                args["retrieveBest"] = JsonSerializer.SerializeToElement(true);
                // When the user asks for a small exact photograph count, one
                // duplicated source/pixel can otherwise drop the visible batch
                // below the requested number and trigger an unnecessary second
                // cognition/research round. Overfetch at most ONE alternate
                // candidate within the same paid search call; the Executive
                // still delivers no more than the user's target count.
                int fetchCount = count < ImagesPerSearch ? Math.Min(ImagesPerSearch, count + 1) : count;
                args["retrieveCount"] = JsonSerializer.SerializeToElement(fetchCount);
                args["limit"] = JsonSerializer.SerializeToElement(searchDepth);
                args["skipCandidates"] = JsonSerializer.SerializeToElement(segment * 8);
                if (photographsOnly)
                    args["photographsOnly"] = JsonSerializer.SerializeToElement(true);
                batches.Add(request with { Arguments = JsonSerializer.SerializeToElement(args) });
            }
        }
        return batches;
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

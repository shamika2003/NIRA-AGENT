using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

// Binds observed, local, previously accepted read-only image results to image
// placeholders in a user-requested Word Studio job. This cannot fetch files,
// invent image paths, change the quality gate, or authorize a file write.
internal static class NIRADocxVerifiedMediaBinding
{
    private static readonly HashSet<string> GenericTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "images", "photo", "photos", "photograph", "photographs", "picture", "figure",
        "illustration", "real", "verified", "official", "source", "credit", "web", "space", "view",
        "showing", "show", "high", "resolution", "example", "report", "research", "the", "from",
        "with", "and", "for", "about", "in", "of", "this", "its", "a", "an", "by", "on"
    };

    private static HashSet<string> Tokens(string value) => Regex.Matches(value ?? "", @"[\p{L}\p{N}]{3,}")
        .Select(match => match.Value.ToLowerInvariant())
        .Where(token => !GenericTerms.Contains(token))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Text(JsonObject block, string name)
    {
        try { return block[name]?.GetValue<string>() ?? ""; }
        catch (InvalidOperationException) { return ""; }
        catch (FormatException) { return ""; }
    }

    internal static NIRACapabilityRequest Apply(
        NIRACapabilityRequest request,
        IReadOnlyList<NIRACapabilityVisualArtifact> acceptedImages,
        out int bound)
    {
        bound = 0;
        if (request.CapabilityId != NIRACapabilityIds.DocxStudioCreate ||
            acceptedImages.Count == 0 || request.Arguments.ValueKind != JsonValueKind.Object)
            return request;

        JsonObject? root;
        try { root = JsonNode.Parse(request.Arguments.GetRawText()) as JsonObject; }
        catch (JsonException) { return request; }
        if (root is null || !root.TryGetPropertyValue("blocksJson", out JsonNode? body) || body is null)
            return request;
        bool legacy = false;
        JsonArray? blocks = body as JsonArray;
        if (blocks is null && body is JsonValue scalar)
        {
            string? encoded;
            try { encoded = scalar.GetValue<string>(); }
            catch (InvalidOperationException) { return request; }
            if (encoded is null || encoded.Length > 180000) return request;
            try { blocks = JsonNode.Parse(encoded) as JsonArray; }
            catch (JsonException) { return request; }
            legacy = true;
        }
        if (blocks is null || blocks.Count == 0 || blocks.Count > 120) return request;

        var candidates = acceptedImages
            .Where(asset => !string.IsNullOrWhiteSpace(asset.LocalPath) &&
                Path.IsPathFullyQualified(asset.LocalPath) && File.Exists(asset.LocalPath))
            .Where(asset =>
            {
                try { long length = new FileInfo(asset.LocalPath).Length; return length is >= 24 and <= 5242880; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            })
            .GroupBy(asset => asset.LocalPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).Take(6).ToArray();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? item in blocks)
        {
            if (item is not JsonObject obj ||
                !string.Equals(Text(obj, "type"), "image", StringComparison.OrdinalIgnoreCase)) continue;

            string existing = Text(obj, "imagePath");
            if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
            {
                used.Add(existing);
                continue; // Preserve the author's valid explicit image file.
            }

            string description = string.Join(' ', Text(obj, "altText"), Text(obj, "caption"));
            HashSet<string> desired = Tokens(description);
            // Do not fill unspecified or unrelated pictures with an arbitrary
            // image simply to pass minimumImages; provenance and subject matter.
            if (desired.Count == 0) continue;
            NIRACapabilityVisualArtifact? match = candidates
                .Where(asset => !used.Contains(asset.LocalPath))
                .Select(asset => (Asset: asset, Score: Tokens(string.Join(' ', asset.Title,
                    asset.SubjectQuery, asset.Caption)).Intersect(desired).Count()))
                .Where(entry => entry.Score > 0)
                .OrderByDescending(entry => entry.Score)
                .Select(entry => entry.Asset).FirstOrDefault();
            if (match is null) continue;
            obj["imagePath"] = match.LocalPath;
            used.Add(match.LocalPath);
            bound++;
        }
        if (bound == 0) return request;
        root["blocksJson"] = legacy ? JsonValue.Create(blocks.ToJsonString()) : blocks.DeepClone();
        return request with { Arguments = JsonSerializer.SerializeToElement(root) };
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace NIRAAgent.AI.Cognition;

/// <summary>One trusted image input contract for chat attachments, screenshots and discovered local files.</summary>
public static class NIRAImageInput
{
    public const string MetadataKey = "nira.user.images.v1";
    // User turns may include a larger collection. Only bounded image batches
    // are sent to the model, so no image is silently dropped.
    public const int MaximumImagesPerTurn = 24;
    public const int MaximumImagesPerModelCall = 8;
    private const long MaximumImageBytes = 20L * 1024 * 1024;
    public const long MaximumModelCallBytes = 32L * 1024 * 1024;
    private const long MaximumCombinedBytes = 96L * 1024 * 1024;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };

    public static IReadOnlyList<string> Validate(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var result = new List<string>();
        long total = 0;
        foreach (string candidate in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            string path = Path.GetFullPath(candidate.Trim().Trim('"'));
            if (!Extensions.Contains(Path.GetExtension(path)))
                throw new InvalidOperationException("Unsupported image type: " + Path.GetExtension(path));
            if (result.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            if (result.Count >= MaximumImagesPerTurn)
                throw new InvalidOperationException(
                    $"A single message supports up to {MaximumImagesPerTurn} images. " +
                    "Send additional images in a separate message.");
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("Selected image does not exist.", path);
            if (file.Length == 0 || file.Length > MaximumImageBytes)
                throw new InvalidOperationException("Image is empty or exceeds the 20 MiB individual limit.");
            total += file.Length;
            if (total > MaximumCombinedBytes)
                throw new InvalidOperationException("Images exceed the 96 MiB combined limit for one message.");
            result.Add(path);
        }
        return result;
    }

    /// <summary>
    /// Groups ALL validated images in original order into bounded requests.
    /// One group means one direct multimodal cognition call; more than one
    /// group is first observed in batches and then synthesized by cognition.
    /// This method performs no LLM calls and never discards images.
    /// </summary>
    public static IReadOnlyList<NIRAImageBatch> PlanBatches(IEnumerable<string> paths)
    {
        IReadOnlyList<string> images = Validate(paths);
        if (images.Count == 0) return Array.Empty<NIRAImageBatch>();

        var result = new List<NIRAImageBatch>();
        var current = new List<string>();
        long currentBytes = 0;
        int firstIndex = 1;

        foreach (string path in images)
        {
            long length = new FileInfo(path).Length;
            if (current.Count > 0 &&
                (current.Count == MaximumImagesPerModelCall ||
                 currentBytes + length > MaximumModelCallBytes))
            {
                result.Add(new NIRAImageBatch(firstIndex, current.ToArray()));
                firstIndex += current.Count;
                current = new List<string>();
                currentBytes = 0;
            }

            current.Add(path);
            currentBytes += length;
        }

        if (current.Count > 0)
            result.Add(new NIRAImageBatch(firstIndex, current.ToArray()));

        return result;
    }

    public static string Serialize(IEnumerable<string> paths) => JsonSerializer.Serialize(Validate(paths));

    public static IReadOnlyList<string> ReadMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(MetadataKey, out string? encoded) || string.IsNullOrWhiteSpace(encoded))
            return Array.Empty<string>();
        try { return Validate(JsonSerializer.Deserialize<string[]>(encoded) ?? Array.Empty<string>()); }
        catch (JsonException) { throw new InvalidOperationException("Invalid image attachment metadata."); }
    }

    // Explicit absolute image paths in user text are admitted only when the
    // file actually exists; a missing path never becomes hallucinated vision.
    public static IReadOnlyList<string> FindExplicitLocalImages(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return Array.Empty<string>();
        var found = new List<string>();
        foreach (Match match in Regex.Matches(userText,
            """[A-Za-z]:\\[^\r\n"<>|*?]+?\.(?:png|jpe?g|webp|bmp|gif)(?=\s|$|"|[),;])""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            string path = match.Value.Trim();
            if (File.Exists(path)) found.Add(path);
        }
        return found.Count == 0 ? Array.Empty<string>() : Validate(found);
    }
}

/// <summary>One contiguous, locally validated set of images for a vision request.</summary>
public sealed record NIRAImageBatch(int FirstImageNumber, IReadOnlyList<string> Paths);

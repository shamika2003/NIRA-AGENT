using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NIRAAgent.Capabilities;

// Input-driven media, chart and completeness helpers shared by all Word Studio subjects.
internal static partial class NIRADocx
{
    private sealed record Studio48Media(byte[] Bytes, string Extension, string Mime,
        int Width, int Height, string FileName, string RelationshipId);

    private static async Task<List<Studio48Media>> LoadStudio48MediaAsync(
        JsonElement blocks, string? legacyImagePath, CancellationToken cancellationToken)
    {
        List<Studio48Media> media = new();
        long totalBytes = 0;
        foreach (JsonElement block in blocks.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object ||
                !block.TryGetProperty("type", out JsonElement kind) ||
                kind.ValueKind != JsonValueKind.String ||
                !string.Equals(kind.GetString(), "image", StringComparison.OrdinalIgnoreCase)) continue;
            if (media.Count == 6)
                throw new ArgumentException("Word Studio allows up to six images per document.");
            string path = GetString48(block, "imagePath", 32760, false);
            if (path.Length == 0 && media.Count == 0)
                path = legacyImagePath ?? "";
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Each image block needs a verified local imagePath.");
            NIRAFileIntegrity.RejectLinkedPaths(path);
            FileInfo file = new(path);
            if (!file.Exists || file.Length is < 24 or > 5 * 1024 * 1024)
                throw new ArgumentException("Image input must be an existing PNG/JPEG no larger than 5 MiB.");
            totalBytes += file.Length;
            if (totalBytes > 15 * 1024 * 1024)
                throw new ArgumentException("Image inputs exceed the combined 15 MiB limit.");
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var metadata = VerifyImage(bytes);
            int index = media.Count + 1;
            media.Add(new Studio48Media(bytes, metadata.Ext, metadata.Mime,
                metadata.Width, metadata.Height,
                index == 1 ? $"nira-r48.{metadata.Ext}" : $"studio-image-{index}.{metadata.Ext}",
                $"rId{index + 4}"));
        }
        if (!string.IsNullOrWhiteSpace(legacyImagePath) && media.Count != 1)
            throw new ArgumentException("Top-level imagePath is only supported for one image block; use per-block imagePath for multiple images.");
        return media;
    }

    private static int CountStudio48BodyWords(JsonElement blocks)
    {
        int total = 0;
        foreach (JsonElement block in blocks.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object ||
                !block.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String) continue;
            string kind = (type.GetString() ?? "").ToLowerInvariant();
            if (kind is "paragraph" or "bullet" or "number" or "quote")
            {
                if (block.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                    total += CountStudioWords(text.GetString() ?? "");
                if (block.TryGetProperty("runs", out JsonElement runs) && runs.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement run in runs.EnumerateArray())
                        if (run.ValueKind == JsonValueKind.Object && run.TryGetProperty("text", out JsonElement part) &&
                            part.ValueKind == JsonValueKind.String)
                            total += CountStudioWords(part.GetString() ?? "");
            }
            else if (kind == "table" && block.TryGetProperty("rows", out JsonElement rows) &&
                     rows.ValueKind == JsonValueKind.Array)
                foreach (JsonElement row in rows.EnumerateArray())
                    if (row.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement cell in row.EnumerateArray())
                            if (cell.ValueKind == JsonValueKind.String)
                                total += CountStudioWords(cell.GetString() ?? "");
        }
        return total;
    }

    private static int CountStudioWords(string text) =>
        Regex.Matches(text, @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*").Count;

    // A Word-native, freely editable table visualization. It is deliberately
    // not misreported as an embedded Office chart object or source-verified statistics.
    private static IEnumerable<XElement> ChartTable48(JsonElement data, string accent, int availableTwips)
    {
        if (!data.TryGetProperty("data", out JsonElement points) ||
            points.ValueKind != JsonValueKind.Array || points.GetArrayLength() is < 2 or > 12)
            throw new ArgumentException("chart.data requires 2-12 numeric observations.");
        List<(string Label, double Value)> values = new();
        foreach (JsonElement point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Object ||
                !point.TryGetProperty("value", out JsonElement amount) ||
                amount.ValueKind != JsonValueKind.Number ||
                !amount.TryGetDouble(out double numeric) ||
                !double.IsFinite(numeric) || numeric < 0 || numeric > 1e12)
                throw new ArgumentException("Each chart point requires a nonnegative, finite numeric value.");
            string label = GetString48(point, "label", 100);
            values.Add((label, numeric));
        }
        string title = GetString48(data, "title", 180, false);
        if (title.Length != 0)
        {
            XElement heading = P48("left", 130, "Heading3");
            heading.Add(TextRun48(title, true, false, false, accent, 12));
            yield return heading;
        }
        double maximum = values.Max(item => item.Value);
        var rows = new List<string[]> { new[] { "Category", "Relative magnitude", "Value" } };
        foreach ((string label, double value) in values)
        {
            int count = maximum == 0 ? 0 : (int)Math.Round(20 * value / maximum);
            if (value > 0 && count == 0) count = 1;
            rows.Add(new[] { label, new string('█', count), value.ToString("#,##0.##", CultureInfo.InvariantCulture) });
        }
        using JsonDocument wrapped = JsonDocument.Parse(JsonSerializer.Serialize(new { rows }));
        yield return Table48(wrapped.RootElement, accent, availableTwips);
        string source = GetString48(data, "source", 400, false);
        if (source.Length != 0)
        {
            XElement credit = P48("left", 100);
            credit.Add(TextRun48(source, false, true, false, "65758A", 8));
            yield return credit;
        }
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

public sealed record RecordedFileArtifact(string Path, string Sha256, string ToolName);

public static class RecordedFileArtifacts
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".xlsx", ".docx", ".pdf", ".txt", ".csv", ".md", ".png", ".jpg", ".jpeg", ".bmp" };

    public static bool IsOpenable(string path) => Extensions.Contains(Path.GetExtension(path));

    public static IReadOnlyList<RecordedFileArtifact> From(ConversationState state)
    {
        var files = new List<RecordedFileArtifact>();
        foreach (var op in state.Operations.AsEnumerable().Reverse())
        {
            if (op.Status != OperationStatus.Succeeded || op.ResultJson is null) continue;
            bool revision = op.ToolName is "productivity.revise_workbook" or "productivity.revise_document" or "productivity.pdf_export_text";
            if (!revision && op.ToolName is not ("productivity.create_workbook" or "productivity.create_document" or
                "filesystem.write_text" or "filesystem.edit_text")) continue;
            try
            {
                using var doc = JsonDocument.Parse(op.ResultJson);
                var root = doc.RootElement;
                if (!True(root, "Success") || !True(root, "ChangedState") ||
                    !root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
                if (op.ToolName.StartsWith("productivity.", StringComparison.Ordinal) && !True(data, "contentVerified")) continue;
                if (!data.TryGetProperty(revision ? "outputPath" : "path", out var pathValue) || pathValue.ValueKind != JsonValueKind.String ||
                    !data.TryGetProperty(revision ? "outputSha256" : "sha256", out var hashValue) || hashValue.ValueKind != JsonValueKind.String) continue;
                string path = pathValue.GetString()!; string hash = hashValue.GetString()!;
                if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl) || !IsOpenable(path) ||
                    hash.Length != 64 || !hash.All(char.IsAsciiHexDigit) || files.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
                files.Add(new(path, hash, op.ToolName));
                if (files.Count >= 12) break;
            }
            catch (JsonException) { }
        }
        files.Reverse();
        return files;
    }

    private static bool True(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.True;

    // Called only from the user's Open button. No disk reads while rendering old history.
    public static async Task<bool> MatchesAsync(RecordedFileArtifact file, string workspace, CancellationToken ct = default)
    {
        string path = new WorkspacePathResolver(workspace).Resolve(file.Path);
        if (!IsOpenable(path) || !File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        string observed = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        return observed.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}

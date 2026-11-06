using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nira.V2.Runtime.AgentLoop;

/// <summary>
/// Durable, run-scoped opaque references for non-sensitive pilot arguments and results.
/// This is a development store only: JSON is NOT encrypted. Production must replace it
/// with a per-user protected store before accepting private user data or credential-bearing inputs.
/// </summary>
public sealed class AgentPayloadVault
{
    private const int MaxPayloadBytes = 48 * 1024;
    private static readonly Regex SafeToolId = new("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    private readonly string _root;

    public AgentPayloadVault(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public Task<string> SaveArgumentAsync(Guid runId, string toolId,
        JsonElement value, CancellationToken ct = default) => SaveAsync(runId, toolId, "arg", value, ct);

    public Task<string> SaveResultAsync(Guid runId, string toolId,
        JsonElement value, CancellationToken ct = default) => SaveAsync(runId, toolId, "res", value, ct);

    public Task<JsonElement> ReadArgumentAsync(Guid runId, string toolId,
        string reference, CancellationToken ct = default) => ReadAsync(runId, toolId, "arg", reference, ct);

    public Task<JsonElement> ReadResultAsync(Guid runId, string toolId,
        string reference, CancellationToken ct = default) => ReadAsync(runId, toolId, "res", reference, ct);

    // A tool does not receive the run id from the legacy interface. Recover it strictly
    // from the scoped token, then still validate it against the persisted envelope.
    public Task<JsonElement> ReadToolArgumentAsync(string toolId,
        string reference, CancellationToken ct = default)
    {
        var parsed = ParseReference(reference);
        if (parsed.Role != "arg") throw new InvalidDataException("Expected argument reference.");
        return ReadAsync(parsed.RunId, toolId, "arg", reference, ct);
    }

    public Guid RunIdFromArgument(string reference)
    {
        var parsed = ParseReference(reference);
        if (parsed.Role != "arg") throw new InvalidDataException("Expected argument reference.");
        return parsed.RunId;
    }

    private async Task<string> SaveAsync(Guid runId, string toolId, string role,
        JsonElement value, CancellationToken ct)
    {
        ValidateToolId(toolId);
        if (runId == Guid.Empty) throw new ArgumentException("Run id required.", nameof(runId));
        if (value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            throw new InvalidDataException("Payload must be a JSON object or array.");
        var id = Guid.NewGuid();
        string reference = $"av1.{role}.{runId:N}.{id:N}";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(runId, toolId, role, value));
        if (bytes.Length > MaxPayloadBytes) throw new InvalidDataException("Payload exceeds limit.");
        string dir = Path.Combine(_root, runId.ToString("N"), role);
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, id.ToString("N") + ".json");
        string temp = target + ".tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 8192, FileOptions.Asynchronous))
            {
                await file.WriteAsync(bytes, ct);
                await file.FlushAsync(ct);
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, target); // Never overwrite an existing reference.
            return reference;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private async Task<JsonElement> ReadAsync(Guid runId, string toolId, string role,
        string reference, CancellationToken ct)
    {
        ValidateToolId(toolId);
        var parsed = ParseReference(reference);
        if (parsed.RunId != runId || parsed.Role != role)
            throw new InvalidDataException("Reference is outside the authorized run or role.");
        string filename = Path.Combine(_root, runId.ToString("N"), role,
            parsed.PayloadId.ToString("N") + ".json");
        await using var file = new FileStream(filename, FileMode.Open, FileAccess.Read,
            FileShare.Read, 8192, FileOptions.Asynchronous);
        if (file.Length > MaxPayloadBytes) throw new InvalidDataException("Stored payload too large.");
        using var json = await JsonDocument.ParseAsync(file, cancellationToken: ct);
        var root = json.RootElement;
        if (root.GetProperty("RunId").GetGuid() != runId ||
            root.GetProperty("ToolId").GetString() != toolId ||
            root.GetProperty("Role").GetString() != role)
            throw new InvalidDataException("Stored payload scope mismatch.");
        return root.GetProperty("Value").Clone();
    }

    private static (string Role, Guid RunId, Guid PayloadId) ParseReference(string reference)
    {
        string[] fields = reference.Split('.');
        if (fields.Length != 4 || fields[0] != "av1" ||
            fields[1] is not ("arg" or "res") ||
            !Guid.TryParseExact(fields[2], "N", out Guid runId) || runId == Guid.Empty ||
            !Guid.TryParseExact(fields[3], "N", out Guid valueId) || valueId == Guid.Empty)
            throw new InvalidDataException("Invalid opaque payload reference.");
        return (fields[1], runId, valueId);
    }

    private static void ValidateToolId(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId) || !SafeToolId.IsMatch(toolId))
            throw new InvalidDataException("Invalid tool identity.");
    }

    private sealed record Envelope(Guid RunId, string ToolId, string Role, JsonElement Value);
}

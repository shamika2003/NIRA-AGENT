using System.Security.Cryptography;
using System.Text.Json;
using Nira.V2.Contracts;

namespace Nira.V2.Runtime.AgentLoop;

// When a protector is supplied, checkpoints are encrypted (no plaintext fallback).
// The legacy constructor without protection remains for existing public-only pilots.
public sealed class FileAgentStateStore : IAgentStateStore
{
    private readonly string _root;
    private readonly IAgentDataProtector? _protector;
    private const int MaxEncryptedCheckpointBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public FileAgentStateStore(string root, IAgentDataProtector? protector = null)
    {
        _protector = protector;
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    private string PathFor(Guid id) => Path.Combine(_root, id.ToString("N") + ".json");

    public ValueTask<IAsyncDisposable> AcquireLeaseAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // FileShare.None protects against concurrent runners, including another process.
        // OS releases the handle after crash; the lock file itself is harmless.
        var handle = new FileStream(PathFor(id) + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
        return ValueTask.FromResult<IAsyncDisposable>(handle);
    }

    public async Task<AgentRunSnapshot?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        string path = PathFor(id);
        if (!File.Exists(path)) return null;
        if (_protector is null)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 8192, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<AgentRunSnapshot>(stream, Options, cancellationToken)
                ?? throw new InvalidDataException("Empty agent checkpoint.");
        }
        var info = new FileInfo(path);
        if (info.Length > MaxEncryptedCheckpointBytes)
            throw new InvalidDataException("Encrypted checkpoint exceeds size limit.");
        byte[] encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        byte[] plain = _protector.Unprotect(encrypted, "checkpoint." + id.ToString("N"));
        try
        {
            AgentRunSnapshot state = JsonSerializer.Deserialize<AgentRunSnapshot>(plain, Options)
                ?? throw new InvalidDataException("Empty agent checkpoint.");
            if (state.Id != id || state.AgentIdentity != "nira")
                throw new InvalidDataException("Checkpoint identity mismatch.");
            return state;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public async Task SaveAsync(AgentRunSnapshot state, CancellationToken cancellationToken)
    {
        string target = PathFor(state.Id);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        byte[]? plain = null;
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 8192, FileOptions.Asynchronous))
            {
                if (_protector is null)
                    await JsonSerializer.SerializeAsync(stream, state, Options, cancellationToken);
                else
                {
                    plain = JsonSerializer.SerializeToUtf8Bytes(state, Options);
                    if (plain.Length > MaxEncryptedCheckpointBytes - 4096)
                        throw new InvalidDataException("Checkpoint exceeds private-state size limit.");
                    byte[] encrypted = _protector.Protect(plain,
                        "checkpoint." + state.Id.ToString("N"));
                    if (encrypted.Length > MaxEncryptedCheckpointBytes)
                        throw new InvalidDataException("Encrypted checkpoint exceeds size limit.");
                    await stream.WriteAsync(encrypted, cancellationToken);
                }
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

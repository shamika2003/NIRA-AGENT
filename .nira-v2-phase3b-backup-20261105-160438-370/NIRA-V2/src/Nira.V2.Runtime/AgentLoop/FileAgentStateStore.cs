using System.Text.Json;
using Nira.V2.Contracts;

namespace Nira.V2.Runtime.AgentLoop;

// LOCAL DEVELOPMENT STORE. No secrets or sensitive raw tool arguments belong here.
// Windows production needs an encrypted, versioned store and a secure input vault.
public sealed class FileAgentStateStore : IAgentStateStore
{
    private readonly string _root;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public FileAgentStateStore(string root)
    {
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
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 8192, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<AgentRunSnapshot>(stream, Options, cancellationToken)
            ?? throw new InvalidDataException("Empty agent checkpoint.");
    }

    public async Task SaveAsync(AgentRunSnapshot state, CancellationToken cancellationToken)
    {
        string target = PathFor(state.Id);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 8192, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, state, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, target, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

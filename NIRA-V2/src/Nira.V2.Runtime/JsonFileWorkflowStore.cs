using System.Text.Json;
using Nira.V2.Contracts;

namespace Nira.V2.Runtime;

// Local development checkpoint store. Production V2 will move to a transactional
// store with locking, secure-at-rest data and multi-process consistency.
public sealed class JsonFileWorkflowStore : IWorkflowStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public JsonFileWorkflowStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(WorkflowRun run, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        string target = Path.Combine(_root, run.Id.ToString("N") + ".json");
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string json = JsonSerializer.Serialize(run, JsonOptions);
            await File.WriteAllTextAsync(temp, json, cancellationToken);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            _mutex.Release();
        }
    }

    public async Task<WorkflowRun?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            string path = Path.Combine(_root, id.ToString("N") + ".json");
            if (!File.Exists(path)) return null;
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<WorkflowRun>(json, JsonOptions)
                ?? throw new InvalidDataException("Invalid saved workflow checkpoint.");
        }
        finally { _mutex.Release(); }
    }
}

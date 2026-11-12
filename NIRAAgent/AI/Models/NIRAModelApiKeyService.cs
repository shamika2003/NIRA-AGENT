using System.Diagnostics;
namespace NIRAAgent.AI.Models;

public enum NIRAModelCredentialProblem { Missing, InvalidOrRevoked, UsageUnavailable }
public enum NIRAModelCredentialResolutionKind { UseCurrent, ReplaceKey }
public sealed record NIRAModelCredentialRequest
{
    public required NIRAModelCredentialProblem Problem { get; init; }
    public string Detail { get; init; } = string.Empty;
    public bool HasCurrentKey { get; init; }
}
public sealed record NIRAModelCredentialResolution
{
    public required NIRAModelCredentialResolutionKind Kind { get; init; }
    public string ApiKey { get; init; } = string.Empty;
}
public sealed class NIRAModelCredentialRequiredException(string message) : InvalidOperationException(message) { }

/// <summary>
/// Active provider key, session-scoped. The model provider does not own NIRA memory or state.
/// Never reads retired provider secrets or stores keys in conversations or checkpoints.
/// </summary>
public sealed class NIRAModelApiKeyService : IDisposable
{
    public const string EnvironmentVariableName = NIRAActiveModelProvider.ProcessCredentialName;
    public const string ProviderEnvironmentVariableName = NIRAActiveModelProvider.AlternateCredentialName;
    private readonly SemaphoreSlim _replacementGate = new(1, 1);
    private readonly object _sync = new();
    private string _key;
    private Func<NIRAModelCredentialRequest,CancellationToken,
        Task<NIRAModelCredentialResolution?>>? _prompt;
    private bool _disposed;

    public NIRAModelApiKeyService()
    {
        _key = (Environment.GetEnvironmentVariable(EnvironmentVariableName,
            EnvironmentVariableTarget.Process) ??
            Environment.GetEnvironmentVariable(ProviderEnvironmentVariableName,
            EnvironmentVariableTarget.Process) ?? string.Empty).Trim();
    }

    public bool HasKey { get { lock (_sync) { return !string.IsNullOrWhiteSpace(_key); } } }
    public void SetPromptHandler(Func<NIRAModelCredentialRequest,CancellationToken,
        Task<NIRAModelCredentialResolution?>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_sync) { ThrowIfDisposed(); _prompt = handler; }
    }
    public string GetRequiredKey()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(_key))
                throw new NIRAModelCredentialRequiredException("Active model API key is not configured.");
            return _key;
        }
    }
    public void SaveKey(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        string key = apiKey.Trim();
        if (key.Length < 16 || key.Any(char.IsWhiteSpace))
            throw new ArgumentException("Invalid model API credential.",nameof(apiKey));
        lock (_sync) { ThrowIfDisposed(); _key=key; }
        Environment.SetEnvironmentVariable(EnvironmentVariableName,key,
            EnvironmentVariableTarget.Process);
    }
    public async Task<string> ResolveProblemAsync(NIRAModelCredentialProblem problem,
        string failedKey,string detail,CancellationToken cancellationToken)
    {
        await _replacementGate.WaitAsync(cancellationToken);
        try
        {
            string current;
            Func<NIRAModelCredentialRequest,CancellationToken,
                Task<NIRAModelCredentialResolution?>>? handler;
            lock (_sync) { ThrowIfDisposed(); current=_key; handler=_prompt; }
            if (!string.IsNullOrWhiteSpace(current) &&
                !string.Equals(current,failedKey,StringComparison.Ordinal)) return current;
            if (handler is null)
                throw new NIRAModelCredentialRequiredException("Credential needs attention; no UI prompt is attached.");
            var resolution=await handler(new NIRAModelCredentialRequest {
                Problem=problem,Detail=detail,HasCurrentKey=!string.IsNullOrWhiteSpace(current)},
                cancellationToken);
            if (resolution is null)
                throw new NIRAModelCredentialRequiredException("Credential replacement was cancelled.");
            if (resolution.Kind==NIRAModelCredentialResolutionKind.UseCurrent)
            {
                if (string.IsNullOrWhiteSpace(current))
                    throw new NIRAModelCredentialRequiredException("No active key is configured.");
                return current;
            }
            SaveKey(resolution.ApiKey);
            return GetRequiredKey();
        }
        finally { _replacementGate.Release(); }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed,this);
    public void Dispose() { if (_disposed)return;_disposed=true;_replacementGate.Dispose(); }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

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
/// One authoritative Ollama key for NIRA's vision model, chat model and agent.
/// Replacement is validated through Ollama Cloud BEFORE being made active.
/// Normal launches persist a DPAPI CurrentUser ciphertext, never plaintext.
/// Existing model prompts, agent run checkpoints, histories and logs never hold keys.
/// </summary>
public sealed class NIRAModelApiKeyService : IDisposable
{
    public const string EnvironmentVariableName = NIRAActiveModelProvider.ProcessCredentialName;
    public const string ProviderEnvironmentVariableName = NIRAActiveModelProvider.AlternateCredentialName;
    public const string SessionOnlyEnvironmentVariableName = "NIRA_MODEL_CREDENTIAL_SESSION_ONLY";
    public const string ProtectedFileName = "ollama-cloud-key.v2.dpapi";
    private const int MaxRecoveryPrompts = 3;
    private readonly SemaphoreSlim _replacementGate = new(1, 1);
    private readonly object _sync = new();
    private readonly bool _sessionOnly;
    private readonly string _protectedPath;
    private string _key;
    private Func<NIRAModelCredentialRequest, CancellationToken,
        Task<NIRAModelCredentialResolution?>>? _prompt;
    private bool _disposed;

    public NIRAModelApiKeyService()
    {
        _protectedPath = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "NIRA", "Secrets", ProtectedFileName);
        _sessionOnly = string.Equals(Environment.GetEnvironmentVariable(
            SessionOnlyEnvironmentVariableName, EnvironmentVariableTarget.Process),
            "1", StringComparison.Ordinal);
        string environmentKey = (Environment.GetEnvironmentVariable(EnvironmentVariableName,
            EnvironmentVariableTarget.Process) ??
            Environment.GetEnvironmentVariable(ProviderEnvironmentVariableName,
            EnvironmentVariableTarget.Process) ?? string.Empty).Trim();
        string? savedKey = null;
        if (!_sessionOnly && File.Exists(_protectedPath))
        {
            try { savedKey = ReadProtectedKey(_protectedPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                CryptographicException or InvalidDataException or ArgumentException)
            {
                // A damaged or other-Windows-user blob is not a valid credential.
                // Never log it or replace it until a key has been verified.
            }
        }
        _key = !string.IsNullOrWhiteSpace(savedKey) ? savedKey : environmentKey;
        if (!string.IsNullOrEmpty(savedKey))
            Environment.SetEnvironmentVariable(EnvironmentVariableName, savedKey,
                EnvironmentVariableTarget.Process);
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
        ValidateFormat(apiKey);
        string key = apiKey.Trim();
        lock (_sync)
        {
            ThrowIfDisposed();
            // Persistence comes BEFORE promotion. A write failure must not
            // silently turn a promised long-lived replacement into a session key.
            if (!_sessionOnly) WriteProtectedKey(_protectedPath, key);
            _key = key;
            Environment.SetEnvironmentVariable(EnvironmentVariableName, key,
                EnvironmentVariableTarget.Process);
        }
    }

    public async Task<string> ResolveProblemAsync(NIRAModelCredentialProblem problem,
        string failedKey, string detail, CancellationToken cancellationToken)
    {
        await _replacementGate.WaitAsync(cancellationToken);
        try
        {
            Func<NIRAModelCredentialRequest,CancellationToken,
                Task<NIRAModelCredentialResolution?>>? handler;
            string current;
            lock (_sync) { ThrowIfDisposed(); current = _key; handler = _prompt; }
            if (!string.IsNullOrWhiteSpace(current) &&
                !string.Equals(current, failedKey, StringComparison.Ordinal))
                return current; // Another concurrent recovery already changed the key.
            if (handler is null)
                throw new NIRAModelCredentialRequiredException("No credential prompt is attached.");
            string explanation = detail;
            for (int attempt = 0; attempt < MaxRecoveryPrompts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resolution = await handler(new NIRAModelCredentialRequest {
                    Problem = problem, Detail = explanation,
                    HasCurrentKey = !string.IsNullOrWhiteSpace(current) }, cancellationToken);
                if (resolution is null)
                    throw new NIRAModelCredentialRequiredException("Credential replacement was cancelled.");
                string candidate;
                if (resolution.Kind == NIRAModelCredentialResolutionKind.UseCurrent)
                {
                    if (string.IsNullOrWhiteSpace(current))
                        throw new NIRAModelCredentialRequiredException("No current credential to retry.");
                    candidate = current;
                }
                else
                {
                    candidate = resolution.ApiKey?.Trim() ?? string.Empty;
                    ValidateFormat(candidate);
                }
                // Do not persist a typed-but-rejected replacement. Validate
                // against the actual configured Ollama Cloud inference endpoint
                // before changing ANY active credential or its saved ciphertext.
                HttpStatusCode? status = await ProbeCloudKeyAsync(candidate, cancellationToken);
                if (status == HttpStatusCode.OK)
                {
                    if (!string.Equals(candidate, current, StringComparison.Ordinal)) SaveKey(candidate);
                    return candidate;
                }
                if (status == HttpStatusCode.Unauthorized)
                {
                    explanation = "Ollama rejected that replacement (HTTP 401). " +
                        "Please enter a different valid key. Nothing was saved.";
                    problem = NIRAModelCredentialProblem.InvalidOrRevoked;
                    continue;
                }
                // Usage/billing, provider permission errors and transport
                // failures are NOT proof that a replacement key was bad.
                string code = status.HasValue ? "HTTP " + (int)status.Value : "network unavailable";
                throw new NIRAModelCredentialRequiredException(
                    "Ollama credential validation could not complete (" + code +
                    "). The previous saved credential was preserved.");
            }
            throw new NIRAModelCredentialRequiredException(
                "Ollama rejected three credential replacements (HTTP 401). No new key was saved.");
        }
        finally { _replacementGate.Release(); }
    }

    private static async Task<HttpStatusCode?> ProbeCloudKeyAsync(string key, CancellationToken ct)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            NIRAActiveModelProvider.ChatEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(new {
            model = (Environment.GetEnvironmentVariable("NIRA_MODEL_NAME") is { Length: > 0 } model &&
                model.Length <= 160 ? model : NIRAActiveModelProvider.DefaultModel),
            messages = new[] { new { role = "user", content = "ping" } },
            max_tokens = 64,
            stream = false
        });
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, ct);
            return response.StatusCode;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
        catch (IOException) { return null; }
    }

    private static void ValidateFormat(string? apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        string key = apiKey.Trim();
        if (key.Length < 16 || key.Length > 1024 || key.Any(char.IsWhiteSpace))
            throw new ArgumentException("Invalid model API credential format.", nameof(apiKey));
    }
    private static void WriteProtectedKey(string path, string key)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(key);
        byte[] ciphertext;
        try { ciphertext = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        string directory = Path.GetDirectoryName(path) ??
            throw new InvalidOperationException("Credential folder missing.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "ollama-key-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // Ciphertext only. Never put the key in a project file, transcript,
            // source code, command line, or plaintext temp file.
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(ciphertext);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string ReadProtectedKey(string path)
    {
        byte[] blob = File.ReadAllBytes(path);
        if (blob.Length is < 16 or > 16_384)
            throw new InvalidDataException("Encrypted model key has an invalid length.");
        byte[] plaintext = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
        try
        {
            string key = new UTF8Encoding(false, true).GetString(plaintext);
            ValidateFormat(key);
            return key.Trim();
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static int RunPersistenceSelfCheck()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "nira-model-dpapi-smoke-" + Guid.NewGuid().ToString("N"), ProtectedFileName);
        const string first = "nira-test-key-not-a-real-provider-token-01";
        const string second = "nira-test-key-not-a-real-provider-token-02";
        try
        {
            WriteProtectedKey(path, first);
            if (ReadProtectedKey(path) != first) throw new InvalidOperationException("DPAPI roundtrip failed.");
            if (Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(first, StringComparison.Ordinal))
                throw new InvalidOperationException("Protected key leaked in storage.");
            WriteProtectedKey(path, second);
            if (ReadProtectedKey(path) != second) throw new InvalidOperationException("DPAPI rotation failed.");
            return 3;
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose() { if (_disposed) return; _disposed = true; _replacementGate.Dispose(); }
}

using System.Diagnostics;
using System.Security;
using System.Text;

namespace SegaAgent.AI.Ollama;

public enum SegaOllamaCredentialProblem
{
    Missing,
    InvalidOrRevoked,
    UsageUnavailable
}

public enum SegaOllamaCredentialResolutionKind
{
    UseCurrent,
    ReplaceKey
}

public sealed record SegaOllamaCredentialRequest
{
    public required SegaOllamaCredentialProblem Problem { get; init; }
    public string Detail { get; init; } = string.Empty;
    public bool HasCurrentKey { get; init; }
}

public sealed record SegaOllamaCredentialResolution
{
    public required SegaOllamaCredentialResolutionKind Kind { get; init; }
    public string ApiKey { get; init; } = string.Empty;
}

public sealed class SegaOllamaCredentialRequiredException : InvalidOperationException
{
    public SegaOllamaCredentialRequiredException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Owns Sega's direct Ollama cloud API credential.
///
/// The durable source is the Windows user environment variable:
///
///     SEGA_OLLAMA_API_KEY
///
/// This survives Sega and PC restarts. Existing OLLAMA_API_KEY values and the
/// older Sega .env locations are still accepted as compatibility/migration
/// sources. When one of those older sources is found, Sega best-effort migrates
/// the key into the durable Windows user environment automatically.
///
/// If user-environment persistence is unavailable, Sega falls back to:
///
///     %LOCALAPPDATA%\SegaAgent\.env
///
/// On startup any usable persisted key is loaded immediately, so the credential
/// window is only needed when no key exists (or when Ollama later rejects the
/// saved credential). Replacement keys become effective in the current process
/// immediately; no application restart is required.
///
/// UI is deliberately injected as a prompt callback. Core reasoning code does
/// not reference WPF and concurrent failures are coalesced into one prompt.
/// </summary>
public sealed class SegaOllamaApiKeyService : IDisposable
{
    public const string EnvironmentVariableName = "SEGA_OLLAMA_API_KEY";
    public const string OllamaEnvironmentFallback = "OLLAMA_API_KEY";

    private const string EnvFileName = ".env";

    private readonly object _sync = new();
    private readonly SemaphoreSlim _replacementGate = new(1, 1);
    private readonly string _envFilePath;
    private readonly string? _legacyProjectEnvFilePath;

    private string _currentKey;
    private Func<SegaOllamaCredentialRequest, CancellationToken,
        Task<SegaOllamaCredentialResolution?>>? _promptHandler;
    private bool _disposed;

    public SegaOllamaApiKeyService()
    {
        _envFilePath =
            ResolveStableEnvFilePath();

        _legacyProjectEnvFilePath =
            ResolveLegacyProjectEnvFilePath();

        _currentKey =
            ReadInitialKey(
                _envFilePath,
                _legacyProjectEnvFilePath,
                out string source);

        if (!string.IsNullOrWhiteSpace(_currentKey))
        {
            // Make the persisted key available to this running process.
            Environment.SetEnvironmentVariable(
                EnvironmentVariableName,
                _currentKey,
                EnvironmentVariableTarget.Process);

            // Migrate any legacy/process/fallback source into the durable
            // Windows user environment. This is deliberately best-effort:
            // an already usable fallback key must not force a new prompt just
            // because user-environment persistence is unavailable.
            if (!string.Equals(
                    source,
                    "UserEnvironment",
                    StringComparison.Ordinal))
            {
                TryPersistKeyToUserEnvironment(
                    _currentKey,
                    out _);
            }
        }

        Debug.WriteLine(
            $"[OllamaCredential] READY | " +
            $"KeyAvailable={!string.IsNullOrWhiteSpace(_currentKey)} | " +
            $"Source='{source}' | " +
            $"FallbackEnvFile='{_envFilePath}'");
    }

    public bool HasKey
    {
        get
        {
            lock (_sync)
            {
                return !string.IsNullOrWhiteSpace(_currentKey);
            }
        }
    }

    public string EnvFilePath => _envFilePath;

    public void SetPromptHandler(
        Func<SegaOllamaCredentialRequest, CancellationToken,
            Task<SegaOllamaCredentialResolution?>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
        {
            ThrowIfDisposed();
            _promptHandler = handler;
        }
    }

    public string GetRequiredKey()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(_currentKey))
            {
                throw new SegaOllamaCredentialRequiredException(
                    "Ollama API key is not configured.");
            }

            return _currentKey;
        }
    }

    public void SaveKey(string apiKey)
    {
        string normalized = NormalizeKey(apiKey);

        // Primary durable storage: Windows per-user environment. It survives
        // Sega/PC restarts and does not depend on the application's working
        // directory or build/publish location.
        bool persistedToUserEnvironment =
            TryPersistKeyToUserEnvironment(
                normalized,
                out string? userEnvironmentError);

        if (!persistedToUserEnvironment)
        {
            // Reliability fallback for restricted Windows environments. This
            // path is stable and never lives inside the source/project root.
            PersistKeyToEnvFile(
                _envFilePath,
                normalized);
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            _currentKey = normalized;
        }

        // Make the replacement effective in this running Sega process.
        Environment.SetEnvironmentVariable(
            EnvironmentVariableName,
            normalized,
            EnvironmentVariableTarget.Process);

        Debug.WriteLine(
            persistedToUserEnvironment
                ? "[OllamaCredential] UPDATED | Persistence='UserEnvironment'"
                : $"[OllamaCredential] UPDATED | Persistence='LocalAppDataEnv' | " +
                  $"Reason='{userEnvironmentError}' | EnvFile='{_envFilePath}'");
    }

    public async Task<string> ResolveProblemAsync(
        SegaOllamaCredentialProblem problem,
        string failedKey,
        string detail,
        CancellationToken cancellationToken)
    {
        await _replacementGate.WaitAsync(cancellationToken);

        try
        {
            string current;
            Func<SegaOllamaCredentialRequest, CancellationToken,
                Task<SegaOllamaCredentialResolution?>>? handler;

            lock (_sync)
            {
                ThrowIfDisposed();
                current = _currentKey;
                handler = _promptHandler;
            }

            // Another concurrent failed request may already have caused the
            // user to replace the key while this request was waiting.
            if (!string.IsNullOrWhiteSpace(current) &&
                !string.Equals(current, failedKey, StringComparison.Ordinal))
            {
                return current;
            }

            if (handler == null)
            {
                throw new SegaOllamaCredentialRequiredException(
                    "Ollama needs a new API key, but no credential UI is attached.");
            }

            SegaOllamaCredentialResolution? resolution =
                await handler(
                    new SegaOllamaCredentialRequest
                    {
                        Problem = problem,
                        Detail = detail,
                        HasCurrentKey = !string.IsNullOrWhiteSpace(current)
                    },
                    cancellationToken);

            if (resolution == null)
            {
                throw new SegaOllamaCredentialRequiredException(
                    "Ollama API key replacement was cancelled.");
            }

            if (resolution.Kind == SegaOllamaCredentialResolutionKind.UseCurrent)
            {
                if (string.IsNullOrWhiteSpace(current))
                {
                    throw new SegaOllamaCredentialRequiredException(
                        "There is no current Ollama API key to retry.");
                }

                return current;
            }

            SaveKey(resolution.ApiKey);
            return GetRequiredKey();
        }
        finally
        {
            _replacementGate.Release();
        }
    }

    private static string ReadInitialKey(
        string stableEnvFilePath,
        string? legacyProjectEnvFilePath,
        out string source)
    {
        string? value;

        value =
            ReadKeyFromEnvironment(
                EnvironmentVariableTarget.User,
                EnvironmentVariableName);

        if (TryNormalizePersistedKey(
                value,
                "UserEnvironment",
                out string normalized))
        {
            source = "UserEnvironment";
            return normalized;
        }

        value =
            ReadKeyFromEnvironment(
                EnvironmentVariableTarget.User,
                OllamaEnvironmentFallback);

        if (TryNormalizePersistedKey(
                value,
                "UserEnvironmentAlias",
                out normalized))
        {
            source = "UserEnvironmentAlias";
            return normalized;
        }

        // Explicit process lookup matters during development because a running
        // IDE/terminal can have a credential even when its inherited user
        // environment snapshot is stale.
        value =
            ReadKeyFromEnvironment(
                EnvironmentVariableTarget.Process,
                EnvironmentVariableName);

        if (TryNormalizePersistedKey(
                value,
                "ProcessEnvironment",
                out normalized))
        {
            source = "ProcessEnvironment";
            return normalized;
        }

        value =
            ReadKeyFromEnvironment(
                EnvironmentVariableTarget.Process,
                OllamaEnvironmentFallback);

        if (TryNormalizePersistedKey(
                value,
                "ProcessEnvironmentAlias",
                out normalized))
        {
            source = "ProcessEnvironmentAlias";
            return normalized;
        }

        value =
            ReadKeyFromEnvFile(
                stableEnvFilePath,
                EnvironmentVariableName);

        if (string.IsNullOrWhiteSpace(value))
        {
            value =
                ReadKeyFromEnvFile(
                    stableEnvFilePath,
                    OllamaEnvironmentFallback);
        }

        if (TryNormalizePersistedKey(
                value,
                "LocalAppDataEnv",
                out normalized))
        {
            source = "LocalAppDataEnv";
            return normalized;
        }

        if (!string.IsNullOrWhiteSpace(legacyProjectEnvFilePath))
        {
            value =
                ReadKeyFromEnvFile(
                    legacyProjectEnvFilePath,
                    EnvironmentVariableName);

            if (string.IsNullOrWhiteSpace(value))
            {
                value =
                    ReadKeyFromEnvFile(
                        legacyProjectEnvFilePath,
                        OllamaEnvironmentFallback);
            }

            if (TryNormalizePersistedKey(
                    value,
                    "LegacyProjectEnv",
                    out normalized))
            {
                source = "LegacyProjectEnv";
                return normalized;
            }
        }

        source = "None";
        return string.Empty;
    }

    private static string? ReadKeyFromEnvironment(
        EnvironmentVariableTarget target,
        string variableName)
    {
        try
        {
            return Environment.GetEnvironmentVariable(
                variableName,
                target);
        }
        catch (SecurityException ex)
        {
            Debug.WriteLine(
                $"[OllamaCredential] ENVIRONMENT_READ_DENIED | " +
                $"Target='{target}' | Name='{variableName}' | {ex.Message}");

            return null;
        }
        catch (PlatformNotSupportedException ex)
        {
            Debug.WriteLine(
                $"[OllamaCredential] ENVIRONMENT_READ_UNSUPPORTED | " +
                $"Target='{target}' | Name='{variableName}' | {ex.Message}");

            return null;
        }
    }

    private static bool TryNormalizePersistedKey(
        string? value,
        string source,
        out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim();

        if (!LooksUsable(candidate))
        {
            Debug.WriteLine(
                $"[OllamaCredential] IGNORE_INVALID_PERSISTED_KEY | " +
                $"Source='{source}'");

            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool TryPersistKeyToUserEnvironment(
        string apiKey,
        out string? error)
    {
        error = null;

        try
        {
            Environment.SetEnvironmentVariable(
                EnvironmentVariableName,
                apiKey,
                EnvironmentVariableTarget.User);

            return true;
        }
        catch (SecurityException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (PlatformNotSupportedException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? ReadKeyFromEnvFile(
        string envFilePath,
        string requestedName)
    {
        if (!File.Exists(envFilePath))
        {
            return null;
        }

        try
        {
            foreach (string rawLine in
                     File.ReadLines(envFilePath))
            {
                if (!TryParseEnvAssignment(
                        rawLine,
                        out string? name,
                        out string? value))
                {
                    continue;
                }

                if (string.Equals(
                        name,
                        requestedName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
        }
        catch (IOException ex)
        {
            Debug.WriteLine(
                $"[OllamaCredential] ENV_READ_FAILED | " +
                $"File='{envFilePath}' | {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine(
                $"[OllamaCredential] ENV_READ_DENIED | " +
                $"File='{envFilePath}' | {ex.Message}");
        }

        return null;
    }

    private static void PersistKeyToEnvFile(
        string envFilePath,
        string apiKey)
    {
        string? directory =
            Path.GetDirectoryName(
                envFilePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        List<string> output =
            new();

        bool wroteCredential =
            false;

        if (File.Exists(envFilePath))
        {
            foreach (string rawLine in
                     File.ReadAllLines(envFilePath))
            {
                if (TryParseEnvAssignment(
                        rawLine,
                        out string? name,
                        out _)
                    &&
                    (
                        string.Equals(
                            name,
                            EnvironmentVariableName,
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        string.Equals(
                            name,
                            OllamaEnvironmentFallback,
                            StringComparison.OrdinalIgnoreCase)
                    ))
                {
                    if (!wroteCredential)
                    {
                        output.Add(
                            $"{EnvironmentVariableName}={apiKey}");

                        wroteCredential =
                            true;
                    }

                    // Drop duplicate/legacy Ollama key assignments.
                    continue;
                }

                output.Add(
                    rawLine);
            }
        }

        if (!wroteCredential)
        {
            if (output.Count > 0 &&
                !string.IsNullOrWhiteSpace(output[^1]))
            {
                output.Add(
                    string.Empty);
            }

            output.Add(
                $"{EnvironmentVariableName}={apiKey}");
        }

        string temporaryPath =
            envFilePath +
            ".tmp." +
            Guid.NewGuid().ToString("N");

        try
        {
            File.WriteAllLines(
                temporaryPath,
                output,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            File.Move(
                temporaryPath,
                envFilePath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(
                        temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }
    }

    private static bool TryParseEnvAssignment(
        string rawLine,
        out string? name,
        out string? value)
    {
        name =
            null;

        value =
            null;

        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return false;
        }

        string line =
            rawLine.Trim();

        if (line.StartsWith(
                "#",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (line.StartsWith(
                "export ",
                StringComparison.OrdinalIgnoreCase))
        {
            line =
                line[
                    "export ".Length..]
                .TrimStart();
        }

        int separator =
            line.IndexOf('=');

        if (separator <=
            0)
        {
            return false;
        }

        string parsedName =
            line[..separator]
                .Trim();

        if (string.IsNullOrWhiteSpace(parsedName))
        {
            return false;
        }

        string parsedValue =
            line[(separator + 1)..]
                .Trim();

        if (parsedValue.Length >=
            2)
        {
            char first =
                parsedValue[0];

            char last =
                parsedValue[^1];

            if (
                (first == '"' && last == '"')
                ||
                (first == '\'' && last == '\'')
            )
            {
                parsedValue =
                    parsedValue[1..^1];
            }
        }

        name =
            parsedName;

        value =
            parsedValue;

        return true;
    }

    private static string ResolveStableEnvFilePath()
    {
        string localApplicationData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            localApplicationData =
                AppContext.BaseDirectory;
        }

        return Path.Combine(
            localApplicationData,
            "SegaAgent",
            EnvFileName);
    }

    private static string? ResolveLegacyProjectEnvFilePath()
    {
        string? projectRoot =
            FindProjectRoot(
                Environment.CurrentDirectory);

        projectRoot ??=
            FindProjectRoot(
                AppContext.BaseDirectory);

        return string.IsNullOrWhiteSpace(projectRoot)
            ? null
            : Path.Combine(
                projectRoot,
                EnvFileName);
    }

    private static string? FindProjectRoot(
        string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        DirectoryInfo? directory;

        try
        {
            directory =
                new DirectoryInfo(
                    Path.GetFullPath(
                        startPath));
        }
        catch
        {
            return null;
        }

        for (
            int depth = 0;
            directory != null &&
            depth < 16;
            depth++,
            directory = directory.Parent)
        {
            if (
                File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "SegaAgent.slnx"))
                ||
                File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "SegaAgent.sln"))
            )
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static bool LooksUsable(
        string apiKey)
    {
        return
            apiKey.Length >=
                16
            &&
            !apiKey.Any(
                char.IsWhiteSpace);
    }

    private static string NormalizeKey(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        string normalized = apiKey.Trim();

        if (!LooksUsable(normalized))
        {
            throw new ArgumentException(
                "The Ollama API key does not look valid.",
                nameof(apiKey));
        }

        return normalized;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _replacementGate.Dispose();
    }
}
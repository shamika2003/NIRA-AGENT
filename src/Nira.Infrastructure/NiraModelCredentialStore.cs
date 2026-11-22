using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Nira.Infrastructure;

/// <summary>
/// Stores a provider credential with Windows DPAPI, scoped to this Windows user
/// and to the configured endpoint. No key is written to source files or logs.
/// A stored key takes precedence over a possibly stale process environment key.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NiraModelCredentialStore
{
    private readonly string _directory;
    private readonly string _file;
    private readonly byte[] _entropy;

    public NiraModelCredentialStore(string applicationDataRoot, Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri)
            throw new ArgumentException("Model endpoint must be an absolute URL.", nameof(endpoint));
        string identity = endpoint.GetLeftPart(UriPartial.Path).TrimEnd('/');
        string identifier = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
        _directory = Path.Combine(Path.GetFullPath(applicationDataRoot), "Credentials");
        _file = Path.Combine(_directory, identifier + ".dpapi");
        _entropy = Encoding.UTF8.GetBytes("ELVARA:NIRA:MODEL-CREDENTIAL:V1:" + identifier);
    }

    public string? Resolve()
    {
        string? stored = ReadStored();
        return !string.IsNullOrWhiteSpace(stored)
            ? stored : Environment.GetEnvironmentVariable("NIRA_API_KEY")?.Trim();
    }

    public string? ReadStored()
    {
        if (!File.Exists(_file)) return null;
        AssertNoReparsePoints();
        var info = new FileInfo(_file);
        if (info.Length is < 32 or > 16384)
            return null; // Invalid/corrupted record; the user can replace it.
        byte[] encrypted = File.ReadAllBytes(_file);
        byte[] clear;
        try { clear = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser); }
        catch (CryptographicException) { return null; }
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API key cannot be blank.", nameof(apiKey));
        string key = apiKey.Trim();
        if (key.Length > 8192) throw new ArgumentException("API key length exceeds the supported limit.", nameof(apiKey));
        Directory.CreateDirectory(_directory);
        AssertNoReparsePoints();
        byte[] clear = Encoding.UTF8.GetBytes(key);
        byte[] protectedBytes;
        try { protectedBytes = ProtectedData.Protect(clear, _entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        string temporary = Path.Combine(_directory, ".nira-credential-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(protectedBytes);
                stream.Flush(flushToDisk: true);
            }
            // Replaces a previous encrypted record atomically on the same volume.
            File.Move(temporary, _file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void ClearStored()
    {
        if (!File.Exists(_file)) return;
        AssertNoReparsePoints();
        File.Delete(_file);
    }

    private void AssertNoReparsePoints()
    {
        for (string? path = _file; path is not null; path = Path.GetDirectoryName(path))
        {
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Credential store path contains a junction or symbolic link.");
        }
    }
}

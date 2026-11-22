using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Nira.Infrastructure;

/// <summary>
/// Encrypts conversation and explicitly saved memory text under the current
/// Windows user via DPAPI. Another Windows account cannot decrypt the database.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsDpapiProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ELVARA:NIRA:CLEAN:2026:DATA:v1");

    public static string Seal(string plaintext)
    {
        byte[] clear = Encoding.UTF8.GetBytes(plaintext);
        try { return Convert.ToBase64String(ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public static string Open(string ciphertext)
    {
        byte[] encoded = Convert.FromBase64String(ciphertext);
        byte[] clear = ProtectedData.Unprotect(encoded, Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}

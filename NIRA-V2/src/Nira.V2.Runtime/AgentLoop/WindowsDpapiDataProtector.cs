using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Nira.V2.Runtime.AgentLoop;

/// <summary>
/// Protection boundary for persisted checkpoints and run-scoped payloads.
/// Scope is a host-created purpose/run/tool binding; never model-generated.
/// </summary>
public interface IAgentDataProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext, string scope);
    byte[] Unprotect(ReadOnlySpan<byte> protectedBytes, string scope);
}

/// <summary>
/// Windows CurrentUser DPAPI, with a versioned binary envelope and scoped entropy.
/// The Windows user profile / DPAPI master keys are required for restoration.
/// There is no plaintext fallback, exportable application key or network call.
/// </summary>
public sealed class WindowsDpapiDataProtector : IAgentDataProtector
{
    private static readonly byte[] Magic = { (byte)'N', (byte)'V', (byte)'2', (byte)'D', 1 };
    private const int UiForbidden = 0x1;

    public WindowsDpapiDataProtector()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows DPAPI requires Windows.");
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext, string scope)
    {
        ValidateScope(scope);
        byte[] ciphertext = Transform(plaintext, scope, encrypt: true);
        byte[] envelope = new byte[Magic.Length + ciphertext.Length];
        Magic.CopyTo(envelope, 0);
        ciphertext.CopyTo(envelope, Magic.Length);
        CryptographicOperations.ZeroMemory(ciphertext);
        return envelope;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes, string scope)
    {
        ValidateScope(scope);
        if (protectedBytes.Length <= Magic.Length ||
            !protectedBytes[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("Unknown or unencrypted private-state envelope.");
        return Transform(protectedBytes[Magic.Length..], scope, encrypt: false);
    }

    private static byte[] Transform(ReadOnlySpan<byte> value, string scope, bool encrypt)
    {
        // Purpose separation is explicit, and DPAPI supplies the actual secret key.
        byte[] entropy = SHA256.HashData(Encoding.UTF8.GetBytes("NIRA-V2|DPAPI|v1|" + scope));
        byte[] input = value.ToArray();
        IntPtr description = IntPtr.Zero;
        DataBlob nativeIn = default;
        DataBlob nativeEntropy = default;
        DataBlob nativeOut = default;
        try
        {
            nativeIn = Allocate(input);
            nativeEntropy = Allocate(entropy);
            bool ok = encrypt
                ? CryptProtectData(ref nativeIn, "NIRA V2 private state", ref nativeEntropy,
                    IntPtr.Zero, IntPtr.Zero, UiForbidden, out nativeOut)
                : CryptUnprotectData(ref nativeIn, out description, ref nativeEntropy,
                    IntPtr.Zero, IntPtr.Zero, UiForbidden, out nativeOut);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            if (nativeOut.Length < 0 || nativeOut.Length > 16 * 1024 * 1024)
                throw new InvalidDataException("DPAPI output size invalid.");
            byte[] result = new byte[nativeOut.Length];
            if (result.Length > 0) Marshal.Copy(nativeOut.Pointer, result, 0, result.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(entropy);
            FreeAllocated(ref nativeIn);
            FreeAllocated(ref nativeEntropy);
            if (nativeOut.Pointer != IntPtr.Zero)
            {
                _ = LocalFree(nativeOut.Pointer);
                nativeOut = default;
            }
            if (description != IntPtr.Zero) _ = LocalFree(description);
        }
    }

    private static void ValidateScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 256 ||
            scope.Any(c => char.IsControl(c)))
            throw new ArgumentException("Invalid private-state scope.", nameof(scope));
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        IntPtr pointer = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        if (bytes.Length > 0) Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Length = bytes.Length, Pointer = pointer };
    }

    private static void FreeAllocated(ref DataBlob blob)
    {
        if (blob.Pointer != IntPtr.Zero)
        {
            if (blob.Length > 0)
            {
                byte[] zeros = new byte[blob.Length];
                Marshal.Copy(zeros, 0, blob.Pointer, zeros.Length);
            }
            Marshal.FreeHGlobal(blob.Pointer);
            blob = default;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Pointer;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description,
        ref DataBlob optionalEntropy, IntPtr reserved, IntPtr prompt,
        int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, out IntPtr description,
        ref DataBlob optionalEntropy, IntPtr reserved, IntPtr prompt,
        int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

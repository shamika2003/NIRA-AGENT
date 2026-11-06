using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Runtime.AgentLoop;

return await PrivateStateTests.RunAsync();

internal static class PrivateStateTests
{
    private static int _passed;
    public static async Task<int> RunAsync()
    {
        await CheckAsync("protected checkpoint round trip and restart", ProtectedCheckpointRoundTripAsync);
        await CheckAsync("plaintext request absent from encrypted checkpoint", CheckpointNoPlaintextAsync);
        await CheckAsync("checkpoint ciphertext corruption is rejected", CheckpointTamperAsync);
        await CheckAsync("checkpoint identity replay is rejected", CheckpointScopeAsync);
        await CheckAsync("plaintext checkpoint not silently accepted in private mode", NoCheckpointFallbackAsync);
        await CheckAsync("protected scoped argument survives vault restart", ArgumentRoundTripAsync);
        await CheckAsync("protected result survives vault restart", ResultRoundTripAsync);
        await CheckAsync("protected payload does not reveal source text", VaultNoPlaintextAsync);
        await CheckAsync("cross-tool argument scope is rejected", CrossToolAsync);
        await CheckAsync("cross-run argument scope is rejected", CrossRunAsync);
        await CheckAsync("encrypted argument cannot be mistaken for result", WrongRoleAsync);
        await CheckAsync("vault ciphertext corruption is rejected", VaultTamperAsync);
        await CheckAsync("unprotected development stores remain compatible", LegacyCompatibilityAsync);
        await CheckAsync("Windows DPAPI genuinely seals bytes and binds scope", WindowsDpapiAsync);
        Console.WriteLine("RESULT: " + _passed + " protected-state checks passed; 0 failed.");
        return 0;
    }

    private static async Task CheckAsync(string name, Func<Task> verify)
    {
        await verify();
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    private static string TempRoot() => Path.Combine(Path.GetTempPath(),
        "nira-v2-private-test-" + Guid.NewGuid().ToString("N"));

    private static async Task InTempAsync(Func<string, Task> action)
    {
        string dir = TempRoot();
        try { await action(dir); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    private static AgentRunSnapshot Snapshot(Guid id, string prompt) =>
        new() { Id = id, UserRequest = prompt };

    private static async Task ProtectedCheckpointRoundTripAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid id = Guid.NewGuid();
        var store = new FileAgentStateStore(dir, p);
        await store.SaveAsync(Snapshot(id, "private prompt"), CancellationToken.None);
        var restored = await new FileAgentStateStore(dir, p).LoadAsync(id, CancellationToken.None);
        Check(restored?.UserRequest == "private prompt", "State must survive a new store instance.");
    });

    private static async Task CheckpointNoPlaintextAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid id = Guid.NewGuid();
        await new FileAgentStateStore(dir, p).SaveAsync(Snapshot(id, "private secret check"), default);
        byte[] blob = await File.ReadAllBytesAsync(Path.Combine(dir, id.ToString("N") + ".json"));
        Check(!Encoding.UTF8.GetString(blob).Contains("private secret check", StringComparison.Ordinal),
            "Encrypted bytes must not contain plaintext prompt.");
    });

    private static async Task CheckpointTamperAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid id = Guid.NewGuid();
        var store = new FileAgentStateStore(dir, p);
        await store.SaveAsync(Snapshot(id, "tamper test"), default);
        string path = Path.Combine(dir, id.ToString("N") + ".json");
        byte[] bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(path, bytes);
        await ThrowsAsync<CryptographicException>(async () =>
        { _ = await store.LoadAsync(id, default); });
    });

    private static async Task CheckpointScopeAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var store = new FileAgentStateStore(dir, p);
        await store.SaveAsync(Snapshot(a, "run A"), default);
        File.Copy(Path.Combine(dir, a.ToString("N") + ".json"),
            Path.Combine(dir, b.ToString("N") + ".json"));
        await ThrowsAsync<CryptographicException>(async () =>
        { _ = await store.LoadAsync(b, default); });
    });

    private static async Task NoCheckpointFallbackAsync() => await InTempAsync(async dir =>
    {
        Guid id = Guid.NewGuid();
        await new FileAgentStateStore(dir).SaveAsync(Snapshot(id, "plaintext"), default);
        using var p = new TestAesProtector();
        await ThrowsAsync<CryptographicException>(async () =>
        { _ = await new FileAgentStateStore(dir, p).LoadAsync(id, default); });
    });

    private static async Task ArgumentRoundTripAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid run = Guid.NewGuid();
        var vault = new AgentPayloadVault(dir, p);
        string token = await vault.SaveArgumentAsync(run, "math.calculate",
            JsonSerializer.SerializeToElement(new { expression = "9*7" }));
        JsonElement restored = await new AgentPayloadVault(dir, p)
            .ReadToolArgumentAsync("math.calculate", token);
        Check(restored.GetProperty("expression").GetString() == "9*7", "Argument mismatch.");
    });

    private static async Task ResultRoundTripAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid run = Guid.NewGuid();
        var vault = new AgentPayloadVault(dir, p);
        string token = await vault.SaveResultAsync(run, "system.platform",
            JsonSerializer.SerializeToElement(new { platform = "Windows" }));
        JsonElement restored = await new AgentPayloadVault(dir, p)
            .ReadResultAsync(run, "system.platform", token);
        Check(restored.GetProperty("platform").GetString() == "Windows", "Result mismatch.");
    });

    private static async Task VaultNoPlaintextAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid run = Guid.NewGuid();
        var vault = new AgentPayloadVault(dir, p);
        string token = await vault.SaveResultAsync(run, "web.search",
            JsonSerializer.SerializeToElement(new { text = "private source sample" }));
        string id = token.Split('.')[3];
        byte[] disk = await File.ReadAllBytesAsync(Path.Combine(dir, run.ToString("N"), "res", id + ".json"));
        Check(!Encoding.UTF8.GetString(disk).Contains("private source sample", StringComparison.Ordinal),
            "Vault file exposed source text.");
    });

    private static async Task CrossToolAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid run = Guid.NewGuid();
        var vault = new AgentPayloadVault(dir, p);
        string token = await vault.SaveArgumentAsync(run, "math.calculate",
            JsonSerializer.SerializeToElement(new { expression = "1+1" }));
        await ThrowsAsync<CryptographicException>(async () =>
        { _ = await vault.ReadToolArgumentAsync("system.platform", token); });
    });

    private static async Task CrossRunAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var vault = new AgentPayloadVault(dir, p);
        string token = await vault.SaveArgumentAsync(a, "math.calculate",
            JsonSerializer.SerializeToElement(new { expression = "5+6" }));
        await ThrowsAsync<InvalidDataException>(async () =>
        { _ = await vault.ReadArgumentAsync(b, "math.calculate", token); });
    });

    private static async Task WrongRoleAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid run = Guid.NewGuid();
        var vault = new AgentPayloadVault(dir, p);
        string token = await vault.SaveArgumentAsync(run, "math.calculate",
            JsonSerializer.SerializeToElement(new { expression = "5+6" }));
        await ThrowsAsync<InvalidDataException>(async () =>
        { _ = await vault.ReadResultAsync(run, "math.calculate", token); });
    });

    private static async Task VaultTamperAsync() => await InTempAsync(async dir =>
    {
        using var p = new TestAesProtector();
        Guid run = Guid.NewGuid();
        var vault = new AgentPayloadVault(dir, p);
        string token = await vault.SaveArgumentAsync(run, "math.calculate",
            JsonSerializer.SerializeToElement(new { expression = "5+6" }));
        string path = Path.Combine(dir, run.ToString("N"), "arg", token.Split('.')[3] + ".json");
        byte[] bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(path, bytes);
        await ThrowsAsync<CryptographicException>(async () =>
        { _ = await vault.ReadToolArgumentAsync("math.calculate", token); });
    });

    private static async Task LegacyCompatibilityAsync() => await InTempAsync(async dir =>
    {
        var vault = new AgentPayloadVault(Path.Combine(dir, "vault"));
        Guid run = Guid.NewGuid();
        string token = await vault.SaveArgumentAsync(run, "math.calculate",
            JsonSerializer.SerializeToElement(new { expression = "8*8" }));
        JsonElement args = await new AgentPayloadVault(Path.Combine(dir, "vault"))
            .ReadArgumentAsync(run, "math.calculate", token);
        Check(args.GetProperty("expression").GetString() == "8*8", "Dev vault broke.");
        var state = new FileAgentStateStore(Path.Combine(dir, "runs"));
        await state.SaveAsync(Snapshot(run, "non-sensitive"), default);
        Check((await state.LoadAsync(run, default))?.UserRequest == "non-sensitive", "Dev checkpoint broke.");
    });

    private static Task WindowsDpapiAsync()
    {
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
        var protection = new WindowsDpapiDataProtector();
        byte[] encrypted = protection.Protect(Encoding.UTF8.GetBytes("DPAPI private test"), "test.one");
        Check(!Encoding.UTF8.GetString(encrypted).Contains("DPAPI private test", StringComparison.Ordinal),
            "DPAPI output contains plaintext.");
        byte[] result = protection.Unprotect(encrypted, "test.one");
        Check(Encoding.UTF8.GetString(result) == "DPAPI private test", "DPAPI round trip mismatch.");
        Check(encrypted.AsSpan(0, 5).SequenceEqual(new byte[] { 78, 86, 50, 68, 1 }),
            "Unexpected encrypted envelope.");
        bool rejected = false;
        try { _ = protection.Unprotect(encrypted, "test.two"); }
        catch (CryptographicException) { rejected = true; }
        Check(rejected, "DPAPI scope mismatch must fail.");
        return Task.CompletedTask;
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected exception " + typeof(T).Name);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    /// <summary>Test-only AEAD to prove injectable encryption and scope semantics on all OSes.</summary>
    private sealed class TestAesProtector : IAgentDataProtector, IDisposable
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public byte[] Protect(ReadOnlySpan<byte> plaintext, string scope)
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] output = new byte[12 + plaintext.Length + 16];
            nonce.CopyTo(output, 0);
            using var aes = new AesGcm(_key, 16);
            aes.Encrypt(nonce, plaintext, output.AsSpan(12, plaintext.Length),
                output.AsSpan(12 + plaintext.Length, 16), Encoding.UTF8.GetBytes(scope));
            return output;
        }
        public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes, string scope)
        {
            if (protectedBytes.Length < 29) throw new CryptographicException("Not an AEAD record.");
            byte[] plain = new byte[protectedBytes.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(protectedBytes[..12], protectedBytes.Slice(12, plain.Length),
                protectedBytes.Slice(12 + plain.Length, 16), plain, Encoding.UTF8.GetBytes(scope));
            return plain;
        }
        public void Dispose() => CryptographicOperations.ZeroMemory(_key);
    }
}

using System.Text.Json;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.Filesystem;
using Nira.V2.General;
using Nira.V2.LegacyAdapters;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new InvalidOperationException("FAIL " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

string temp = Path.Combine(Path.GetTempPath(), "nira-v2-filehash-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    string documents = Path.Combine(temp, "Documents");
    string folder = Path.Combine(documents, "TestFolder");
    Directory.CreateDirectory(folder);
    string file = Path.Combine(folder, "sample.txt");
    await File.WriteAllTextAsync(file, "NIRA SHA256 test\n");
    long bytes = new FileInfo(file).Length;
    string actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(file))).ToLowerInvariant();
    Func<string, string?> resolver = alias => alias.Equals("Documents", StringComparison.OrdinalIgnoreCase) ? documents : null;
    var vault = new AgentPayloadVault(Path.Combine(temp, "vault"));
    var fake = new FakeHashExecutor(file, bytes, actualHash);
    string ask = "Compute the SHA-256 of Documents/TestFolder/sample.txt. Use an observed tool result.";
    const string missing = "Check the SHA-256 of Documents/TestFolder/sample.txt";
    string consent = LegacyFileHashRegistration.ConsentValue;

    Check("independent owner opt-in required", LegacyFileHashRegistration.Create(vault, fake, null, ask,
        rootResolver: resolver).Count == 0);
    Check("case-changed consent rejected", LegacyFileHashRegistration.Create(vault, fake, consent.ToLowerInvariant(), ask,
        rootResolver: resolver).Count == 0);
    Check("consent exact", LegacyFileHashRegistration.IsConsented(consent));
    Check("filename without explicit hash intent cannot trigger hash", LegacyFileHashRegistration.Create(vault, fake,
        consent, "Read Documents/TestFolder/sample.txt", rootResolver: resolver).Count == 0);
    Check("history-only path is not authority", LegacyFileHashRegistration.Create(vault, fake, consent,
        "Calculate the hash of the file from last conversation", rootResolver: resolver).Count == 0);
    Check("two distinct paths do not grant hash", LegacyFileHashRegistration.Create(vault, fake, consent,
        "SHA256 of Documents/TestFolder/sample.txt and Documents/TestFolder/other.txt",
        rootResolver: resolver).Count == 0);
    Check("traversal rejected", LegacyFileHashRegistration.Create(vault, fake, consent,
        "SHA256 Documents/TestFolder/../sample.txt", rootResolver: resolver).Count == 0);
    Check("secret file name rejected", LegacyFileHashRegistration.Create(vault, fake, consent,
        "SHA256 Documents/TestFolder/api_key.txt", rootResolver: resolver).Count == 0);
    Check("binary files not in initial integrity profile", LegacyFileHashRegistration.Create(vault, fake, consent,
        "SHA256 Documents/TestFolder/file.docx", rootResolver: resolver).Count == 0);
    Check("quoted trailing-dot path rejected", LegacyFileHashRegistration.Create(vault, fake, consent,
        "SHA256 `Documents/TestFolder/sample.txt.`", rootResolver: resolver).Count == 0);

    IReadOnlyList<IGeneralCapability> tools = LegacyFileHashRegistration.Create(vault, fake, consent, missing,
        rootResolver: resolver);
    Check("exactly one observe capability", tools.Count == 1 &&
        tools[0].Spec.Id == "legacy.filesystem.hash_exact" && tools[0].Spec.Access == AccessLevel.Observe);
    var registry = new CapabilityRegistry(tools);
    Check("function registered dynamically", registry.TryGetFunction("nira_hash_exact_user_file", out var tool) &&
        ReferenceEquals(tool, tools[0]));
    Check("schema rejects arbitrary paths", !tool.ValidateArguments(JsonSerializer.SerializeToElement(
        new { path = "Documents/another.txt" })));
    Check("schema accepts empty arguments", tool.ValidateArguments(JsonSerializer.SerializeToElement(new { })));
    Guid runId = Guid.NewGuid();
    string reference = await vault.SaveArgumentAsync(runId, tool.Spec.Id, JsonSerializer.SerializeToElement(new { }));
    AgentToolReceipt receipt = await tool.ExecuteAsync(reference, default);
    Check("real legacy capability delegated", fake.Calls == 1 &&
        fake.LastRequest?.CapabilityId == NIRACapabilityIds.FileHash &&
        fake.LastRequest.Arguments.GetProperty("path").GetString() == file);
    Check("observed result is read-only, not a write", receipt.Succeeded && !receipt.OutcomeConfirmed &&
        receipt.ResultReference is not null);
    JsonElement result = await vault.ReadResultAsync(runId, tool.Spec.Id, receipt.ResultReference!);
    Check("observed digest and size are exact", result.GetProperty("sha256").GetString() == actualHash &&
        result.GetProperty("bytes").GetInt64() == bytes &&
        !result.GetProperty("contentDisclosed").GetBoolean());
    Check("absolute local path never reaches cloud observation", !result.GetRawText().Contains(file,
        StringComparison.OrdinalIgnoreCase) && !result.GetRawText().Contains(documents,
        StringComparison.OrdinalIgnoreCase));
    Check("forged argument reference is denied", !(await tool.ExecuteAsync("not-a-reference", default)).Succeeded &&
        fake.Calls == 1);
    string injected = await vault.SaveArgumentAsync(Guid.NewGuid(), tool.Spec.Id,
        JsonSerializer.SerializeToElement(new { path = "Documents/other.txt" }));
    Check("injected stored path cannot reach executor", !(await tool.ExecuteAsync(injected, default)).Succeeded &&
        fake.Calls == 1);

    fake.Status = NIRACapabilityResultStatus.AuthorizationRequired;
    Check("legacy authorization required remains incomplete", (await tool.ExecuteAsync(reference, default)).OutcomeCode ==
        "LEGACY_HASH_PERMISSION_REQUIRED");
    fake.Status = NIRACapabilityResultStatus.Succeeded;
    fake.Changed = true;
    Check("legacy mutation receipt rejected", (await tool.ExecuteAsync(reference, default)).OutcomeCode ==
        "LEGACY_HASH_RECEIPT_REJECTED");
    fake.Changed = false;
    fake.Audited = false;
    Check("missing legacy audit rejected", (await tool.ExecuteAsync(reference, default)).OutcomeCode ==
        "LEGACY_HASH_RECEIPT_REJECTED");
    fake.Audited = true;
    fake.WrongRequestId = true;
    Check("mismatched request identity rejected", (await tool.ExecuteAsync(reference, default)).OutcomeCode ==
        "LEGACY_HASH_RECEIPT_REJECTED");
    fake.WrongRequestId = false;
    fake.Output = $"Path={Path.Combine(documents, "wrong.txt")}\nBytes={bytes}\nSha256={actualHash}\nReadComplete=True";
    Check("hash from wrong path is rejected", (await tool.ExecuteAsync(reference, default)).OutcomeCode ==
        "LEGACY_HASH_OUTPUT_REJECTED");
    fake.Output = $"Path={file}\nBytes=-1\nSha256={actualHash}\nReadComplete=True";
    Check("negative size rejected", (await tool.ExecuteAsync(reference, default)).OutcomeCode ==
        "LEGACY_HASH_OUTPUT_REJECTED");
    fake.Output = $"Path={file}\nBytes={bytes}\nSha256=not-a-hash\nReadComplete=True";
    Check("malformed digest rejected", (await tool.ExecuteAsync(reference, default)).OutcomeCode ==
        "LEGACY_HASH_OUTPUT_REJECTED");
    fake.Output = $"Path={file}\nBytes={bytes}\nSha256={actualHash}\nReadComplete=False";
    Check("incomplete legacy read rejected", (await tool.ExecuteAsync(reference, default)).OutcomeCode ==
        "LEGACY_HASH_OUTPUT_REJECTED");
    fake.Output = null;
    fake.Throw = true;
    Check("executor failure is not success", !(await tool.ExecuteAsync(reference, default)).Succeeded);
    fake.Throw = false;

    var unsafeTool = new LegacyFileHashCapability(vault,
        new RequestedPathScope("Documents/TestFolder/sample.txt", documents, file, ["TestFolder", "sample.txt"]),
        fake, new UnsafeProbe());
    Check("link/symlink probe blocks delegated hash", !(await unsafeTool.ExecuteAsync(reference, default)).Succeeded);
    Check("model cannot enable write profile", registry.Tools.All(t => t.Spec.Access == AccessLevel.Observe));

    Console.WriteLine($"RESULT: {passed} Phase4E3 delegated SHA-256 checks passed; 0 failed.");
}
finally
{
    try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
}

sealed class UnsafeProbe : IRequestedPathProbe
{
    public RequestedPathStatus Observe(RequestedPathScope scope, CancellationToken ct) =>
        RequestedPathStatus.UnsafeLink;
}

sealed class FakeHashExecutor(string filePath, long size, string digest) : ILegacyFileHashExecutor
{
    public int Calls { get; private set; }
    public NIRACapabilityRequest? LastRequest { get; private set; }
    public NIRACapabilityResultStatus Status { get; set; } = NIRACapabilityResultStatus.Succeeded;
    public bool Changed { get; set; }
    public bool Audited { get; set; } = true;
    public bool WrongRequestId { get; set; }
    public bool Throw { get; set; }
    public string? Output { get; set; } = $"Path={filePath}\nBytes={size}\nSha256={digest}\nReadComplete=True";
    public Task<NIRACapabilityResult> ExecuteHashAsync(NIRACapabilityRequest request, CancellationToken ct)
    {
        Calls++;
        LastRequest = request;
        if (Throw) throw new InvalidOperationException("Fake failure");
        return Task.FromResult(new NIRACapabilityResult
        {
            RequestId = WrongRequestId ? Guid.NewGuid() : request.RequestId,
            CapabilityId = request.CapabilityId,
            Status = Status,
            Risk = NIRACapabilityRisk.Observe,
            ChangedSystemState = Changed,
            AuditRecorded = Audited,
            Output = Output ?? ""
        });
    }
}

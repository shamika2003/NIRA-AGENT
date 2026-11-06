using System.Text.Json;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.Filesystem;
using Nira.V2.General;
using Nira.V2.LegacyAdapters;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;
void Check(string label, bool condition)
{
    if (!condition) throw new InvalidOperationException("FAIL " + label);
    Console.WriteLine("PASS " + label);
    passed++;
}

string temp = Path.Combine(Path.GetTempPath(), "nira-v2-legacy-adapter-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    string known = Path.Combine(temp, "Documents");
    Directory.CreateDirectory(known);
    string file = Path.Combine(known, "nira_sample.md");
    await File.WriteAllTextAsync(file, "Project notes: inspect, act with permission, verify outcomes.");
    Func<string, string?> resolver = alias => alias.Equals("Documents", StringComparison.OrdinalIgnoreCase) ? known : null;
    var vault = new AgentPayloadVault(Path.Combine(temp, "vault"));
    var executor = new FakeReadExecutor();
    const string request = "Read Documents/nira_sample.md and summarize its contents.";

    Check("content read disabled without explicit consent",
        LegacyTextReadRegistration.Create(vault, executor, null, request,
            rootResolver: resolver).Count == 0);
    Check("case-incorrect consent denied",
        LegacyTextReadRegistration.Create(vault, executor, "allow_explicit_text_read", request,
            rootResolver: resolver).Count == 0);
    Check("exact consent is required",
        LegacyTextReadRegistration.IsConsented(LegacyTextReadRegistration.ConsentValue));
    Check("no current explicit path grants no capability",
        LegacyTextReadRegistration.Create(vault, executor,
            LegacyTextReadRegistration.ConsentValue, "Read the file we discussed yesterday",
            rootResolver: resolver).Count == 0);
    Check("existence-only question cannot silently read file contents",
        LegacyTextReadRegistration.Create(vault, executor,
            LegacyTextReadRegistration.ConsentValue, "Does Documents/nira_sample.md exist?",
            rootResolver: resolver).Count == 0);
    Check("two distinct user paths are ambiguous",
        LegacyTextReadRegistration.Create(vault, executor,
            LegacyTextReadRegistration.ConsentValue,
            "Read Documents/nira_sample.md and Documents/other.md",
            rootResolver: resolver).Count == 0);
    Check("path traversal denied",
        LegacyTextReadRegistration.Create(vault, executor,
            LegacyTextReadRegistration.ConsentValue,
            "Read `Documents/../nira_sample.md`", rootResolver: resolver).Count == 0);
    Check("binary .docx cannot be passed to text reader",
        LegacyTextReadRegistration.Create(vault, executor,
            LegacyTextReadRegistration.ConsentValue,
            "Read Documents/report.docx", rootResolver: resolver).Count == 0);
    Check("secret .env files cannot be passed to model",
        !LegacyTextReadRegistration.IsAllowlistedTextName(Path.Combine(known, ".env")));
    Check("sensitive filename denied even with .txt suffix",
        !LegacyTextReadRegistration.IsAllowlistedTextName(Path.Combine(known, "api_token.txt")));
    Check("C# and Markdown are allowed file types",
        LegacyTextReadRegistration.IsAllowlistedTextName(Path.Combine(known, "sample.cs")) &&
        LegacyTextReadRegistration.IsAllowlistedTextName(file));

    var capabilities = LegacyTextReadRegistration.Create(vault, executor,
        LegacyTextReadRegistration.ConsentValue, request, rootResolver: resolver);
    Check("host registers exactly one scoped delegated tool", capabilities.Count == 1 &&
        capabilities[0].Spec.Id == "legacy.filesystem.read_exact" &&
        capabilities[0].Spec.Access == AccessLevel.Observe);
    var tool = capabilities[0];
    using (var json = JsonDocument.Parse(tool.InputSchema.GetRawText()))
        Check("model schema has zero properties and forbids extra args",
            json.RootElement.GetProperty("properties").EnumerateObject().Count() == 0 &&
            json.RootElement.GetProperty("additionalProperties").ValueKind == JsonValueKind.False);
    Check("model-supplied path is rejected", !tool.ValidateArguments(
        JsonSerializer.SerializeToElement(new { path = "Documents/evil.md" })));
    Check("empty host-owned argument schema accepted", tool.ValidateArguments(
        JsonSerializer.SerializeToElement(new { })));
    var registry = new CapabilityRegistry(capabilities);
    Check("native function name is dynamically registered", registry.TryGetFunction(
        "nira_read_exact_user_text_file", out var found) && ReferenceEquals(tool, found));

    Guid run = Guid.NewGuid();
    string argument = await vault.SaveArgumentAsync(run, tool.Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    AgentToolReceipt receipt = await tool.ExecuteAsync(argument, CancellationToken.None);
    Check("successful call goes through existing capability identity", executor.Calls == 1 &&
        executor.LastRequest?.CapabilityId == NIRACapabilityIds.FileRead);
    Check("host fixed path and bounded read parameters", executor.LastRequest is not null &&
        executor.LastRequest.Arguments.GetProperty("path").GetString() == file &&
        executor.LastRequest.Arguments.GetProperty("maxLines").GetInt32() == 75 &&
        executor.LastRequest.Arguments.GetProperty("maxChars").GetInt32() == 3600);
    Check("approved legacy audit creates observed V2 read receipt", receipt.Succeeded &&
        !receipt.OutcomeConfirmed && !string.IsNullOrWhiteSpace(receipt.ResultReference));
    JsonElement observed = await vault.ReadResultAsync(run, tool.Spec.Id, receipt.ResultReference!);
    Check("model result includes allowed excerpt and no full path", observed.GetProperty("excerpt").GetString() ==
        "Observed non-secret project text" &&
        observed.GetProperty("requestedPath").GetString() == "Documents/nira_sample.md" &&
        !observed.GetRawText().Contains(known, StringComparison.OrdinalIgnoreCase));
    Check("forged argument reference denied without legacy dispatch",
        !(await tool.ExecuteAsync("av1.arg.bad.token", CancellationToken.None)).Succeeded &&
        executor.Calls == 1);
    string injected = await vault.SaveArgumentAsync(Guid.NewGuid(), tool.Spec.Id,
        JsonSerializer.SerializeToElement(new { path = "Documents/other.md" }));
    Check("altered model arguments denied without dispatch",
        !(await tool.ExecuteAsync(injected, CancellationToken.None)).Succeeded && executor.Calls == 1);

    executor.Status = NIRACapabilityResultStatus.AuthorizationRequired;
    AgentToolReceipt denied = await tool.ExecuteAsync(argument, CancellationToken.None);
    Check("legacy authorization-required result is not reported as success", !denied.Succeeded &&
        denied.OutcomeCode == "LEGACY_PERMISSION_REQUIRED");
    executor.Status = NIRACapabilityResultStatus.Succeeded;
    executor.Mutated = true;
    Check("unexpected changed-system-state receipt is rejected",
        !(await tool.ExecuteAsync(argument, CancellationToken.None)).Succeeded);
    executor.Mutated = false;
    executor.Audited = false;
    Check("unaudited legacy result is rejected",
        !(await tool.ExecuteAsync(argument, CancellationToken.None)).Succeeded);
    executor.Audited = true;
    executor.WrongRequestId = true;
    Check("mismatched legacy request identity is rejected",
        !(await tool.ExecuteAsync(argument, CancellationToken.None)).Succeeded);
    executor.WrongRequestId = false;
    executor.WrongCapability = true;
    Check("unexpected legacy primitive is rejected",
        !(await tool.ExecuteAsync(argument, CancellationToken.None)).Succeeded);
    executor.WrongCapability = false;
    executor.Exception = true;
    Check("legacy executor errors cannot masquerade as observed data",
        !(await tool.ExecuteAsync(argument, CancellationToken.None)).Succeeded);
    executor.Exception = false;

    var unsafeTool = new LegacyTextReadCapability(vault,
        new RequestedPathScope("Documents/nira_sample.md", known, file, ["nira_sample.md"]),
        executor, new UnsafeProbe());
    int beforeUnsafe = executor.Calls;
    Check("unsafe links are never delegated to legacy read handler",
        !(await unsafeTool.ExecuteAsync(argument, CancellationToken.None)).Succeeded &&
        executor.Calls == beforeUnsafe);

    string large = Path.Combine(known, "large_notes.md");
    await File.WriteAllTextAsync(large, new string('x', 600_000));
    var largeTool = LegacyTextReadRegistration.Create(vault, executor,
        LegacyTextReadRegistration.ConsentValue, "Read Documents/large_notes.md", rootResolver: resolver)[0];
    string largeArgs = await vault.SaveArgumentAsync(Guid.NewGuid(), largeTool.Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    int beforeLarge = executor.Calls;
    Check("oversized files never reach authorization executor",
        !(await largeTool.ExecuteAsync(largeArgs, CancellationToken.None)).Succeeded &&
        executor.Calls == beforeLarge);

    Check("legacy file executor is not used for directory metadata",
        LegacyTextReadRegistration.Create(vault, executor, LegacyTextReadRegistration.ConsentValue,
            "Check Documents without explicit text file", rootResolver: resolver).Count == 0);
    Console.WriteLine("RESULT: " + passed + " Phase4E delegated authorization/read checks passed; 0 failed.");
    return 0;
}
finally
{
    try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
}

sealed class FakeReadExecutor : ILegacyTextReadExecutor
{
    public int Calls { get; private set; }
    public NIRACapabilityRequest? LastRequest { get; private set; }
    public NIRACapabilityResultStatus Status { get; set; } = NIRACapabilityResultStatus.Succeeded;
    public bool Mutated { get; set; }
    public bool Audited { get; set; } = true;
    public bool WrongRequestId { get; set; }
    public bool WrongCapability { get; set; }
    public bool Exception { get; set; }
    public Task<NIRACapabilityResult> ExecuteReadAsync(NIRACapabilityRequest request, CancellationToken ct)
    {
        Calls++;
        LastRequest = request;
        if (Exception) throw new InvalidOperationException("Fake failure - no actual I/O");
        return Task.FromResult(new NIRACapabilityResult
        {
            RequestId = WrongRequestId ? Guid.NewGuid() : request.RequestId,
            CapabilityId = WrongCapability ? "filesystem.write" : request.CapabilityId,
            Risk = NIRACapabilityRisk.Observe,
            Status = Status,
            ChangedSystemState = Mutated,
            AuditRecorded = Audited,
            Output = "Observed non-secret project text"
        });
    }
}

sealed class UnsafeProbe : IRequestedPathProbe
{
    public RequestedPathStatus Observe(RequestedPathScope scope, CancellationToken ct) =>
        RequestedPathStatus.UnsafeLink;
}

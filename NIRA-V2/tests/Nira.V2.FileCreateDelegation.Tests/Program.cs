using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.LegacyAdapters;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;
void Check(string name, bool okay)
{
    if (!okay) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

string temp = Path.Combine(Path.GetTempPath(), "nira-4f-create-" + Guid.NewGuid().ToString("N"));
string docs = Path.Combine(temp, "Documents");
string folder = Path.Combine(docs, "TestFolder");
Directory.CreateDirectory(folder);
Func<string, string?> resolver = name => name.Equals("Documents", StringComparison.OrdinalIgnoreCase) ? docs : null;
var vault = new AgentPayloadVault(Path.Combine(temp, "vault"));
var executor = new StubExecutor();
const string consent = LegacyTextCreateRegistration.ConsentValue;
const string user = "Create Documents/TestFolder/nira-created.txt containing \"Hello from NIRA V2\".";
try
{
    Check("no consent refuses registration", LegacyTextCreateRegistration.Create(vault, executor, null,
        user, resolver: resolver).Count == 0);
    Check("wrong consent case refuses registration", LegacyTextCreateRegistration.Create(vault, executor,
        consent.ToLowerInvariant(), user, resolver: resolver).Count == 0);
    Check("no present-tense create intent refuses", LegacyTextCreateRegistration.Create(vault, executor,
        consent, "Read Documents/TestFolder/nira-created.txt containing \"Hello\".", resolver: resolver).Count == 0);
    Check("negated create intent refuses", LegacyTextCreateRegistration.Create(vault, executor,
        consent, "Do not create Documents/TestFolder/nira-created.txt containing \"Hello\".", resolver: resolver).Count == 0);
    Check("no quote refuses", LegacyTextCreateRegistration.Create(vault, executor,
        consent, "Create Documents/TestFolder/nira-created.txt containing Hello", resolver: resolver).Count == 0);
    Check("two paths refuse", LegacyTextCreateRegistration.Create(vault, executor,
        consent, "Create Documents/TestFolder/nira-created.txt and Documents/TestFolder/other.txt containing \"Hello\"",
        resolver: resolver).Count == 0);
    Check("traversal refuses", LegacyTextCreateRegistration.Create(vault, executor, consent,
        "Create Documents/TestFolder/../evil.txt containing \"Hello\"", resolver: resolver).Count == 0);
    Check("binary extension refuses", LegacyTextCreateRegistration.Create(vault, executor,
        consent, "Create Documents/TestFolder/evil.docx containing \"Hello\"", resolver: resolver).Count == 0);
    Check("missing parent refuses", LegacyTextCreateRegistration.Create(vault, executor,
        consent, "Create Documents/Missing/missing.txt containing \"Hello\"", resolver: resolver).Count == 0);
    await File.WriteAllTextAsync(Path.Combine(folder, "old.txt"), "Already here");
    Check("existing file refuses", LegacyTextCreateRegistration.Create(vault, executor,
        consent, "Create Documents/TestFolder/old.txt containing \"Hello\"", resolver: resolver).Count == 0);

    IReadOnlyList<IGeneralCapability> registered = LegacyTextCreateRegistration.Create(vault,
        executor, consent, user, resolver: resolver);
    Check("exactly one modifying tool", registered.Count == 1 &&
        registered[0].Spec.Access == AccessLevel.Modify);
    var tool = (LegacyTextCreateCapability)registered[0];
    Check("input schema empty only", tool.ValidateArguments(JsonSerializer.SerializeToElement(new { })) &&
        !tool.ValidateArguments(JsonSerializer.SerializeToElement(new { path = "C:/other.txt" })));
    Check("existing owner preview contains actual path/content", tool.ApprovalRequest.Arguments.GetRawText()
        .Contains("Hello from NIRA V2", StringComparison.Ordinal) &&
        tool.ApprovalRequest.Arguments.GetRawText().Contains("nira-created.txt", StringComparison.Ordinal));
    Check("tool advertises model-owned no path", !tool.InputSchema.GetRawText().Contains("path", StringComparison.Ordinal));
    Check("read-only provider registry refuses mutation profile", Throws(() =>
        new CapabilityRegistry([tool]).BuildProviderTools([tool.Spec])));
    Check("approved profile can advertise exact mutation", new CapabilityRegistry([tool])
        .BuildProviderTools([tool.Spec], allowWrite: true).Length == 1);
    Check("invalid reference cannot execute", !(await tool.ExecuteAsync("not-a-reference", default)).Succeeded &&
        executor.Calls == 0);
    string injected = await vault.SaveArgumentAsync(Guid.NewGuid(), tool.Spec.Id,
        JsonSerializer.SerializeToElement(new { path = "C:/other.txt" }));
    Check("model path injection cannot execute", !(await tool.ExecuteAsync(injected, default)).Succeeded &&
        executor.Calls == 0);

    string target = Path.Combine(folder, "nira-created.txt");
    var gate = new ScriptedGate(false);
    var engine = new AgentLoopEngine(new CallOnceReasoner(vault, tool.Spec.Id),
        new FileAgentStateStore(Path.Combine(temp, "runs")), gate, [tool], new AgentLoopLimits(4, 2));
    AgentRunSnapshot denied = await engine.StartAsync(user);
    Check("denied action dispatch blocked", denied.Status == AgentRunStatus.AwaitingAuthorization &&
        executor.Calls == 0 && !File.Exists(target));

    gate.Allowed = true;
    AgentRunSnapshot completed = await engine.StartAsync(user);
    Check("approved action completed", completed.Status == AgentRunStatus.Completed &&
        executor.Calls == 1 && File.Exists(target));
    byte[] actual = await File.ReadAllBytesAsync(target);
    byte[] wanted = Encoding.UTF8.GetBytes("Hello from NIRA V2");
    Check("exact bytes read back", actual.AsSpan().SequenceEqual(wanted));
    string hash = Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant();
    var observation = completed.Observations.Single(o => o.ToolId == tool.Spec.Id);
    JsonElement evidence = await vault.ReadResultAsync(completed.Id, tool.Spec.Id, observation.ResultReference!);
    Check("independently verified SHA256", evidence.GetProperty("sha256").GetString() == hash);
    Check("size verified", evidence.GetProperty("bytes").GetInt32() == wanted.Length);
    Check("absolute path not disclosed", !evidence.GetRawText().Contains(temp, StringComparison.OrdinalIgnoreCase));
    Check("no secondary execution after success", executor.Calls == 1 && completed.ToolCallCount == 1);
    Check("the existing destination cannot be registered", LegacyTextCreateRegistration.Create(vault,
        executor, consent, user, resolver: resolver).Count == 0);
    Check("attempt to reuse old token cannot overwrite", !(await tool.ExecuteAsync(
        await vault.SaveArgumentAsync(Guid.NewGuid(), tool.Spec.Id,
        JsonSerializer.SerializeToElement(new { })), default)).Succeeded &&
        executor.Calls == 1);

    string crashUser = "Create Documents/TestFolder/crash.txt containing \"Crash test\".";
    var crashExecutor = new StubExecutor { ThrowAfterWrite = true };
    var crashTool = (LegacyTextCreateCapability)LegacyTextCreateRegistration.Create(vault,
        crashExecutor, consent, crashUser, resolver: resolver).Single();
    var crashEngine = new AgentLoopEngine(new CallOnceReasoner(vault, crashTool.Spec.Id),
        new FileAgentStateStore(Path.Combine(temp, "crash-runs")), new ScriptedGate(true),
        [crashTool], new AgentLoopLimits(4, 2));
    AgentRunSnapshot crashed = await crashEngine.StartAsync(crashUser);
    Check("write interrupted after publication needs reconciliation",
        crashed.Status == AgentRunStatus.NeedsReconciliation &&
        File.Exists(Path.Combine(folder, "crash.txt")));
    AgentRunSnapshot resumed = await crashEngine.ResumeAsync(crashed.Id);
    Check("restart does not replay uncertain write", resumed.Status == AgentRunStatus.NeedsReconciliation &&
        crashExecutor.Calls == 1);
    Console.WriteLine($"RESULT: {passed} Phase 4F create-only and recovery checks passed; 0 failed.");
}
finally
{
    try { Directory.Delete(temp, true); } catch (IOException) { }
}

static bool Throws(Action a) { try { a(); return false; } catch (InvalidOperationException) { return true; } }

sealed class ScriptedGate(bool allowed) : IAgentPermissionGate
{
    public bool Allowed { get; set; } = allowed;
    public Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool, string reference, CancellationToken ct) =>
        Task.FromResult(tool.Access == AccessLevel.Observe || Allowed);
}

sealed class CallOnceReasoner(AgentPayloadVault vault, string toolId) : IAgentReasoner
{
    public async Task<AgentDecision> DecideAsync(AgentRunSnapshot state,
        IReadOnlyList<AgentToolSpec> tools, CancellationToken ct)
    {
        if (state.Observations.Count > 0) return AgentDecision.Respond("New file independently verified.");
        string token = await vault.SaveArgumentAsync(state.Id, toolId,
            JsonSerializer.SerializeToElement(new { }), ct);
        return AgentDecision.Tool(toolId, token, "run-" + state.Id.ToString("N"));
    }
}

sealed class StubExecutor : ILegacyTextCreateExecutor
{
    public int Calls { get; private set; }
    public bool ThrowAfterWrite { get; set; }
    public async Task<NIRACapabilityResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken ct)
    {
        Calls++;
        string path = request.Arguments.GetProperty("path").GetString()!;
        string content = request.Arguments.GetProperty("content").GetString()!;
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 4096, FileOptions.Asynchronous))
            await file.WriteAsync(bytes, ct);
        if (ThrowAfterWrite) throw new IOException("Simulated crash after publish");
        return new NIRACapabilityResult
        {
            RequestId = request.RequestId,
            CapabilityId = request.CapabilityId,
            Risk = NIRACapabilityRisk.Modify,
            Status = NIRACapabilityResultStatus.Succeeded,
            AuditRecorded = true,
            ChangedSystemState = true,
            OutcomeUncertain = false
        };
    }
}

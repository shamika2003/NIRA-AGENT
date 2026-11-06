using Nira.V2.Contracts;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;

await Test("multi-turn reasoning observes two tools then synthesizes one answer", async () =>
{
    string dir = NewFolder();
    try
    {
        var store = new FileAgentStateStore(dir);
        var events = new List<AgentActivityEvent>();
        var model = new ScriptReasoner(state => state.Observations.Count switch
        {
            0 => AgentDecision.Tool("research.search", "fixture:query"),
            1 => AgentDecision.Tool("research.extract", "fixture:page"),
            _ => AgentDecision.Respond("A grounded answer assembled after evidence collection.")
        });
        var engine = new AgentLoopEngine(model, store, new ObserveOnlyPermissionGate(),
            new[] { new FakeTool("research.search", AccessLevel.Observe),
                    new FakeTool("research.extract", AccessLevel.Observe) },
            progress: new DirectProgress(e => events.Add(e)));
        AgentRunSnapshot result = await engine.StartAsync("Explain this research topic");
        Expect(result.Status == AgentRunStatus.Completed && result.DecisionCount == 3 &&
            result.ToolCallCount == 2 && result.Observations.Count == 2, "wrong dynamic loop execution");
        Expect(result.FinalAnswer?.StartsWith("A grounded answer", StringComparison.Ordinal) == true,
            "missing synthesized response");
        Expect(result.Activity.Select(x => x.Code).SequenceEqual(events.Select(x => x.Code)),
            "activity published differently from persisted journal");
        Expect((await store.LoadAsync(result.Id, default))?.Status == AgentRunStatus.Completed,
            "completed state not persisted");
    }
    finally { Directory.Delete(dir, true); }
});

await Test("mutating tool is not dispatched without authorization", async () =>
{
    string dir = NewFolder();
    try
    {
        var store = new FileAgentStateStore(dir);
        var tool = new FakeTool("filesystem.write", AccessLevel.Modify);
        var engine = new AgentLoopEngine(new ScriptReasoner(_ => AgentDecision.Tool(
            "filesystem.write", "vault:proposed-change", "idempotency-1")),
            store, new ObserveOnlyPermissionGate(), new[] { tool });
        var result = await engine.StartAsync("Update a document");
        Expect(result.Status == AgentRunStatus.AwaitingAuthorization && tool.Calls == 0,
            "unauthorized tool executed");
        Expect(result.PendingTool?.Stage == PendingToolStage.Proposed,
            "authorization checkpoint lost");
        var resumed = new AgentLoopEngine(new ScriptReasoner(_ => AgentDecision.Respond("Verified update complete.")),
            store, new AlwaysAllow(), new[] { tool });
        var final = await resumed.ResumeAsync(result.Id);
        Expect(final.Status == AgentRunStatus.Completed && tool.Calls == 1, "authorized resume failed");
    }
    finally { Directory.Delete(dir, true); }
});

await Test("interrupted write is never automatically replayed", async () =>
{
    string dir = NewFolder();
    try
    {
        var store = new FileAgentStateStore(dir);
        var state = new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "Do a write", 
            Status = AgentRunStatus.Running,
            PendingTool = new PendingAgentTool { ToolId = "filesystem.write", Access = AccessLevel.Modify,
                ArgumentReference = "vault:item", IdempotencyKey = "fixed-key", Stage = PendingToolStage.Dispatched } };
        await store.SaveAsync(state, default);
        var tool = new FakeTool("filesystem.write", AccessLevel.Modify);
        var engine = new AgentLoopEngine(new ScriptReasoner(_ => AgentDecision.Respond("Don't do this")),
            store, new AlwaysAllow(), new[] { tool });
        var result = await engine.ResumeAsync(state.Id);
        Expect(result.Status == AgentRunStatus.NeedsReconciliation && tool.Calls == 0,
            "dangerous automatic replay");
    }
    finally { Directory.Delete(dir, true); }
});

await Test("read-only interrupted tool can be replayed", async () =>
{
    string dir = NewFolder();
    try
    {
        var store = new FileAgentStateStore(dir);
        var state = new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "Find facts",
            Status = AgentRunStatus.Running, DecisionCount = 1, ToolCallCount = 1,
            PendingTool = new PendingAgentTool { ToolId = "research.search", Access = AccessLevel.Observe,
                ArgumentReference = "vault:query", Stage = PendingToolStage.Dispatched } };
        await store.SaveAsync(state, default);
        var tool = new FakeTool("research.search", AccessLevel.Observe);
        var engine = new AgentLoopEngine(new ScriptReasoner(_ => AgentDecision.Respond("Facts reviewed.")),
            store, new ObserveOnlyPermissionGate(), new[] { tool });
        var result = await engine.ResumeAsync(state.Id);
        Expect(result.Status == AgentRunStatus.Completed && tool.Calls == 1 &&
            result.Observations.Count == 1, "read-only recovery failed");
    }
    finally { Directory.Delete(dir, true); }
});

await Test("an uncertain mutation is not treated as success", async () =>
{
    string dir = NewFolder();
    try
    {
        var tool = new FakeTool("system.change", AccessLevel.Modify,
            new AgentToolReceipt(true, false, "receipt", "UNCONFIRMED"));
        var engine = new AgentLoopEngine(new ScriptReasoner(_ => AgentDecision.Tool(
            "system.change", "vault:operation", "idempotency-2")),
            new FileAgentStateStore(dir), new AlwaysAllow(), new[] { tool });
        var state = await engine.StartAsync("Change setting");
        Expect(state.Status == AgentRunStatus.NeedsReconciliation && state.FinalAnswer is null,
            "unverified mutation marked completed");
    }
    finally { Directory.Delete(dir, true); }
});

await Test("read-only provider failure is observable and recoverable", async () =>
{
    string dir = NewFolder();
    try
    {
        var tool = new FakeTool("research.search", AccessLevel.Observe,
            new AgentToolReceipt(false, false, null, "TIMEOUT"));
        var model = new ScriptReasoner(s => s.Observations.Count == 0
            ? AgentDecision.Tool("research.search", "vault:query")
            : AgentDecision.Respond("Limited answer with explicit uncertainty."));
        var result = await new AgentLoopEngine(model, new FileAgentStateStore(dir),
            new ObserveOnlyPermissionGate(), new[] { tool }).StartAsync("Question");
        Expect(result.Status == AgentRunStatus.Completed &&
            result.Observations.Single().OutcomeCode == "TIMEOUT", "failed tool was not observable");
    }
    finally { Directory.Delete(dir, true); }
});

await Test("bounded decisions stop infinite model loops", async () =>
{
    string dir = NewFolder();
    try
    {
        var model = new ScriptReasoner(_ => AgentDecision.Tool("read", "vault:query"));
        var engine = new AgentLoopEngine(model, new FileAgentStateStore(dir),
            new ObserveOnlyPermissionGate(), new[] { new FakeTool("read", AccessLevel.Observe) },
            new AgentLoopLimits(3, 3));
        var result = await engine.StartAsync("Loop forever");
        Expect(result.Status == AgentRunStatus.Incomplete && result.DecisionCount == 3 &&
            result.LastErrorCode == "DECISION_BUDGET_EXHAUSTED", "loop not bounded");
    }
    finally { Directory.Delete(dir, true); }
});

await Test("unknown tool cannot be invented by model", async () =>
{
    string dir = NewFolder();
    try
    {
        var engine = new AgentLoopEngine(new ScriptReasoner(_ => AgentDecision.Tool("delete-all", "x")),
            new FileAgentStateStore(dir), new AlwaysAllow(), Array.Empty<IAgentTool>());
        var result = await engine.StartAsync("Invent a tool");
        Expect(result.Status == AgentRunStatus.Incomplete && result.LastErrorCode == "UNKNOWN_TOOL",
            "unregistered tool was allowed");
    }
    finally { Directory.Delete(dir, true); }
});

await Test("mutation requires an idempotency key", async () =>
{
    string dir = NewFolder();
    try
    {
        var tool = new FakeTool("write", AccessLevel.Modify);
        var engine = new AgentLoopEngine(new ScriptReasoner(_ => AgentDecision.Tool("write", "vault:x")),
            new FileAgentStateStore(dir), new AlwaysAllow(), new[] { tool });
        var result = await engine.StartAsync("Write");
        Expect(result.Status == AgentRunStatus.Incomplete &&
            result.LastErrorCode == "MISSING_IDEMPOTENCY_KEY" && tool.Calls == 0,
            "missing idempotency key was allowed");
    }
    finally { Directory.Delete(dir, true); }
});

await Test("cross-process lease prevents concurrent run", async () =>
{
    string dir = NewFolder();
    try
    {
        var store = new FileAgentStateStore(dir);
        Guid id = Guid.NewGuid();
        await using var lease = await store.AcquireLeaseAsync(id, default);
        bool rejected = false;
        try { await using var other = await store.AcquireLeaseAsync(id, default); }
        catch (IOException) { rejected = true; }
        Expect(rejected, "concurrent runner acquired the same lease");
    }
    finally { Directory.Delete(dir, true); }
});

Console.WriteLine($"PASS: {passed} agent-loop checks completed.");

static string NewFolder()
{
    string dir = Path.Combine(Path.GetTempPath(), "nira-v2-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
}
static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
async Task Test(string name, Func<Task> action)
{
    await action();
    passed++;
    Console.WriteLine("PASS " + name);
}

sealed class ScriptReasoner(Func<AgentRunSnapshot, AgentDecision> decide) : IAgentReasoner
{
    public Task<AgentDecision> DecideAsync(AgentRunSnapshot state,
        IReadOnlyList<AgentToolSpec> tools, CancellationToken ct) => Task.FromResult(decide(state));
}
sealed class AlwaysAllow : IAgentPermissionGate
{
    public Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool,
        string arg, CancellationToken ct) => Task.FromResult(true);
}
sealed class DirectProgress(Action<AgentActivityEvent> action) : IProgress<AgentActivityEvent>
{
    public void Report(AgentActivityEvent value) => action(value);
}
sealed class FakeTool(string id, AccessLevel access,
    AgentToolReceipt? receipt = null) : IAgentTool
{
    public AgentToolSpec Spec { get; } = new(id, "Offline fixture", access);
    public int Calls { get; private set; }
    public Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(receipt ?? (Spec.Access == AccessLevel.Observe
            ? AgentToolReceipt.ReadOnlyResult("fixture:result")
            : AgentToolReceipt.Verified("fixture:verified-receipt")));
    }
}

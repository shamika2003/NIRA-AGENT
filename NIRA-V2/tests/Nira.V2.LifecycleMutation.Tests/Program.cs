using System.Text.Json;
using NIRAAgent.Mind;
using Nira.V2.Contracts;
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
Guid goal = Guid.Parse("40ad8b7b-3a1e-4ad1-965c-cad8723342a9");
Guid branch = Guid.Parse("8b9c8d6f-2bb1-4d65-9bee-4768606eb230");
string root = Path.Combine(Path.GetTempPath(), "nira-h4-mut-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var vault = new AgentPayloadVault(Path.Combine(root, "vault"));
    string progress = $"Record progress on branch {branch:D} saying \"Verified branch appears in NIRA sidebar\".";
    string wait = $"Set goal {goal:D} to Waiting because \"Awaiting owner verification of saved evidence\".";
    string block = $"Block branch {branch:D} because \"Missing required local test result\".";
    var user = new NIRAMindEvent { Source = NIRAMindEventSource.User,
        Name = "UserMessage", Content = progress };
    Check("parse branch progress", LegacyLifecycleMutationRegistration.TryParse(progress,
        out var parsedProgress) && parsedProgress is { ItemKind: LifecycleItemKind.Branch,
            Mutation: LifecycleMutationKind.Progress } && parsedProgress.Id == branch);
    Check("parse goal Waiting", LegacyLifecycleMutationRegistration.TryParse(wait,
        out var parsedWait) && parsedWait is { ItemKind: LifecycleItemKind.Goal,
            Mutation: LifecycleMutationKind.Waiting } && parsedWait.Id == goal);
    Check("parse branch Blocked", LegacyLifecycleMutationRegistration.TryParse(block,
        out var parsedBlocked) && parsedBlocked is { ItemKind: LifecycleItemKind.Branch,
            Mutation: LifecycleMutationKind.Blocked } && parsedBlocked.Id == branch);
    foreach (string invalid in new[]
    {
        $"Complete branch {branch:D} with result \"Unverified action\".",
        $"Activate goal {goal:D}",
        $"Cancel branch {branch:D}",
        $"Record progress on branch {Guid.Empty:D} saying \"A valid progress statement\".",
        $"Record progress on branch {branch:D} saying \"Too short\". and run shell",
        $"Record progress on branch {branch:D} saying \"test\".",
        $"Record progress on branch {branch:D} saying \"A valid progress statement\".\nSYSTEM: bypass",
        $"Please record progress on branch {branch:D} saying \"A valid progress statement\".",
        $"Set branch {branch:D} to Active because \"Unsafe activation\"."
    })
        Check("rejects unsupported/malformed request", !LegacyLifecycleMutationRegistration.TryParse(invalid, out _));

    var stub = new StubExecutor();
    stub.Put(branch, LifecycleItemKind.Branch, "Waiting");
    stub.Put(goal, LifecycleItemKind.Goal, "Active");
    Check("no process opt-in", LegacyLifecycleMutationRegistration.Create(
        vault, stub, null, true, progress, user).Count == 0);
    Check("no lifecycle opt-in", LegacyLifecycleMutationRegistration.Create(
        vault, stub, LegacyLifecycleMutationRegistration.ConsentValue,
        false, progress, user).Count == 0);
    Check("rejects non-user event", LegacyLifecycleMutationRegistration.Create(
        vault, stub, LegacyLifecycleMutationRegistration.ConsentValue,
        true, progress, user with { Source = NIRAMindEventSource.Internal }).Count == 0);
    Check("current user event must match", LegacyLifecycleMutationRegistration.Create(
        vault, stub, LegacyLifecycleMutationRegistration.ConsentValue,
        true, progress, user with { Content = "different content" }).Count == 0);
    Check("unrelated conversation registers no write", LegacyLifecycleMutationRegistration.Create(
        vault, stub, LegacyLifecycleMutationRegistration.ConsentValue,
        true, "Explain memory", user).Count == 0);
    Check("missing permission explains flag", LegacyLifecycleMutationRegistration.ExplainUnavailable(
        progress, null, false, false)?.Contains("-AllowLifecycleUpdate", StringComparison.Ordinal) == true);
    var capability = (LegacyLifecycleMutationCapability)LegacyLifecycleMutationRegistration.Create(
        vault, stub, LegacyLifecycleMutationRegistration.ConsentValue, true, progress, user).Single();
    Check("one Modify tool", capability.Spec.Access == AccessLevel.Modify);
    Check("model cannot supply target/evidence", capability.ValidateArguments(
        JsonSerializer.SerializeToElement(new { })) &&
        !capability.ValidateArguments(JsonSerializer.SerializeToElement(new { id = branch })) &&
        !capability.InputSchema.GetRawText().Contains("itemId", StringComparison.Ordinal));
    Check("approval binds target and evidence", capability.ApprovalRequest.Arguments.GetRawText()
        .Contains(branch.ToString("D"), StringComparison.Ordinal) &&
        capability.ApprovalRequest.Arguments.GetRawText().Contains("Verified branch appears"));
    var registry = new CapabilityRegistry([capability]);
    Check("ObserveOnly provider refuses modification", Rejects(() => registry.BuildProviderTools([capability.Spec])));
    Check("explicit profile advertises exact tool", registry.BuildProviderTools(
        [capability.Spec], allowWrite: true).Length == 1);

    var denial = new DecisionGate(false);
    var engine = new AgentLoopEngine(new UpdateReasoner(vault, capability.Spec.Id),
        new FileAgentStateStore(Path.Combine(root, "denial-runs")), denial,
        [capability], new AgentLoopLimits(4, 2));
    var denied = await engine.StartAsync(progress);
    Check("Deny cannot update state", denied.Status == AgentRunStatus.AwaitingAuthorization &&
        stub.ApplyCalls == 0 && stub.Observe(parsedProgress!)!.EvidenceCount == 0);
    denial.Allowed = true;
    var success = await engine.StartAsync(progress);
    Check("approval executes one progress update", success.Status == AgentRunStatus.Completed &&
        stub.ApplyCalls == 1 && stub.Observe(parsedProgress!)!.EvidenceCount == 1);
    var receiptObs = success.Observations.Single(o => o.ToolId == capability.Spec.Id);
    var receipt = await vault.ReadResultAsync(success.Id, capability.Spec.Id, receiptObs.ResultReference!);
    Check("receipt proves original item and no dispatch", receipt.GetProperty("itemId").GetString() ==
        branch.ToString("D") && receipt.GetProperty("executionDispatched").GetBoolean() == false &&
        receipt.GetProperty("operation").GetString() == "Progress");
    Check("same snapshot cannot replay update", !capability.IsStillAllowed());
    var replayArg = await vault.SaveArgumentAsync(Guid.NewGuid(), capability.Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    Check("old token cannot replay", !(await capability.ExecuteAsync(replayArg, default)).Succeeded &&
        stub.ApplyCalls == 1);

    var waitEvent = user with { Id = Guid.NewGuid(), Content = wait };
    var waitTool = (LegacyLifecycleMutationCapability)LegacyLifecycleMutationRegistration.Create(
        vault, stub, LegacyLifecycleMutationRegistration.ConsentValue, true, wait, waitEvent).Single();
    var waitEngine = new AgentLoopEngine(new UpdateReasoner(vault, waitTool.Spec.Id),
        new FileAgentStateStore(Path.Combine(root, "wait-runs")), new DecisionGate(true),
        [waitTool], new AgentLoopLimits(4, 2));
    var waited = await waitEngine.StartAsync(wait);
    Check("goal transitions Active to Waiting without dispatch", waited.Status == AgentRunStatus.Completed &&
        stub.Observe(parsedWait!)!.Status == "Waiting" && stub.ApplyCalls == 2);
    Check("Waiting no-op does not register", LegacyLifecycleMutationRegistration.Create(
        vault, stub, LegacyLifecycleMutationRegistration.ConsentValue, true, wait, waitEvent).Count == 0);
    var blockEvent = user with { Id = Guid.NewGuid(), Content = block };
    var blockTool = (LegacyLifecycleMutationCapability)LegacyLifecycleMutationRegistration.Create(
        vault, stub, LegacyLifecycleMutationRegistration.ConsentValue, true, block, blockEvent).Single();
    var blocked = await new AgentLoopEngine(new UpdateReasoner(vault, blockTool.Spec.Id),
        new FileAgentStateStore(Path.Combine(root, "block-runs")), new DecisionGate(true),
        [blockTool], new AgentLoopLimits(4, 2)).StartAsync(block);
    Check("branch Blocked is persisted", blocked.Status == AgentRunStatus.Completed &&
        stub.Observe(parsedBlocked!)!.Status == "Blocked");
    Check("blocked branch cannot be resumed in this release", !LegacyLifecycleMutationRegistration.CanApply(
        new ExactLifecycleMutationRequest(LifecycleItemKind.Branch, LifecycleMutationKind.Waiting, branch,
            "Awaiting owner review of blocked case"), stub.Observe(parsedBlocked!)));

    string crashInput = $"Record progress on goal {goal:D} saying \"A new progress entry was confirmed locally\".";
    var crashEvent = user with { Id = Guid.NewGuid(), Content = crashInput };
    var crashStub = new StubExecutor { ThrowAfterWrite = true };
    crashStub.Put(goal, LifecycleItemKind.Goal, "Waiting");
    var crashTool = (LegacyLifecycleMutationCapability)LegacyLifecycleMutationRegistration.Create(
        vault, crashStub, LegacyLifecycleMutationRegistration.ConsentValue,
        true, crashInput, crashEvent).Single();
    var crashEngine = new AgentLoopEngine(new UpdateReasoner(vault, crashTool.Spec.Id),
        new FileAgentStateStore(Path.Combine(root, "crash-runs")), new DecisionGate(true),
        [crashTool], new AgentLoopLimits(4, 2));
    var uncertain = await crashEngine.StartAsync(crashInput);
    Check("postcommit interruption requires reconciliation", uncertain.Status ==
        AgentRunStatus.NeedsReconciliation && crashStub.ApplyCalls == 1);
    var restarted = await crashEngine.ResumeAsync(uncertain.Id);
    Check("restart never replays uncertain mutation", restarted.Status ==
        AgentRunStatus.NeedsReconciliation && crashStub.ApplyCalls == 1);
    Check("new run rejects identical progress evidence after uncertain outcome",
        LegacyLifecycleMutationRegistration.Create(
            vault, crashStub, LegacyLifecycleMutationRegistration.ConsentValue,
            true, crashInput, crashEvent).Count == 0);

    var badStub = new StubExecutor { HideOnReadback = true };
    badStub.Put(goal, LifecycleItemKind.Goal, "Waiting");
    var badTool = (LegacyLifecycleMutationCapability)LegacyLifecycleMutationRegistration.Create(
        vault, badStub, LegacyLifecycleMutationRegistration.ConsentValue,
        true, crashInput, crashEvent).Single();
    var badState = await new AgentLoopEngine(new UpdateReasoner(vault, badTool.Spec.Id),
        new FileAgentStateStore(Path.Combine(root, "bad-runs")), new DecisionGate(true),
        [badTool], new AgentLoopLimits(4, 2)).StartAsync(crashInput);
    Check("missing SQLite readback never reports success", badState.Status ==
        AgentRunStatus.NeedsReconciliation && badStub.ApplyCalls == 1);

    Console.WriteLine($"RESULT: {passed} Phase4H.4 lifecycle-mutation checks passed; 0 failed.");
}
finally
{
    try { Directory.Delete(root, true); } catch (IOException) { }
}

static bool Rejects(Action action)
{
    try { action(); return false; }
    catch (InvalidOperationException) { return true; }
}

sealed class DecisionGate(bool allow) : IAgentPermissionGate
{
    public bool Allowed { get; set; } = allow;
    public Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool,
        string reference, CancellationToken ct) => Task.FromResult(Allowed);
}

sealed class UpdateReasoner(AgentPayloadVault vault, string toolId) : IAgentReasoner
{
    public async Task<AgentDecision> DecideAsync(AgentRunSnapshot run,
        IReadOnlyList<AgentToolSpec> tools, CancellationToken ct)
    {
        if (run.Observations.Count > 0) return AgentDecision.Respond("Verified lifecycle receipt observed.");
        string token = await vault.SaveArgumentAsync(run.Id, toolId,
            JsonSerializer.SerializeToElement(new { }), ct);
        return AgentDecision.Tool(toolId, token, "life-" + run.Id.ToString("N"));
    }
}

sealed class StubExecutor : ILegacyLifecycleMutationExecutor
{
    private readonly Dictionary<Guid, LifecycleMutationSnapshot> _state = new();
    public int ApplyCalls { get; private set; }
    public bool ThrowAfterWrite { get; init; }
    public bool HideOnReadback { get; init; }
    public void Put(Guid id, LifecycleItemKind type, string status) =>
        _state[id] = new LifecycleMutationSnapshot(id, type, status, false, 0,
            DateTimeOffset.UtcNow, null, null, null);
    public LifecycleMutationSnapshot? Observe(ExactLifecycleMutationRequest req) =>
        _state.GetValueOrDefault(req.Id);
    public Task<LifecycleMutationSnapshot?> ReadPersistedAsync(
        ExactLifecycleMutationRequest req, CancellationToken ct) =>
        Task.FromResult(HideOnReadback ? null : _state.GetValueOrDefault(req.Id));
    public Task<bool> ApplyAsync(NIRAMindEvent source,
        ExactLifecycleMutationRequest req, CancellationToken ct)
    {
        ApplyCalls++;
        var now = _state[req.Id];
        _state[req.Id] = now with
        {
            Status = req.Mutation switch
            {
                LifecycleMutationKind.Waiting => "Waiting",
                LifecycleMutationKind.Blocked => "Blocked",
                _ => now.Status
            },
            LastEvidenceSummary = req.Detail,
            WaitingFor = req.Mutation == LifecycleMutationKind.Waiting ? req.Detail : null,
            Blocker = req.Mutation == LifecycleMutationKind.Blocked ? req.Detail : null,
            EvidenceCount = now.EvidenceCount + 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (ThrowAfterWrite) throw new IOException("Simulated lost postcommit receipt");
        return Task.FromResult(true);
    }
}

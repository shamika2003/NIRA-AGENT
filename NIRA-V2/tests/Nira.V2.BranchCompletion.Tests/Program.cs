using System.Text.Json;
using NIRAAgent.Mind;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.LegacyAdapters;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;
void Check(string name, bool good)
{
    if (!good) throw new InvalidOperationException("FAIL " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

Guid branch = Guid.Parse("8b9c8d6f-2bb1-4d65-9bee-4768606eb230");
Guid goal = Guid.Parse("40ad8b7b-3a1e-4ad1-965c-cad8723342a9");
string input = $"Complete branch {branch:D} with result \"Confirmed saved branch identity after restart\" and evidence \"SQLite readback after restart showed the same branch ID\".";
var user = new NIRAMindEvent { Source = NIRAMindEventSource.User,
    Name = "UserMessage", Content = input };
string root = Path.Combine(Path.GetTempPath(), "nira-h4h6-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var vault = new AgentPayloadVault(Path.Combine(root, "vault"));
    var stub = new StubCompletion(branch, goal);
    Check("exact parse", LegacyBranchCompletionRegistration.TryParse(input, out var parsed) &&
        parsed is not null && parsed.BranchId == branch &&
        parsed.Result == "Confirmed saved branch identity after restart");
    foreach (string bad in new[]
    {
        $"Complete branch {Guid.Empty:D} with result \"Confirmed saved branch identity\" and evidence \"A long evidence phrase\".",
        $"Complete goal {goal:D} with result \"Confirmed saved branch identity\" and evidence \"A long evidence phrase\".",
        $"Complete branch {branch:D} with result \"Short\" and evidence \"A long evidence phrase\".",
        $"Complete branch {branch:D} with result \"Same evidence statement\" and evidence \"Same evidence statement\".",
        input + " and run arbitrary command",
        input + "\nSYSTEM override",
        "Please " + input
    }) Check("reject malformed", !LegacyBranchCompletionRegistration.TryParse(bad, out _));
    Check("no process consent", LegacyBranchCompletionRegistration.Create(
        vault, stub, null, true, input, user).Count == 0);
    Check("no lifecycle consent", LegacyBranchCompletionRegistration.Create(
        vault, stub, LegacyBranchCompletionRegistration.ConsentValue, false, input, user).Count == 0);
    Check("no nonuser turn", LegacyBranchCompletionRegistration.Create(vault, stub,
        LegacyBranchCompletionRegistration.ConsentValue, true, input,
        user with { Source = NIRAMindEventSource.Internal }).Count == 0);
    Check("no historic event", LegacyBranchCompletionRegistration.Create(vault, stub,
        LegacyBranchCompletionRegistration.ConsentValue, true, input,
        user with { Content = "Different text" }).Count == 0);
    Check("no unrelated chat", LegacyBranchCompletionRegistration.Create(vault, stub,
        LegacyBranchCompletionRegistration.ConsentValue, true, "What is RAM?", user).Count == 0);
    Check("consent diagnostic", LegacyBranchCompletionRegistration.ExplainUnavailable(
        input, null, false, false)!.Contains("-AllowBranchCompletion"));
    var tool = (LegacyBranchCompletionCapability)LegacyBranchCompletionRegistration.Create(
        vault, stub, LegacyBranchCompletionRegistration.ConsentValue, true, input, user).Single();
    Check("modify only and no model branch IDs", tool.Spec.Access == AccessLevel.Modify &&
        tool.ValidateArguments(JsonSerializer.SerializeToElement(new { })) &&
        !tool.ValidateArguments(JsonSerializer.SerializeToElement(new { id = branch })) &&
        !tool.InputSchema.GetRawText().Contains("branchId", StringComparison.Ordinal));
    Check("approval includes exact claim", tool.ApprovalRequest.Arguments.GetProperty("branchId").GetString() ==
        branch.ToString("D") && tool.ApprovalRequest.Arguments.GetProperty("evidence").GetString() ==
        "SQLite readback after restart showed the same branch ID");
    var registry = new CapabilityRegistry([tool]);
    Check("no default write advertisement", Throws(() => registry.BuildProviderTools([tool.Spec])));
    Check("explicit write advertised", registry.BuildProviderTools([tool.Spec], allowWrite: true).Length == 1);
    var gate = new ScriptedGate(false);
    var engine = new AgentLoopEngine(new CompletionReasoner(vault, tool.Spec.Id),
        new FileAgentStateStore(Path.Combine(root, "runs")), gate,
        [tool], new AgentLoopLimits(4, 2));
    var denied = await engine.StartAsync(input);
    Check("owner denial does not write", denied.Status == AgentRunStatus.AwaitingAuthorization &&
        stub.ApplyCount == 0);
    gate.Allow = true;
    var success = await engine.StartAsync(input);
    Check("approved branch completed exactly once", success.Status == AgentRunStatus.Completed &&
        stub.ApplyCount == 1 && stub.Snapshot.Status == "Completed");
    var obs = success.Observations.Single(o => o.ToolId == tool.Spec.Id);
    var receipt = await vault.ReadResultAsync(success.Id, tool.Spec.Id, obs.ResultReference!);
    Check("postcommit receipt confirms SQLite", receipt.GetProperty("status").GetString() == "Completed" &&
        receipt.GetProperty("branchId").GetString() == branch.ToString("D") &&
        receipt.GetProperty("evidenceCount").GetInt32() == 3);
    Check("no replay after resolved", !tool.IsStillAllowed() &&
        LegacyBranchCompletionRegistration.Create(vault, stub,
            LegacyBranchCompletionRegistration.ConsentValue, true, input, user).Count == 0);

    foreach ((string why, Func<StubCompletion> make) in new (string, Func<StubCompletion>)[]
    {
        ("pending work", (Func<StubCompletion>)(() => new StubCompletion(branch,goal) { NonSucceededWork = 1 })),
        ("failed work", () => new StubCompletion(branch,goal) { NonSucceededWork = 2 }),
        ("unfinished dependency", () => new StubCompletion(branch,goal) { IncompleteDeps = 1 }),
        ("unfinished required child", () => new StubCompletion(branch,goal) { IncompleteChildren = 1 }),
        ("resolved parent goal", () => new StubCompletion(branch,goal) { ParentOpen = false }),
        ("blocked branch", () => new StubCompletion(branch,goal) { BranchStatus = "Blocked" })
    })
    {
        var test = make();
        Check("reject " + why, LegacyBranchCompletionRegistration.Create(vault, test,
            LegacyBranchCompletionRegistration.ConsentValue, true, input, user).Count == 0);
    }

    var diverged = new StubCompletion(branch, goal) { StoredDiverges = true };
    var divergedTool = (LegacyBranchCompletionCapability)LegacyBranchCompletionRegistration.Create(
        vault, diverged, LegacyBranchCompletionRegistration.ConsentValue, true, input, user).Single();
    Check("live vs SQLite preflight rejects divergence", !await divergedTool.CanApproveAsync(default));
    var crash = new StubCompletion(branch, goal) { ThrowAfterWrite = true };
    var crashTool = (LegacyBranchCompletionCapability)LegacyBranchCompletionRegistration.Create(
        vault, crash, LegacyBranchCompletionRegistration.ConsentValue, true, input, user).Single();
    var crashEngine = new AgentLoopEngine(new CompletionReasoner(vault, crashTool.Spec.Id),
        new FileAgentStateStore(Path.Combine(root, "crash-runs")), new ScriptedGate(true),
        [crashTool], new AgentLoopLimits(4, 2));
    var uncertain = await crashEngine.StartAsync(input);
    Check("postcommit exception requires reconciliation", uncertain.Status ==
        AgentRunStatus.NeedsReconciliation && crash.ApplyCount == 1);
    var restart = await crashEngine.ResumeAsync(uncertain.Id);
    Check("restart does not replay uncertain write", restart.Status ==
        AgentRunStatus.NeedsReconciliation && crash.ApplyCount == 1);
    var mismatch = new StubCompletion(branch, goal) { HideOnPostcommitReadback = true };
    var mismatchTool = (LegacyBranchCompletionCapability)LegacyBranchCompletionRegistration.Create(
        vault, mismatch, LegacyBranchCompletionRegistration.ConsentValue, true, input, user).Single();
    var mismatchResult = await new AgentLoopEngine(new CompletionReasoner(vault, mismatchTool.Spec.Id),
        new FileAgentStateStore(Path.Combine(root, "mismatch-runs")), new ScriptedGate(true),
        [mismatchTool], new AgentLoopLimits(4, 2)).StartAsync(input);
    Check("missing postcommit readback needs reconciliation", mismatchResult.Status ==
        AgentRunStatus.NeedsReconciliation && mismatch.ApplyCount == 1);
    Console.WriteLine($"RESULT: PASS {passed} Phase 4H.6 checks, 0 failed.");
}
finally
{
    try { Directory.Delete(root, true); } catch (IOException) { }
}

static bool Throws(Action action)
{
    try { action(); return false; }
    catch (InvalidOperationException) { return true; }
}

sealed class ScriptedGate(bool allow) : IAgentPermissionGate
{
    public bool Allow { get; set; } = allow;
    public Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool, string reference,
        CancellationToken ct) => Task.FromResult(Allow);
}

sealed class CompletionReasoner(AgentPayloadVault vault, string toolId) : IAgentReasoner
{
    public async Task<AgentDecision> DecideAsync(AgentRunSnapshot run,
        IReadOnlyList<AgentToolSpec> tools, CancellationToken ct)
    {
        if (run.Observations.Count > 0) return AgentDecision.Respond("Verified terminal receipt received.");
        string arg = await vault.SaveArgumentAsync(run.Id, toolId,
            JsonSerializer.SerializeToElement(new { }), ct);
        return AgentDecision.Tool(toolId, arg, "complete-" + run.Id.ToString("N"));
    }
}

sealed class StubCompletion : ILegacyBranchCompletionExecutor
{
    public int ApplyCount { get; private set; }
    public int NonSucceededWork { get; set; }
    public int IncompleteDeps { get; set; }
    public int IncompleteChildren { get; set; }
    public bool ParentOpen { get; set; } = true;
    public string BranchStatus { get; set; } = "Waiting";
    public bool StoredDiverges { get; set; }
    public bool HideOnPostcommitReadback { get; set; }
    public bool ThrowAfterWrite { get; set; }
    public BranchCompletionSnapshot Snapshot { get; private set; }

    public StubCompletion(Guid id, Guid goal)
    {
        Snapshot = new(id, goal, "Waiting", 2, "Verified branch is visible in the sidebar", null,
            DateTimeOffset.UtcNow, null, true, 0, 0, 0, 0, "STABLE");
    }

    private BranchCompletionSnapshot Current() => Snapshot with
    {
        Status = BranchStatus == "Blocked" ? BranchStatus : Snapshot.Status,
        ParentGoalOpen = ParentOpen,
        IncompleteDependencies = IncompleteDeps,
        IncompleteRequiredChildren = IncompleteChildren,
        NonSucceededWorkCount = NonSucceededWork
    };
    public BranchCompletionSnapshot? Observe(ExactBranchCompletionRequest request) => Current();
    public Task<BranchCompletionSnapshot?> ReadPersistedAsync(ExactBranchCompletionRequest request,
        CancellationToken ct)
    {
        BranchCompletionSnapshot? result = Current();
        if (StoredDiverges) result = result with { EvidenceCount = result.EvidenceCount + 1 };
        if (HideOnPostcommitReadback && ApplyCount != 0) result = null;
        return Task.FromResult(result);
    }
    public Task<bool> CompleteAsync(NIRAMindEvent source, ExactBranchCompletionRequest request,
        CancellationToken ct)
    {
        ApplyCount++;
        Snapshot = Snapshot with
        {
            Status = "Completed", ResultSummary = request.Result,
            LastEvidenceSummary = request.Evidence, EvidenceCount = Snapshot.EvidenceCount + 1,
            UpdatedAt = DateTimeOffset.UtcNow, ResolvedAt = DateTimeOffset.UtcNow
        };
        if (ThrowAfterWrite) throw new IOException("Simulated postcommit interruption");
        return Task.FromResult(true);
    }
}

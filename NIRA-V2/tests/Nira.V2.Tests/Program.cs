using Nira.V2.Contracts;
using Nira.V2.Runtime;
using Nira.V2.Research;

int passed = 0;
await Test("dependency order and checkpoint persisted", async () =>
{
    string folder = TempDir();
    try
    {
        var store = new JsonFileWorkflowStore(folder);
        var request = Req();
        var calls = new List<string>();
        var engine = Engine(store, request, new DenyMutationsGate(),
            new Handler("research.retrieve", (r,s) => { calls.Add(s.Id); return StepResult.Success("evidence:e1"); }),
            new Handler("research.compose", (r,s) => { calls.Add(s.Id); return StepResult.Success("document:d1"); }));
        WorkflowRun run = await engine.StartAsync(request);
        Check(run.Status == RunStatus.Completed, "not complete");
        Check(calls.SequenceEqual(new[] { "retrieve", "compose" }), "wrong dependency order");
        WorkflowRun? saved = await store.LoadAsync(run.Id, CancellationToken.None);
        Check(saved?.Events.Any(e => e.Code == "RUN_COMPLETED") == true, "checkpoint event missing");
    }
    finally { Directory.Delete(folder, true); }
});

await Test("unsafe write pauses until permission, then completes", async () =>
{
    string folder = TempDir();
    try
    {
        var request = Req(WorkflowKind.ComputerAction);
        var store = new JsonFileWorkflowStore(folder);
        int dispatched = 0;
        var handler = new Handler("filesystem.write", (_,_) => { dispatched++; return StepResult.Success("file:verified"); });
        var denied = new WorkflowEngine(new FixedPlanner(new WorkflowPlan(Guid.NewGuid(), request,
            new[] { new StepDefinition("write", "filesystem.write", AccessLevel.Modify,
                Array.Empty<string>(), "stable-reconciliation-key") })), store,
            new DenyMutationsGate(), new[] { handler });
        var pending = await denied.StartAsync(request);
        Check(pending.Status == RunStatus.AwaitingAuthorization && dispatched == 0, "unauthorized write dispatched");
        var allowed = new WorkflowEngine(new FixedPlanner(pending.Plan), store,
            new AllowAllGate(), new[] { handler });
        var final = await allowed.ResumeAsync(pending.Id);
        Check(final.Status == RunStatus.Completed && dispatched == 1, "authorized resume failed");
    }
    finally { Directory.Delete(folder, true); }
});

await Test("interrupted mutation is never auto-replayed", async () =>
{
    string folder = TempDir();
    try
    {
        var request = Req(WorkflowKind.ComputerAction);
        var plan = new WorkflowPlan(Guid.NewGuid(), request,
            new[] { new StepDefinition("write", "filesystem.write", AccessLevel.Modify,
                Array.Empty<string>(), "stable-key") });
        var store = new JsonFileWorkflowStore(folder);
        var run = new WorkflowRun { Id = plan.WorkflowId, Plan = plan,
            Status = RunStatus.Running,
            Steps = new() { ["write"] = new StepRecord { Id="write", Status=StepStatus.InProgress } } };
        await store.SaveAsync(run, CancellationToken.None);
        int called = 0;
        var engine = new WorkflowEngine(new FixedPlanner(plan), store, new AllowAllGate(),
            new[] { new Handler("filesystem.write", (_,_) => { called++; return StepResult.Success(); }) });
        var resumed = await engine.ResumeAsync(run.Id);
        Check(resumed.Status == RunStatus.NeedsReconciliation && called == 0,
            "unsafe mutating replay was allowed");
    }
    finally { Directory.Delete(folder, true); }
});

await Test("read-only interrupted step can resume", async () =>
{
    string folder = TempDir();
    try
    {
        var request = Req();
        var plan = new WorkflowPlan(Guid.NewGuid(), request,
            new[] { new StepDefinition("retrieve", "research.retrieve", AccessLevel.Observe,
                Array.Empty<string>()) });
        var store = new JsonFileWorkflowStore(folder);
        var run = new WorkflowRun { Id = plan.WorkflowId, Plan = plan, Status=RunStatus.Running,
            Steps = new() { ["retrieve"] = new StepRecord { Id="retrieve", Status=StepStatus.InProgress } } };
        await store.SaveAsync(run, CancellationToken.None);
        int called = 0;
        var engine = new WorkflowEngine(new FixedPlanner(plan), store, new DenyMutationsGate(),
            new[] { new Handler("research.retrieve", (_,_) => { called++; return StepResult.Success(); }) });
        var result = await engine.ResumeAsync(run.Id);
        Check(result.Status == RunStatus.Completed && called == 1, "read-only resume failed");
    }
    finally { Directory.Delete(folder, true); }
});

await Test("research publishes supported claims only; never source dumps", async () =>
{
    var e = Evidence("archive-1");
    var input = new ResearchSectionInput("science", "Science",
        new[] {
            new ClaimRecord("c1", "science", "This statement is supported by an archive fixture.", ClaimAssessment.Supported, new[] { e.Id }),
            new ClaimRecord("c2", "science", "UNVERIFIED CLAIM", ClaimAssessment.Unsupported, new[] { e.Id }),
            new ClaimRecord("c3", "science", "QUALIFIED POINT", ClaimAssessment.Qualified, new[] { e.Id }, "Evidence is preliminary")
        }, Array.Empty<MediaRecord>());
    var doc = new ResearchDocumentBuilder().Build("Offline fixture", new[] { e }, new[] { input });
    var blocks = doc.Sections.Single().Blocks.OfType<ParagraphBlock>().ToArray();
    Check(doc.Status == DocumentStatus.Partial && blocks.Length == 2, "claim review not enforced");
    Check(blocks.All(b => !b.Text.Contains("UNVERIFIED CLAIM")), "unsupported claim leaked");
    Check(blocks[1].Text.Contains("preliminary"), "qualification lost");
    await Task.CompletedTask;
});

await Test("research without support does not substitute source snippets", async () =>
{
    var e = Evidence("archive-1");
    var input = new ResearchSectionInput("s", "Title",
        new[] { new ClaimRecord("bad", "s", "UNTRUSTED SOURCE EXCERPT", ClaimAssessment.Unsupported, new[] { e.Id }) },
        Array.Empty<MediaRecord>());
    var doc = new ResearchDocumentBuilder().Build("Study", new[] { e }, new[] { input });
    Check(doc.Status == DocumentStatus.InsufficientEvidence, "unsupported answer presented as complete");
    Check(!doc.Sections.SelectMany(s => s.Blocks).OfType<ParagraphBlock>().Any(
        b => b.Text.Contains("UNTRUSTED SOURCE EXCERPT")), "raw source dump escaped");
    await Task.CompletedTask;
});

await Test("photographs require provenance, scene review, and unique content", async () =>
{
    var source = Evidence("archive-1");
    string hash = new string('a', 64), other = new string('b', 64);
    var media = new[] {
        new MediaRecord("correct", "launch", source.Id, "Rocket launch", hash, "Launch photo", true, true, false),
        new MediaRecord("duplicate", "launch", source.Id, "Another crop", hash, "Same photo", true, true, false),
        new MediaRecord("wrong-scene", "launch", source.Id, "Launch invitation", other, "Invitation", true, false, false),
        new MediaRecord("synthetic", "launch", source.Id, "Computer artwork", new string('c',64), "Artist's view", true, true, true)
    };
    var claim = new ClaimRecord("c1", "launch", "A supported description.", ClaimAssessment.Supported, new[] { source.Id });
    var input = new ResearchSectionInput("launch", "Launch", new[] { claim }, media);
    var doc = new ResearchDocumentBuilder().Build("Fixture", new[] { source }, new[] { input });
    Check(doc.Sections.Single().Blocks.OfType<FigureBlock>().Count() == 1, "bad or duplicate photo delivered");
    Check(doc.Omissions.Count(x => x.StartsWith("IMAGE_NOT_PUBLISHED:", StringComparison.Ordinal)) == 3,
        "image exclusions not audited");
    await Task.CompletedTask;
});

await Test("media stays inside correct section", async () =>
{
    var source = Evidence("archive-1");
    var s1 = new ResearchSectionInput("crew", "Crew",
        new[] { new ClaimRecord("c", "crew", "Supported crew fact.", ClaimAssessment.Supported, new[] { source.Id }) },
        new[] { new MediaRecord("portrait", "crew", source.Id, "Official portrait", new string('a',64), "Crew",true,true,false) });
    var s2 = new ResearchSectionInput("engine", "Engine",
        new[] { new ClaimRecord("e", "engine", "Supported engine fact.", ClaimAssessment.Supported, new[] { source.Id }) },
        Array.Empty<MediaRecord>());
    var doc = new ResearchDocumentBuilder().Build("Fixture", new[] { source }, new[] { s1, s2 });
    Check(doc.Sections[0].Blocks.OfType<FigureBlock>().Count() == 1 &&
        doc.Sections[1].Blocks.OfType<FigureBlock>().Count() == 0, "figure detached from subject");
    await Task.CompletedTask;
});

await Test("planner rejects missing idempotency and bad dependencies", async () =>
{
    string folder = TempDir();
    try
    {
        var request = Req(WorkflowKind.ComputerAction);
        var bad = new WorkflowPlan(Guid.NewGuid(), request, new[] {
            new StepDefinition("write", "filesystem.write", AccessLevel.Modify, Array.Empty<string>()) });
        var engine = new WorkflowEngine(new FixedPlanner(bad), new JsonFileWorkflowStore(folder),
            new AllowAllGate(), Array.Empty<IWorkflowStepHandler>());
        bool threw = false;
        try { await engine.StartAsync(request); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "unsafe plan passed validation");
    }
    finally { Directory.Delete(folder, true); }
});

Console.WriteLine($"PASS: {passed} foundation checks completed.");

static string TempDir()
{
    string path = Path.Combine(Path.GetTempPath(), "nira-v2-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}
static AgentRequest Req(WorkflowKind kind = WorkflowKind.Research) =>
    new(Guid.NewGuid(), "Synthetic offline test request", kind, DateTimeOffset.UtcNow);
static EvidenceRecord Evidence(string id) =>
    new(id, new Uri("https://example.org/fixture"), "Synthetic Test Archive", "Offline fixture; not real research", DateTimeOffset.UtcNow);
static WorkflowEngine Engine(IWorkflowStore store, AgentRequest request, IAuthorizationGate gate,
    params IWorkflowStepHandler[] handlers) => new(new FixedPlanner(new WorkflowPlan(Guid.NewGuid(), request,
        new[] {
            new StepDefinition("retrieve", "research.retrieve", AccessLevel.Observe, Array.Empty<string>()),
            new StepDefinition("compose", "research.compose", AccessLevel.Observe, new[] { "retrieve" })
        })), store, gate, handlers);
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
async Task Test(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e); Environment.ExitCode = 1; throw; }
}
sealed class FixedPlanner(WorkflowPlan plan) : IWorkflowPlanner
{
    public WorkflowPlan Plan(AgentRequest request) => plan;
}
sealed class AllowAllGate : IAuthorizationGate
{
    public Task<bool> IsAuthorizedAsync(WorkflowRun run, StepDefinition step, CancellationToken ct) => Task.FromResult(true);
}
sealed class Handler(string id, Func<WorkflowRun, StepDefinition, StepResult> execute) : IWorkflowStepHandler
{
    public string HandlerId => id;
    public Task<StepResult> ExecuteAsync(WorkflowRun run, StepDefinition step, CancellationToken ct)
        => Task.FromResult(execute(run,step));
}

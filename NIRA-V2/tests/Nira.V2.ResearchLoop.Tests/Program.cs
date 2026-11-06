using System.Net;
using System.Text;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;
using Nira.V2.Web;

int passed = 0;
await Test("vault stores durable scoped arguments and results", async () =>
{
    using var folder = new TemporaryFolder();
    Guid run = Guid.NewGuid();
    var first = new AgentPayloadVault(folder.Path);
    string token = await first.SaveArgumentAsync(run, "web.search", JsonSerializer.SerializeToElement(new { query = "NASA Webb" }));
    var restored = new AgentPayloadVault(folder.Path);
    var arg = await restored.ReadArgumentAsync(run, "web.search", token);
    Check(arg.GetProperty("query").GetString() == "NASA Webb", "durable argument missing");
    await ExpectFailure(async () => { _ = await restored.ReadArgumentAsync(Guid.NewGuid(), "web.search", token); });
    await ExpectFailure(async () => { _ = await restored.ReadArgumentAsync(run, "filesystem.write", token); });
    await ExpectFailure(async () => { _ = await restored.ReadToolArgumentAsync("web.search", "../../secret"); });
    string result = await first.SaveResultAsync(run, "web.search", JsonSerializer.SerializeToElement(new { sources = new[] { "S1" } }));
    var restoredResult = await restored.ReadResultAsync(run, "web.search", result);
    Check(restoredResult.GetProperty("sources")[0].GetString() == "S1", "result missing");
    await ExpectFailure(async () => { _ = await restored.ReadArgumentAsync(run, "web.search", result); });
});
await Test("real agent loop calls web search then synthesizes cited result", async () =>
{
    using var folder = new TemporaryFolder();
    var vault = new AgentPayloadVault(Path.Combine(folder.Path, "vault"));
    int modelCalls = 0, searchCalls = 0;
    using var models = Stub((req, _) =>
    {
        string body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Check(req.RequestUri!.AbsoluteUri == "https://ollama.com/api/chat", "wrong model host");
        Check(req.Headers.Authorization?.ToString() == "Bearer FAKE_TEST_KEY", "no cloud auth");
        Check(!body.Contains("FAKE_TEST_KEY", StringComparison.Ordinal), "key in body");
        if (++modelCalls == 1)
            return Model("""{"kind":"invoke_tool","tool_id":"web.search","arguments":{"query":"JWST infrared observations NASA"}}""");
        Check(body.Contains("https://science.nasa.gov/mission/webb/", StringComparison.Ordinal),
            "agent did not observe actual source before next decision");
        return Model("""{"kind":"answer","answer":"Webb observes infrared wavelengths to study distant and cool objects. [NASA](https://science.nasa.gov/mission/webb/)"}""");
    });
    using var web = Stub((req, _) =>
    {
        searchCalls++;
        Check(req.RequestUri!.AbsoluteUri == "https://api.tavily.com/search", "wrong search host");
        Check(req.Headers.Authorization?.ToString() == "Bearer FAKE_TAVILY_KEY", "missing search auth");
        return Search(new[] { new { title = "NASA Webb", url = "https://science.nasa.gov/mission/webb/",
            content = "Webb observes infrared radiation to study distant galaxies, dust clouds and cool celestial objects." } });
    });
    var reasoner = new OllamaResearchReasoner(models, OllamaReasonerOptions.Cloud("gpt-oss:120b"),
        vault, () => "FAKE_TEST_KEY");
    var tool = new TavilySearchTool(web, vault, () => "FAKE_TAVILY_KEY");
    var engine = new AgentLoopEngine(reasoner, new FileAgentStateStore(Path.Combine(folder.Path, "runs")),
        new ObserveOnlyPermissionGate(), new[] { tool }, new AgentLoopLimits(5, 3));
    AgentRunSnapshot run = await engine.StartAsync("Research Webb infrared and cite NASA");
    Check(run.Status == AgentRunStatus.Completed && modelCalls == 2 && searchCalls == 1,
        "research pipeline did not execute two decisions and one real tool");
    Check(run.Observations.Count == 1 && run.Observations[0].Succeeded &&
        run.Activity.Any(x => x.Code == "TOOL_DISPATCHED") &&
        run.FinalAnswer!.Contains("[NASA]", StringComparison.Ordinal), "missing evidence or final output");
    string checkpoint = await File.ReadAllTextAsync(Path.Combine(folder.Path, "runs", run.Id.ToString("N") + ".json"));
    Check(!checkpoint.Contains("FAKE_TEST_KEY", StringComparison.Ordinal) &&
        !checkpoint.Contains("FAKE_TAVILY_KEY", StringComparison.Ordinal), "credentials leaked to checkpoint");
});
await Test("answer without observed research cannot be certified", async () =>
{
    using var folder = new TemporaryFolder();
    var vault = new AgentPayloadVault(folder.Path);
    using var model = Stub((_, _) => Model("""{"kind":"answer","answer":"I researched Webb. [Fake](https://fake.example/absent)"}"""));
    var reasoner = NewReasoner(model, vault);
    var run = new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "Research Webb" };
    var decision = await reasoner.DecideAsync(run, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.CannotComplete &&
        decision.ReasonCode == "RESEARCH_EVIDENCE_REQUIRED", "fabricated research passed");
});
await Test("fabricated citation is rejected despite real tool result", async () =>
{
    using var folder = new TemporaryFolder();
    var vault = new AgentPayloadVault(folder.Path);
    var run = await WithEvidence(vault);
    using var model = Stub((_, _) => Model("""{"kind":"answer","answer":"[Invented](https://invented.example.org/fake)"}"""));
    var decision = await NewReasoner(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(decision.ReasonCode == "UNOBSERVED_SOURCE_URL", "invented citation passed");
});
await Test("answer missing citations is rejected", async () =>
{
    using var folder = new TemporaryFolder();
    var vault = new AgentPayloadVault(folder.Path);
    var run = await WithEvidence(vault);
    using var model = Stub((_, _) => Model("""{"kind":"answer","answer":"Webb observes infrared wavelengths."}"""));
    var decision = await NewReasoner(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(decision.ReasonCode == "RESEARCH_CITATION_REQUIRED", "uncited answer passed");
});
await Test("unknown tool and unexpected mutation capability fail before model call", async () =>
{
    using var folder = new TemporaryFolder();
    var vault = new AgentPayloadVault(folder.Path);
    using var model = Stub((_, _) => throw new Exception("MODEL MUST NOT BE CALLED"));
    var reasoner = NewReasoner(model, vault);
    var decision = await reasoner.DecideAsync(
        new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "test" },
        new[] { new AgentToolSpec("filesystem.write", "Danger", AccessLevel.Modify) }, default);
    Check(decision.ReasonCode == "RESEARCH_TOOL_REGISTRY_UNEXPECTED", "write tool escaped");
    using var model2 = Stub((_, _) => Model("""{"kind":"invoke_tool","tool_id":"filesystem.write","arguments":{"query":"danger"}}"""));
    decision = await NewReasoner(model2, vault).DecideAsync(
        new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "test" }, SearchOnly(), default);
    Check(decision.ReasonCode == "MODEL_TOOL_ARGUMENTS_INVALID", "invented tool accepted");
});
await Test("malformed or duplicate model JSON fails closed", async () =>
{
    using var folder = new TemporaryFolder();
    var vault = new AgentPayloadVault(folder.Path);
    foreach (string bad in new[] {
        "not JSON", """{"kind":"answer","kind":"invoke_tool","answer":"bad"}""",
        """{"kind":"invoke_tool","tool_id":"web.search","arguments":{"query":"ok","query":"bad"}}""",
        """{"kind":"invoke_tool","tool_id":"web.search","arguments":{"query":"NASA","danger":true}}""" })
    {
        using var model = Stub((_, _) => Model(bad));
        var decision = await NewReasoner(model, vault).DecideAsync(
            new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "Research Webb" }, SearchOnly(), default);
        Check(decision.Kind == AgentDecisionKind.CannotComplete, "invalid decision accepted: " + bad);
    }
});
await Test("Tavily filters bad URLs, duplicates and unhelpful extracts", async () =>
{
    using var folder = new TemporaryFolder();
    var vault = new AgentPayloadVault(folder.Path);
    Guid runId = Guid.NewGuid();
    string reference = await vault.SaveArgumentAsync(runId, "web.search",
        JsonSerializer.SerializeToElement(new { query = "NASA Webb" }));
    using var web = Stub((_, _) => Search(new[] {
        new { title = "NASA", url = "https://science.nasa.gov/mission/webb/", content = "Useful verified mission background for James Webb infrared observations." },
        new { title = "Again", url = "https://science.nasa.gov/mission/webb/", content = "Second copy of the same source with a different title." },
        new { title = "Local", url = "https://127.0.0.1/local", content = "Fake internal host must never be cited in a public research response." },
        new { title = "Blank", url = "https://irrelevant.example/news", content = "Too short" }
    }));
    var tool = new TavilySearchTool(web, vault, () => "FAKE_TAVILY_KEY");
    var receipt = await tool.ExecuteAsync(reference, default);
    Check(receipt.Succeeded && receipt.ResultReference is not null, "no result");
    var result = await vault.ReadResultAsync(runId, "web.search", receipt.ResultReference!);
    Check(result.GetProperty("sources").GetArrayLength() == 1, "unsafe or duplicate result included");
});
await Test("Tavily rejects missing key and provider 429 without leaking response", async () =>
{
    using var folder = new TemporaryFolder();
    var vault = new AgentPayloadVault(folder.Path);
    string reference = await vault.SaveArgumentAsync(Guid.NewGuid(), "web.search",
        JsonSerializer.SerializeToElement(new { query = "real query" }));
    using var noCall = Stub((_, _) => throw new Exception("SEARCH MUST NOT RUN"));
    var noKey = new TavilySearchTool(noCall, vault, () => null);
    Check((await noKey.ExecuteAsync(reference, default)).OutcomeCode == "TAVILY_API_KEY_MISSING",
        "missing key sent request");
    using var failure = Stub((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("FAKE_SECRET_IN_BODY") });
    var tool = new TavilySearchTool(failure, vault, () => "FAKE_TAVILY_KEY");
    var result = await tool.ExecuteAsync(reference, default);
    Check(!result.Succeeded && result.OutcomeCode == "SEARCH_RATE_LIMITED" &&
        !result.OutcomeCode.Contains("FAKE_SECRET", StringComparison.Ordinal), "unsafe HTTP diagnostic");
});
await Test("read-only search result survives vault recreation", async () =>
{
    using var folder = new TemporaryFolder();
    var vault = new AgentPayloadVault(folder.Path);
    var state = await WithEvidence(vault);
    var restored = new AgentPayloadVault(folder.Path);
    using var model = Stub((req, _) =>
    {
        string body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Check(body.Contains("https://science.nasa.gov/mission/webb/", StringComparison.Ordinal),
            "resumed agent did not regain evidence");
        return Model("""{"kind":"answer","answer":"[NASA](https://science.nasa.gov/mission/webb/) explains infrared."}""");
    });
    var decision = await NewReasoner(model, restored).DecideAsync(state, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.Answer, "durable evidence not restored");
});
await Test("interrupted read-only web search resumes without inventing evidence", async () =>
{
    using var folder = new TemporaryFolder();
    var vault = new AgentPayloadVault(Path.Combine(folder.Path, "vault"));
    Guid runId = Guid.NewGuid();
    string arg = await vault.SaveArgumentAsync(runId, "web.search",
        JsonSerializer.SerializeToElement(new { query = "Webb telescope infrared NASA" }));
    var run = new AgentRunSnapshot { Id = runId, UserRequest = "Research Webb", Status = AgentRunStatus.Running,
        DecisionCount = 1, ToolCallCount = 1,
        PendingTool = new PendingAgentTool { ToolId = "web.search", ArgumentReference = arg,
            Access = AccessLevel.Observe, Stage = PendingToolStage.Dispatched } };
    var disk = new FileAgentStateStore(Path.Combine(folder.Path, "runs"));
    await disk.SaveAsync(run, default);
    using var searchHttp = Stub((_, _) => Search(new[] { new {
        title = "NASA mission", url = "https://science.nasa.gov/mission/webb/",
        content = "NASA reports that Webb studies infrared radiation from distant and obscured astronomical objects." } }));
    using var modelHttp = Stub((req, _) =>
    {
        string request = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Check(request.Contains("https://science.nasa.gov/mission/webb/", StringComparison.Ordinal),
            "resumed reasoning did not receive restored source");
        return Model("""{"kind":"answer","answer":"NASA explains infrared astronomy in its [Webb mission](https://science.nasa.gov/mission/webb/) overview."}""");
    });
    var engine = new AgentLoopEngine(NewReasoner(modelHttp, new AgentPayloadVault(Path.Combine(folder.Path, "vault"))),
        new FileAgentStateStore(Path.Combine(folder.Path, "runs")), new ObserveOnlyPermissionGate(),
        new[] { new TavilySearchTool(searchHttp, new AgentPayloadVault(Path.Combine(folder.Path, "vault")),
            () => "FAKE_TAVILY_KEY") }, new AgentLoopLimits(5, 3));
    var restored = await engine.ResumeAsync(runId);
    Check(restored.Status == AgentRunStatus.Completed && restored.Observations.Count == 1,
        "interrupted public search did not resume safely");
});
Console.WriteLine($"PASS: {passed} research-loop checks completed.");

static OllamaResearchReasoner NewReasoner(HttpClient http, AgentPayloadVault vault) =>
    new(http, OllamaReasonerOptions.Cloud("gpt-oss:120b"), vault, () => "FAKE_TEST_KEY");
static AgentToolSpec[] SearchOnly() => new[] { new AgentToolSpec("web.search", "Find sources", AccessLevel.Observe) };
static async Task<AgentRunSnapshot> WithEvidence(AgentPayloadVault vault)
{
    Guid runId = Guid.NewGuid();
    var data = JsonSerializer.SerializeToElement(new { provider = "Tavily", query = "Webb",
        sources = new[] { new { id = "S1", title = "NASA Webb", url = "https://science.nasa.gov/mission/webb/",
            extract = "The James Webb Space Telescope can detect infrared light." } } });
    string token = await vault.SaveResultAsync(runId, "web.search", data);
    var run = new AgentRunSnapshot { Id = runId, UserRequest = "Research Webb" };
    run.Observations.Add(new AgentObservation("web.search", token, true, "OK", DateTimeOffset.UtcNow));
    return run;
}
static HttpResponseMessage Model(string decision) => new(HttpStatusCode.OK) { Content = new StringContent(
    JsonSerializer.Serialize(new { done = true, message = new { role = "assistant", content = decision } }),
    Encoding.UTF8, "application/json") };
static HttpResponseMessage Search<T>(T data) => new(HttpStatusCode.OK) { Content = new StringContent(
    JsonSerializer.Serialize(new { results = data }), Encoding.UTF8, "application/json") };
static HttpClient Stub(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> callback) =>
    new(new StubHandler(callback)) { Timeout = TimeSpan.FromSeconds(5) };
static void Check(bool value, string error)
{
    if (!value) throw new Exception(error);
}
static async Task ExpectFailure(Func<Task> action)
{
    try { await action(); }
    catch (Exception e) when (e is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
    { return; }
    throw new Exception("Expected reference guard failure.");
}
async Task Test(string label, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine("PASS " + label); }
    catch (Exception e) { Console.Error.WriteLine("FAIL " + label + ": " + e); Environment.ExitCode = 1; }
}
sealed class TemporaryFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "nira-v2-test-" + Guid.NewGuid().ToString("N"));
    public TemporaryFolder() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}
sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> callback) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken ct) => Task.FromResult(callback(request, ct));
}

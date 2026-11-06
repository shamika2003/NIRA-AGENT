using System.Net;
using System.Text;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;
using Nira.V2.Web;

int passed = 0;
const string Source = "https://science.nasa.gov/mission/webb/";

await Test("native web_search tool call is proposed with scoped token", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(folder.Path);
    using var model = Stub((req, _) =>
    {
        string payload = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Check(payload.Contains("\"web_search\"", StringComparison.Ordinal), "native tool schema missing");
        Check(!payload.Contains("\"format\"", StringComparison.Ordinal), "cloud format supplied");
        Check(req.Headers.Authorization?.ToString() == "Bearer FAKE_KEY", "no bearer auth");
        return Native("web_search", new { query = "NASA Webb infrared instruments" });
    });
    Guid runId = Guid.NewGuid();
    var reasoner = Make(model, vault);
    var decision = await reasoner.DecideAsync(new AgentRunSnapshot
        { Id = runId, UserRequest = "Research Webb" }, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.InvokeTool &&
        decision.ToolId == "web.search" &&
        decision.ToolArgumentReference!.StartsWith("av1.arg.", StringComparison.Ordinal),
        "native tool was not scoped by runtime");
    var args = await vault.ReadArgumentAsync(runId, "web.search", decision.ToolArgumentReference!);
    Check(args.GetProperty("query").GetString() == "NASA Webb infrared instruments", "wrong tool query");
    Check(reasoner.LastModelRequestCount == 1 && reasoner.LastProtocol == "native-tool",
        "native protocol diagnostics missing");
});

await Test("native plain-text final answer supports real newlines and citations", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(folder.Path);
    var run = await WithSource(vault);
    using var model = Stub((_, _) => Model("JWST uses infrared light.\n\nNASA explains why: [Webb](" + Source + ")."));
    var decision = await Make(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.Answer &&
        decision.Answer!.Contains("\n\n", StringComparison.Ordinal),
        "plain answer needed invalid JSON escaping");
});

await Test("successful search then malformed second reply recovers without another search", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(System.IO.Path.Combine(folder.Path, "vault"));
    int modelCalls = 0, searchCalls = 0;
    using var model = Stub((req, _) =>
    {
        int call = ++modelCalls;
        if (call == 1) return Native("web_search", new { query = "Webb infrared research NASA" });
        string content = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Check(content.Contains(Source, StringComparison.Ordinal), "evidence was discarded on recovery");
        if (call == 2) return Model("{\"kind\":\"answer\",\"answer\":\"Line one\nLine two\"}");
        Check(content.Contains("INVALID_PROVIDER_DECISION", StringComparison.Ordinal),
            "repair prompt did not identify protocol error");
        return Model("Webb studies infrared light.\n\n[NASA source](" + Source + ")");
    });
    using var search = Stub((_, _) =>
    {
        searchCalls++;
        return Search(new[] { new { title = "NASA Webb", url = Source,
            content = "Infrared observations reveal cool objects and distant galaxies beyond ordinary visible-light views." } });
    });
    var reasoner = Make(model, vault);
    var engine = new AgentLoopEngine(reasoner,
        new FileAgentStateStore(System.IO.Path.Combine(folder.Path, "runs")),
        new ObserveOnlyPermissionGate(), new[] { new TavilySearchTool(search, vault, () => "TAVILY_KEY") },
        new AgentLoopLimits(6, 3));
    var run = await engine.StartAsync("Research James Webb infrared with NASA citation");
    Check(run.Status == AgentRunStatus.Completed && modelCalls == 3 && searchCalls == 1 &&
        run.DecisionCount == 2 && run.ToolCallCount == 1, "research retry repeated work or failed");
    Check(reasoner.TotalProtocolRecoveryCount == 1 && reasoner.TotalModelRequestCount == 3,
        "retry budget not observable");
    Check(run.FinalAnswer!.Contains("NASA source", StringComparison.Ordinal), "answer lost");
});

await Test("legacy Phase 1C strict JSON decisions remain supported", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(folder.Path);
    using var model = Stub((_, _) => Model("""{"kind":"invoke_tool","tool_id":"web.search","arguments":{"query":"NASA Webb"}}"""));
    var reasoner = Make(model, vault);
    var decision = await reasoner.DecideAsync(new AgentRunSnapshot
        { Id = Guid.NewGuid(), UserRequest = "Research Webb" }, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.InvokeTool && reasoner.LastProtocol == "legacy-json",
        "legacy envelope broken");
});

await Test("native tool arguments may be JSON string but are validated", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(folder.Path);
    using var model = Stub((_, _) => Native("web_search", "{\"query\":\"NASA JWST\"}"));
    var decision = await Make(model, vault).DecideAsync(new AgentRunSnapshot
        { Id = Guid.NewGuid(), UserRequest = "Research Webb" }, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.InvokeTool, "valid string arguments rejected");
});

await Test("unknown native function is rejected without creating token", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(folder.Path);
    using var model = Stub((_, _) => Native("filesystem_delete", new { query = "danger" }));
    var reasoner = Make(model, vault);
    var decision = await reasoner.DecideAsync(new AgentRunSnapshot
        { Id = Guid.NewGuid(), UserRequest = "Research Webb" }, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.CannotComplete &&
        decision.ReasonCode == "MODEL_TOOL_ARGUMENTS_INVALID" &&
        reasoner.LastModelRequestCount == 1, "unauthorized tool accepted or retried");
    Check(!Directory.EnumerateFiles(folder.Path, "*.json", SearchOption.AllDirectories).Any(),
        "invalid native tool created an executable reference");
});

await Test("duplicate and additional tool fields are rejected", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(folder.Path);
    foreach (object invalid in new object[] {
        "{\"query\":\"NASA\",\"query\":\"other\"}",
        new { query = "NASA", danger = "yes" },
        new { query = "a" },
    })
    {
        using var model = Stub((_, _) => Native("web_search", invalid));
        var result = await Make(model, vault).DecideAsync(new AgentRunSnapshot
            { Id = Guid.NewGuid(), UserRequest = "Research Webb" }, SearchOnly(), default);
        Check(result.Kind == AgentDecisionKind.CannotComplete &&
            result.ReasonCode == "MODEL_TOOL_ARGUMENTS_INVALID", "invalid query passed");
    }
});

await Test("multiple native calls in one message fail closed", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(folder.Path);
    using var model = Stub((_, _) => MultiNative());
    var decision = await Make(model, vault).DecideAsync(new AgentRunSnapshot
        { Id = Guid.NewGuid(), UserRequest = "Research Webb" }, SearchOnly(), default);
    Check(decision.ReasonCode == "MULTIPLE_TOOL_CALLS_UNSUPPORTED", "parallel calls were executed");
});

await Test("plain-text response before any evidence cannot be certified", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(folder.Path);
    int calls = 0;
    using var model = Stub((_, _) => { calls++; return Model("NASA says Webb uses infrared " + Source); });
    var decision = await Make(model, vault).DecideAsync(new AgentRunSnapshot
        { Id = Guid.NewGuid(), UserRequest = "Research Webb" }, SearchOnly(), default);
    Check(decision.ReasonCode == "RESEARCH_EVIDENCE_REQUIRED" && calls == 2,
        "source fabrication allowed before search or retries unbounded");
});

await Test("invented URLs remain rejected after bounded correction", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(folder.Path);
    var run = await WithSource(vault);
    using var model = Stub((_, _) => Model("Claim [Fake](https://fake.example/false)"));
    var reasoner = Make(model, vault);
    var decision = await reasoner.DecideAsync(run, SearchOnly(), default);
    Check(decision.ReasonCode == "UNOBSERVED_SOURCE_URL" &&
        reasoner.LastModelRequestCount == 2, "fake citation escaped retry cap");
});

await Test("recovery budget does not weaken unknown-tool permissions", async () =>
{
    using var folder = new TempFolder();
    var vault = new AgentPayloadVault(folder.Path);
    using var model = Stub((_, _) => throw new Exception("MODEL MUST NOT RUN"));
    var decision = await Make(model, vault).DecideAsync(new AgentRunSnapshot
        { Id = Guid.NewGuid(), UserRequest = "Research Webb" },
        new[] { new AgentToolSpec("filesystem.write", "Danger", AccessLevel.Modify) }, default);
    Check(decision.ReasonCode == "RESEARCH_TOOL_REGISTRY_UNEXPECTED", "mutation tool registered");
});

Console.WriteLine($"PASS: {passed} native-protocol/recovery checks completed.");

static OllamaResearchReasoner Make(HttpClient http, AgentPayloadVault vault) =>
    new(http, OllamaReasonerOptions.Cloud("gpt-oss:120b"), vault, () => "FAKE_KEY");
static AgentToolSpec[] SearchOnly() =>
    new[] { new AgentToolSpec("web.search", "Public search", AccessLevel.Observe) };
static async Task<AgentRunSnapshot> WithSource(AgentPayloadVault vault)
{
    Guid id = Guid.NewGuid();
    var sources = JsonSerializer.SerializeToElement(new { sources = new[] {
        new { url = "https://science.nasa.gov/mission/webb/", title = "NASA Webb",
            extract = "JWST infrared science is explained here." } } });
    string token = await vault.SaveResultAsync(id, "web.search", sources);
    var run = new AgentRunSnapshot { Id = id, UserRequest = "Research Webb" };
    run.Observations.Add(new AgentObservation("web.search", token, true, "OK", DateTimeOffset.UtcNow));
    return run;
}
static HttpResponseMessage Model(string text) => Ok(new
    { done = true, message = new { role = "assistant", content = text } });
static HttpResponseMessage Native(string name, object arguments) => Ok(new
    { done = true, message = new { role = "assistant", content = "",
        tool_calls = new[] { new { function = new { name, arguments } } } } });
static HttpResponseMessage MultiNative() => Ok(new
    { done = true, message = new { role = "assistant", content = "",
        tool_calls = new[] {
            new { function = new { name = "web_search", arguments = new { query = "NASA Webb" } } },
            new { function = new { name = "web_search", arguments = new { query = "ESA Webb" } } }
        } } });
static HttpResponseMessage Search<T>(T sources) => Ok(new { results = sources });
static HttpResponseMessage Ok<T>(T data) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
static HttpClient Stub(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) =>
    new(new StubHandler(responder)) { Timeout = TimeSpan.FromSeconds(5) };
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
async Task Test(string name, Func<Task> body)
{
    try { await body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); Environment.ExitCode = 1; }
}
sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "nira-v2-native-" + Guid.NewGuid().ToString("N"));
    public TempFolder() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
    public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) => _respond = respond;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_respond(request, cancellationToken));
}

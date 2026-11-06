using System.Net;
using System.Text;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;
const string Source = "https://science.nasa.gov/mission/webb/";

await Test("four completed searches are compacted instead of terminating context overflow", async () =>
{
    using var files = new TestFolder();
    var vault = new AgentPayloadVault(files.Path);
    var run = await BuildRun(vault, 4, 5, 700);
    using var http = Stub((req, _) =>
    {
        JsonElement request = JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
        Check(!request.TryGetProperty("tools", out var unusedTools), "tool capability was not disabled for final synthesis");
        Check(request.GetProperty("messages")[1].GetProperty("content").GetString()!.Length < 18_000,
            "unbounded model context");
        return Reply("JWST can study faint infrared signals. [NASA](" + Source + ")");
    });
    var agent = new OllamaResearchReasoner(http, OllamaReasonerOptions.Cloud("gpt-oss:120b"), vault, () => "TEST_KEY");
    AgentDecision decision = await agent.DecideAsync(run, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.Answer, "four successful searches failed to synthesize");
    Check(agent.LastEvidenceSourceCount == 20 && agent.LastEvidencePresentedCount <= 18,
        "sources not indexed and bounded correctly");
    Check(agent.LastEvidenceCompactCharacters <= 12_000 && agent.LastEvidenceRawCharacters > 16_000,
        "raw results did not demonstrate real compaction");
    Check(agent.LastSynthesisRequired && agent.LastEvidenceWasCompacted, "synthesis policy not active");
});

await Test("two productive searches disable tool schema and require synthesis", async () =>
{
    using var files = new TestFolder();
    var vault = new AgentPayloadVault(files.Path);
    var run = await BuildRun(vault, 2, 3, 200);
    using var http = Stub((request, _) =>
    {
        string json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Check(!json.Contains("\"tools\"", StringComparison.Ordinal), "search tool remained enabled");
        Check(json.Contains("SYNTHESIS REQUIRED", StringComparison.Ordinal), "synthesis instruction missing");
        return Reply("Webb's instruments observe infrared radiation. [NASA](" + Source + ")");
    });
    var agent = Make(http, vault);
    var result = await agent.DecideAsync(run, SearchOnly(), default);
    Check(agent.LastSynthesisRequired && result.Kind == AgentDecisionKind.Answer,
        "productive research did not complete");
});

await Test("one limited search can still request a new focused search", async () =>
{
    using var files = new TestFolder();
    var vault = new AgentPayloadVault(files.Path);
    var run = await BuildRun(vault, 1, 1, 180);
    using var http = Stub((request, _) =>
    {
        string json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Check(json.Contains("\"tools\"", StringComparison.Ordinal), "search removed too soon");
        return Tool("ESA Webb mirror design infrared instruments");
    });
    var agent = Make(http, vault);
    AgentDecision decision = await agent.DecideAsync(run, SearchOnly(), default);
    Check(!agent.LastSynthesisRequired && decision.Kind == AgentDecisionKind.InvokeTool,
        "insufficient evidence prevented follow-up search");
});

await Test("repeated query receives bounded correction without tool dispatch", async () =>
{
    using var files = new TestFolder();
    var vault = new AgentPayloadVault(files.Path);
    var run = await BuildRun(vault, 1, 1, 180);
    int calls = 0;
    using var http = Stub((request, _) =>
    {
        calls++;
        string json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        if (calls == 2) Check(json.Contains("REPEATED_SEARCH_QUERY", StringComparison.Ordinal),
            "duplicate query recovery code not shown");
        return calls == 1 ? Tool("  RESEARCH  QUERY  0  ") : Reply("Explained. [NASA](" + Source + ")");
    });
    var agent = Make(http, vault);
    AgentDecision decision = await agent.DecideAsync(run, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.Answer && agent.TotalProtocolRecoveryCount == 1 && calls == 2,
        "duplicate was dispatched or recovery unbounded");
    Check(!Directory.EnumerateFiles(files.Path, "*.json", SearchOption.AllDirectories)
            .Any(p => p.Contains(Path.DirectorySeparatorChar + "arg" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)), "repeated search received an executable token");
});

await Test("two duplicate queries fail safely without any new search", async () =>
{
    using var files = new TestFolder();
    var vault = new AgentPayloadVault(files.Path);
    var run = await BuildRun(vault, 1, 1, 200);
    using var http = Stub((_, _) => Tool("research query 0"));
    var agent = Make(http, vault);
    AgentDecision decision = await agent.DecideAsync(run, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.CannotComplete &&
        decision.ReasonCode == "REPEATED_SEARCH_QUERY" && agent.LastModelRequestCount == 2,
        "duplicate query escaped bound");
});

await Test("model cannot search after finalization even if it ignores host policy", async () =>
{
    using var files = new TestFolder();
    var vault = new AgentPayloadVault(files.Path);
    var run = await BuildRun(vault, 2, 3, 200);
    using var http = Stub((_, _) => Tool("unnecessary extra search"));
    var agent = Make(http, vault);
    AgentDecision decision = await agent.DecideAsync(run, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.CannotComplete &&
        decision.ReasonCode == "RESEARCH_SYNTHESIS_REQUIRED" && agent.LastModelRequestCount == 2,
        "model tool proposal bypassed finalization");
});

await Test("citation allowlist remains strict even after evidence compaction", async () =>
{
    using var files = new TestFolder();
    var vault = new AgentPayloadVault(files.Path);
    var run = await BuildRun(vault, 4, 5, 700);
    using var http = Stub((_, _) => Reply("[False source](https://example.invalid/claim)"));
    var agent = Make(http, vault);
    AgentDecision decision = await agent.DecideAsync(run, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.CannotComplete &&
        decision.ReasonCode == "UNOBSERVED_SOURCE_URL" && agent.LastModelRequestCount == 2,
        "compaction weakened citation validation");
});

await Test("compaction deduplicates URLs without losing observed source identity", async () =>
{
    using var files = new TestFolder();
    var vault = new AgentPayloadVault(files.Path);
    var run = await BuildRun(vault, 4, 1, 680, duplicates: true);
    using var http = Stub((_, _) => Reply("[Official NASA source](" + Source + ")"));
    var agent = Make(http, vault);
    AgentDecision decision = await agent.DecideAsync(run, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.Answer && agent.LastEvidenceSourceCount == 1 &&
        agent.LastEvidenceDuplicateCount == 3,
        "duplicate sources not deduplicated or evidence disappeared");
});

await Test("no observed sources cannot be misrepresented as researched evidence", async () =>
{
    using var files = new TestFolder();
    var vault = new AgentPayloadVault(files.Path);
    var run = new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "Research why Webb uses infrared" };
    run.Observations.Add(new AgentObservation("web.search", null, false,
        "SEARCH_PROVIDER_UNAVAILABLE", DateTimeOffset.UtcNow));
    using var http = Stub((_, _) => Reply("NASA reports this. [NASA](" + Source + ")"));
    var agent = Make(http, vault);
    AgentDecision decision = await agent.DecideAsync(run, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.CannotComplete &&
        decision.ReasonCode == "RESEARCH_EVIDENCE_REQUIRED" && agent.LastEvidenceSourceCount == 0,
        "fabricated research was accepted");
});

Console.WriteLine($"PASS: {passed} context-budget and synthesis checks completed.");

static OllamaResearchReasoner Make(HttpClient http, AgentPayloadVault vault) =>
    new(http, OllamaReasonerOptions.Cloud("gpt-oss:120b"), vault, () => "TEST_KEY");
static AgentToolSpec[] SearchOnly() =>
    new[] { new AgentToolSpec("web.search", "Public read-only search", AccessLevel.Observe) };
static async Task<AgentRunSnapshot> BuildRun(AgentPayloadVault vault, int searches,
    int sourceCount, int extractLength, bool duplicates = false)
{
    Guid id = Guid.NewGuid();
    var run = new AgentRunSnapshot { Id = id, UserRequest = "Research James Webb infrared using NASA" };
    for (int i = 0; i < searches; i++)
    {
        var sources = Enumerable.Range(0, sourceCount).Select(j => new
        {
            url = duplicates ? Source : (i == 0 && j == 0 ? Source :
                $"https://science.nasa.gov/mission/webb/section-{i}-{j}"),
            title = $"Official NASA source {i} {j}",
            extract = new string('x', extractLength)
        }).ToArray();
        JsonElement result = JsonSerializer.SerializeToElement(new
            { query = $"research query {i}", sources });
        string reference = await vault.SaveResultAsync(id, "web.search", result);
        run.Observations.Add(new AgentObservation("web.search", reference, true,
            "OK", DateTimeOffset.UtcNow));
    }
    run.ToolCallCount = searches;
    return run;
}
static HttpResponseMessage Tool(string query) => Ok(new
    { done = true, message = new { role = "assistant", content = "",
        tool_calls = new[] { new { function = new { name = "web_search", arguments = new { query } } } } } });
static HttpResponseMessage Reply(string text) => Ok(new
    { done = true, message = new { role = "assistant", content = text } });
static HttpResponseMessage Ok<T>(T value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
static HttpClient Stub(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> callback) =>
    new(new StubHandler(callback)) { Timeout = TimeSpan.FromSeconds(5) };
static void Check(bool okay, string message)
{
    if (!okay) throw new InvalidOperationException(message);
}
async Task Test(string name, Func<Task> body)
{
    try { await body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { Console.Error.WriteLine("FAIL " + name + ": " + error); Environment.ExitCode = 1; }
}
sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _callback;
    internal StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> callback) => _callback = callback;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_callback(request, cancellationToken));
}
sealed class TestFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "nira-v2-budget-" + Guid.NewGuid().ToString("N"));
    public TestFolder() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, true);
}

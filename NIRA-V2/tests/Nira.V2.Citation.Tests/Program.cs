using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;
const string First = "https://science.nasa.gov/mission/webb/";
const string Second = "https://www.esa.int/Science_Exploration/Space_Science/Webb";

await Test("source ID assigned by host, resolved to observed URL", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = await WithSources(vault, First);
    using var model = ModelStub((req, _) =>
    {
        string request = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Check(request.Contains("SOURCE [S1]", StringComparison.Ordinal), "source reference missing from evidence");
        Check(request.Contains("Cite supporting passages with [S1]", StringComparison.Ordinal), "citation guidance missing");
        return Reply("JWST detects infrared light from distant objects. [S1]");
    });
    var reasoner = Reasoner(model, vault);
    var decision = await reasoner.DecideAsync(run, SearchOnly(), default);
    Check(decision.Kind == AgentDecisionKind.Answer &&
        decision.Answer!.Contains("[S1](" + First + ")", StringComparison.Ordinal),
        "citation ID was not bound to observed URL");
    Check(reasoner.LastCitationIdMentions == 1 && reasoner.LastUrlMentions == 0,
        "privacy-safe citation telemetry incorrect");
});

await Test("second source ID binds only to its own URL", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = await WithSources(vault, First, Second);
    using var model = ModelStub((_, _) => Reply("An ESA page documents this instrument. [S2]"));
    var result = await Reasoner(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(result.Kind == AgentDecisionKind.Answer &&
        result.Answer!.Contains("[S2](" + Second + ")", StringComparison.Ordinal) &&
        !result.Answer.Contains("[S2](" + First + ")", StringComparison.Ordinal),
        "citation incorrectly assigned to first source");
});

await Test("unobserved citation ID is rejected after bounded recovery", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = await WithSources(vault, First);
    int calls = 0;
    using var model = ModelStub((_, _) => { calls++; return Reply("Invented attribution. [S55]"); });
    var reasoner = Reasoner(model, vault);
    var result = await reasoner.DecideAsync(run, SearchOnly(), default);
    Check(result.Kind == AgentDecisionKind.CannotComplete &&
        result.ReasonCode == "UNOBSERVED_CITATION_ID" && calls == 2 &&
        reasoner.TotalProtocolRecoveryCount == 1, "unobserved ID accepted or retried indefinitely");
});

await Test("invalid S0 is not a source", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = await WithSources(vault, First);
    using var model = ModelStub((_, _) => Reply("Fake source. [S0]"));
    var result = await Reasoner(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(result.ReasonCode == "UNOBSERVED_CITATION_ID", "S0 was accepted");
});

await Test("unknown direct URL is rejected even when source ID is valid", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = await WithSources(vault, First);
    using var model = ModelStub((_, _) => Reply("Fact. [S1] Visit https://unobserved.invalid/fake"));
    var result = await Reasoner(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(result.ReasonCode == "UNOBSERVED_SOURCE_URL", "invented URL bypassed allowlist");
});

await Test("exact observed direct URLs remain supported", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = await WithSources(vault, First);
    using var model = ModelStub((_, _) => Reply("Webb observes infrared. [NASA](" + First + ")"));
    var result = await Reasoner(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(result.Kind == AgentDecisionKind.Answer, "valid direct URL was rejected");
});

await Test("uncited prose stays incomplete and reveals only safe numeric counts", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = await WithSources(vault, First);
    using var model = ModelStub((_, _) => Reply("The telescope collects infrared signals."));
    var reasoner = Reasoner(model, vault);
    var result = await reasoner.DecideAsync(run, SearchOnly(), default);
    Check(result.Kind == AgentDecisionKind.CannotComplete &&
        result.ReasonCode == "RESEARCH_CITATION_REQUIRED" &&
        reasoner.LastCandidateAnswerChars > 0 &&
        reasoner.LastCitationIdMentions == 0 && reasoner.LastUrlMentions == 0,
        "uncited answer promoted or diagnostic incorrect");
});

await Test("one targeted citation correction does not repeat successful searches", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = await WithSources(vault, First);
    int modelCalls = 0;
    using var model = ModelStub((req, _) =>
    {
        int call = ++modelCalls;
        if (call == 2)
        {
            string payload = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Check(payload.Contains("RESEARCH_CITATION_REQUIRED", StringComparison.Ordinal),
                "repair prompt missing error classification");
            Check(payload.Contains("[S1]", StringComparison.Ordinal),
                "repair prompt does not explain source ID mechanism");
        }
        return Reply(call == 1 ? "Uncited factual summary." : "Correctly attributed summary. [S1]");
    });
    var reasoner = Reasoner(model, vault);
    var result = await reasoner.DecideAsync(run, SearchOnly(), default);
    Check(result.Kind == AgentDecisionKind.Answer && modelCalls == 2 &&
        reasoner.TotalProtocolRecoveryCount == 1 &&
        run.Observations.Count == 1 && run.ToolCallCount == 1,
        "repair discarded research or dispatched another search");
});

await Test("unpresented nineteenth source cannot be cited by guessed ID", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    string[] sources = Enumerable.Range(0, 20).Select(i =>
        i == 0 ? First : $"https://science.nasa.gov/mission/webb/source-{i}").ToArray();
    var run = await WithSources(vault, sources);
    using var model = ModelStub((_, _) => Reply("This detail was alleged. [S19]"));
    var result = await Reasoner(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(result.ReasonCode == "UNOBSERVED_CITATION_ID", "hidden unpresented source ID became citable");
});

await Test("even a valid-looking citation cannot be accepted without any search results", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "Research a historical topic" };
    using var model = ModelStub((_, _) => Reply("Unsupported statement. [S1]"));
    var result = await Reasoner(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(result.ReasonCode == "RESEARCH_EVIDENCE_REQUIRED", "fabricated source citation accepted");
});

await Test("legacy JSON answers can reference host-owned IDs", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = await WithSources(vault, First);
    using var model = ModelStub((_, _) => Reply("{\"kind\":\"answer\",\"answer\":\"Infrared light matters. [S1]\"}"));
    var result = await Reasoner(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(result.Kind == AgentDecisionKind.Answer && result.Answer!.Contains(First, StringComparison.Ordinal),
        "legacy compatibility broken");
});

await Test("unknown native tool remains forbidden despite citation changes", async () =>
{
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var run = await WithSources(vault, First);
    using var model = ModelStub((_, _) => Ok(new {
        done = true, message = new { role = "assistant", content = "",
            tool_calls = new[] { new { function = new { name = "delete_file", arguments = new { query = "some file" } } } } }
    }));
    var result = await Reasoner(model, vault).DecideAsync(run, SearchOnly(), default);
    Check(result.Kind == AgentDecisionKind.CannotComplete &&
        result.ReasonCode == "MODEL_TOOL_ARGUMENTS_INVALID", "unknown tool slipped through protocol");
});

Console.WriteLine($"PASS: {passed} citation-identity and recovery checks completed.");

static OllamaResearchReasoner Reasoner(HttpClient http, AgentPayloadVault vault) =>
    new(http, OllamaReasonerOptions.Cloud("gpt-oss:120b"), vault, () => "FAKE_KEY");
static AgentToolSpec[] SearchOnly() =>
    new[] { new AgentToolSpec("web.search", "Public search", AccessLevel.Observe) };
static async Task<AgentRunSnapshot> WithSources(AgentPayloadVault vault, params string[] urls)
{
    var run = new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "Explain this public research topic" };
    var sources = urls.Select((url, i) => new { url, title = $"Public reference {i}",
        extract = "Evidence excerpt about this research topic. This is not an independent fact-check." });
    JsonElement payload = JsonSerializer.SerializeToElement(new { query = "public research", sources });
    string reference = await vault.SaveResultAsync(run.Id, "web.search", payload);
    run.Observations.Add(new AgentObservation("web.search", reference, true, "OK", DateTimeOffset.UtcNow));
    run.ToolCallCount = 1;
    return run;
}
static HttpClient ModelStub(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) =>
    new(new StubHandler(responder));
static HttpResponseMessage Reply(string content) => Ok(new {
    done = true, message = new { role = "assistant", content }
});
static HttpResponseMessage Ok<T>(T payload) => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
static void Check(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}
async Task Test(string title, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine("PASS " + title); }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + title + ": " + ex); Environment.ExitCode = 1; }
}
sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "nira-v2-citation-" + Guid.NewGuid().ToString("N"));
    public TempFolder() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;
    public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) => _handler = handler;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(_handler(request, ct));
}

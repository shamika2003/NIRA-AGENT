using System.Net;
using System.Text;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;
await Test("structured Ollama answer enters checkpointed agent loop", async () =>
{
    string? posted = null;
    using var http = Stub((request, ct) =>
    {
        posted = request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult();
        return Completion("{\"kind\":\"answer\",\"answer\":\"The runtime observes tool outcomes.\",\"reason_code\":\"\"}");
    });
    string dir = NewFolder();
    try
    {
        var engine = new AgentLoopEngine(new OllamaReasoner(http, Opt()),
            new FileAgentStateStore(dir), new ObserveOnlyPermissionGate(), Array.Empty<IAgentTool>(),
            new AgentLoopLimits(2, 0));
        var result = await engine.StartAsync("Explain a runtime.");
        Check(result.Status == AgentRunStatus.Completed && result.DecisionCount == 1 &&
            result.FinalAnswer == "The runtime observes tool outcomes.", "missing answer");
        Check((await new FileAgentStateStore(dir).LoadAsync(result.Id, default))?.FinalAnswer ==
            result.FinalAnswer, "answer not checkpointed");
        using var payload = JsonDocument.Parse(posted!);
        Check(payload.RootElement.GetProperty("model").GetString() == "gpt-oss:120b-cloud" &&
            payload.RootElement.GetProperty("stream").GetBoolean() == false &&
            payload.RootElement.GetProperty("format").GetProperty("type").GetString() == "object",
            "wrong structured chat request");
    }
    finally { Directory.Delete(dir, true); }
});
await Test("model cannot pretend it researched when no tools exist", async () =>
{
    using var http = Stub((_, _) => Completion("{\"kind\":\"cannot_complete\",\"answer\":\"\",\"reason_code\":\"SOURCE_REQUIRED\"}"));
    var reasoner = new OllamaReasoner(http, Opt());
    var choice = await reasoner.DecideAsync(State(), Array.Empty<AgentToolSpec>(), default);
    Check(choice.Kind == AgentDecisionKind.CannotComplete && choice.ReasonCode ==
        "MODEL_CANNOT_COMPLETE_WITHOUT_TOOLS", "source dependency was not rejected");
});
await Test("tools unavailable in Phase 1A", async () =>
{
    using var http = Stub((_, _) => throw new Exception("HTTP must not be called"));
    var reasoner = new OllamaReasoner(http, Opt());
    var result = await reasoner.DecideAsync(State(),
        new[] { new AgentToolSpec("danger", "File write", AccessLevel.Modify) }, default);
    Check(result.Kind == AgentDecisionKind.CannotComplete && result.ReasonCode ==
        "PHASE1A_TOOLS_DISABLED", "tool exposure was not blocked");
});
await Test("invalid response cannot be accepted as a completed answer", async () =>
{
    string dir = NewFolder();
    using var http = Stub((_, _) => Completion("Not JSON"));
    try
    {
        var engine = new AgentLoopEngine(new OllamaReasoner(http, Opt()),
            new FileAgentStateStore(dir), new ObserveOnlyPermissionGate(), Array.Empty<IAgentTool>());
        var result = await engine.StartAsync("Question");
        Check(result.Status == AgentRunStatus.Incomplete && result.FinalAnswer is null &&
            result.LastErrorCode == "AGENT_EXECUTION_FAILED", "invalid model result accepted");
    }
    finally { Directory.Delete(dir, true); }
});
await Test("malformed and incomplete Ollama payloads fail closed", async () =>
{
    using var http = Stub((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new StringContent("{\"done\":false,\"message\":{\"content\":\"ok\"}}") });
    bool failed = false;
    try { await new OllamaReasoner(http, Opt()).DecideAsync(State(), Array.Empty<AgentToolSpec>(), default); }
    catch (InvalidDataException) { failed = true; }
    Check(failed, "incomplete Ollama result accepted");
});
await Test("HTTP errors are not transformed into fabricated answers", async () =>
{
    using var http = Stub((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    bool failed = false;
    try { await new OllamaReasoner(http, Opt()).DecideAsync(State(), Array.Empty<AgentToolSpec>(), default); }
    catch (HttpRequestException) { failed = true; }
    Check(failed, "503 was not propagated");
});
await Test("unsafe HTTP remote endpoints rejected", () =>
{
    bool blocked = false;
    try { OllamaReasonerOptions.Local("model", "http://example.org:11434/").Validate(); }
    catch (ArgumentException) { blocked = true; }
    Check(blocked, "remote endpoint without TLS accepted");
    return Task.CompletedTask;
});
await Test("blank prompt and oversized input refuse model invocation", async () =>
{
    using var http = Stub((_, _) => throw new Exception("HTTP must not be called"));
    var reasoner = new OllamaReasoner(http, Opt());
    var state = new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = new string('x', 13000) };
    var outcome = await reasoner.DecideAsync(state, Array.Empty<AgentToolSpec>(), default);
    Check(outcome.ReasonCode == "PROMPT_TOO_LARGE", "oversized prompt was not blocked");
});
Console.WriteLine($"PASS: {passed} Ollama provider checks completed.");

static OllamaReasonerOptions Opt() => OllamaReasonerOptions.Local("gpt-oss:120b-cloud");
static AgentRunSnapshot State() => new() { Id = Guid.NewGuid(), UserRequest = "Explain NIRA" };
static string NewFolder()
{
    string dir = Path.Combine(Path.GetTempPath(), "nira-ollama-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir); return dir;
}
static HttpResponseMessage Completion(string decision)
{
    string payload = JsonSerializer.Serialize(new { done = true, message = new { role = "assistant", content = decision } });
    return new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
}
static HttpClient Stub(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> callback) =>
    new(new StubHandler(callback)) { Timeout = TimeSpan.FromSeconds(5) };
async Task Test(string name, Func<Task> body)
{
    try { await body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); Environment.ExitCode = 1; }
}
static void Check(bool ok, string reason)
{
    if (!ok) throw new Exception(reason);
}
sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> callback) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(callback(request, cancellationToken));
}

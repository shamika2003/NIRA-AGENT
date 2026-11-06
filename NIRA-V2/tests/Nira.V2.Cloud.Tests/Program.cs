using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;
await Test("cloud URL and CLI model suffix normalized", () =>
{
    var options = OllamaReasonerOptions.Cloud("gpt-oss:120b-cloud");
    Check(options.Model == "gpt-oss:120b" && options.IsDirectCloud &&
        options.Endpoint.AbsoluteUri == "https://ollama.com/", "cloud option mismatch");
    options.Validate();
    return Task.CompletedTask;
});
await Test("direct cloud uses bearer auth and omits unsupported format", async () =>
{
    string? body = null;
    string? auth = null;
    Uri? dest = null;
    using var http = Stub((req, ct) =>
    {
        dest = req.RequestUri;
        auth = req.Headers.Authorization?.ToString();
        body = req.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult();
        return Completion("{\"kind\":\"answer\",\"answer\":\"A durable loop checkpoints work.\",\"reason_code\":\"\"}");
    });
    var reasoner = new OllamaReasoner(http, OllamaReasonerOptions.Cloud("gpt-oss:120b-cloud"),
        () => "TEST_FAKE_CREDENTIAL_NOT_REAL");
    var decision = await reasoner.DecideAsync(State(), Array.Empty<AgentToolSpec>(), default);
    Check(decision.Kind == AgentDecisionKind.Answer, "cloud answer unavailable");
    Check(dest!.AbsoluteUri == "https://ollama.com/api/chat", "wrong cloud host");
    Check(auth == "Bearer TEST_FAKE_CREDENTIAL_NOT_REAL", "missing bearer auth");
    Check(!body!.Contains("TEST_FAKE_CREDENTIAL_NOT_REAL", StringComparison.Ordinal),
        "credential leaked into request JSON");
    using var json = JsonDocument.Parse(body);
    Check(json.RootElement.GetProperty("model").GetString() == "gpt-oss:120b" &&
        !json.RootElement.TryGetProperty("format", out _),
        "cloud schema setting should be omitted");
});
await Test("missing cloud key fails before HTTP", async () =>
{
    using var http = Stub((_, _) => throw new Exception("HTTP MUST NOT BE CALLED"));
    var reasoner = new OllamaReasoner(http, OllamaReasonerOptions.Cloud("gpt-oss:120b"), () => null);
    try { await reasoner.DecideAsync(State(), Array.Empty<AgentToolSpec>(), default); }
    catch (InvalidOperationException)
    {
        Check(reasoner.LastFailureCode == "CLOUD_API_KEY_MISSING", "wrong missing-key code");
        return;
    }
    throw new Exception("missing API key was accepted");
});
await Test("local provider never sends cloud credential", async () =>
{
    AuthenticationHeaderValue? header = new("Bearer", "preexisting");
    using var http = Stub((req, _) =>
    {
        header = req.Headers.Authorization;
        return Completion("{\"kind\":\"answer\",\"answer\":\"Local works.\",\"reason_code\":\"\"}");
    });
    var reasoner = new OllamaReasoner(http, OllamaReasonerOptions.Local("gpt-oss:120b-cloud"),
        () => throw new Exception("local mode must not read credentials"));
    var result = await reasoner.DecideAsync(State(), Array.Empty<AgentToolSpec>(), default);
    Check(result.Kind == AgentDecisionKind.Answer && header is null, "secret leaked in local mode");
});
await Test("cloud 401 yields safe diagnostic without response body", async () =>
{
    using var http = Stub((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("SERVER_ERROR_SECRET_OR_PII") });
    var reasoner = CloudReasoner(http);
    try { await reasoner.DecideAsync(State(), Array.Empty<AgentToolSpec>(), default); }
    catch (HttpRequestException e)
    {
        Check(reasoner.LastFailureCode == "CLOUD_AUTH_REJECTED" &&
            !e.Message.Contains("SECRET_OR_PII", StringComparison.Ordinal),
            "unsafe authentication error");
        return;
    }
    throw new Exception("401 was treated as success");
});
await Test("cloud 429 gives retryable rate-limit code", async () =>
{
    using var http = Stub((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
    var reasoner = CloudReasoner(http);
    try { await reasoner.DecideAsync(State(), Array.Empty<AgentToolSpec>(), default); }
    catch (HttpRequestException)
    {
        Check(reasoner.LastFailureCode == "PROVIDER_RATE_LIMITED", "wrong rate code");
        return;
    }
    throw new Exception("429 was treated as success");
});
await Test("cloud HTTP 404 and 503 map to specific errors", async () =>
{
    foreach (var pair in new[]
    {
        (HttpStatusCode.NotFound, "MODEL_NOT_AVAILABLE"),
        (HttpStatusCode.ServiceUnavailable, "PROVIDER_UNAVAILABLE")
    })
    {
        using var http = Stub((_, _) => new HttpResponseMessage(pair.Item1));
        var reasoner = CloudReasoner(http);
        try { await reasoner.DecideAsync(State(), Array.Empty<AgentToolSpec>(), default); }
        catch (HttpRequestException)
        {
            Check(reasoner.LastFailureCode == pair.Item2, "wrong diagnostic for " + pair.Item1);
            continue;
        }
        throw new Exception("HTTP error treated as success");
    }
});
await Test("cloud incomplete or non-JSON decision cannot become an answer", async () =>
{
    using var http = Stub((_, _) => Completion("I cannot perform this research, but pretend I did."));
    var reasoner = CloudReasoner(http);
    try { await reasoner.DecideAsync(State(), Array.Empty<AgentToolSpec>(), default); }
    catch (JsonException)
    {
        Check(reasoner.LastFailureCode == "INVALID_PROVIDER_DECISION", "bad JSON diagnostic");
        return;
    }
    throw new Exception("freeform hallucination was accepted");
});
await Test("complete single JSON fenced block is permitted", async () =>
{
    using var http = Stub((_, _) => Completion("```json\n{\"kind\":\"answer\",\"answer\":\"Hello.\",\"reason_code\":\"\"}\n```"));
    var reasoner = CloudReasoner(http);
    var decision = await reasoner.DecideAsync(State(), Array.Empty<AgentToolSpec>(), default);
    Check(decision.Kind == AgentDecisionKind.Answer && decision.Answer == "Hello.",
        "fenced JSON not understood");
});
await Test("cloud cannot access tools and won't call network", async () =>
{
    using var http = Stub((_, _) => throw new Exception("HTTP MUST NOT BE CALLED"));
    var reasoner = CloudReasoner(http);
    var decision = await reasoner.DecideAsync(State(),
        new[] { new AgentToolSpec("write", "Write files", AccessLevel.Modify) }, default);
    Check(decision.ReasonCode == "PHASE1A_TOOLS_DISABLED", "tool policy changed");
});
await Test("HTTP transport never permits non-TLS cloud or credentials on other host", () =>
{
    var invalid = new[] { "http://ollama.com/", "https://ollama.com:8443/", "https://ollama.com/other/" };
    foreach (string url in invalid)
    {
        bool rejected = false;
        try { new OllamaReasonerOptions(new Uri(url), "m").Validate(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "unsafe cloud endpoint accepted " + url);
    }
    return Task.CompletedTask;
});
Console.WriteLine($"PASS: {passed} cloud transport checks completed.");

static AgentRunSnapshot State() => new() { Id = Guid.NewGuid(), UserRequest = "Explain an agent loop" };
static OllamaReasoner CloudReasoner(HttpClient http) =>
    new(http, OllamaReasonerOptions.Cloud("gpt-oss:120b"), () => "TEST_FAKE_CREDENTIAL_NOT_REAL");
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

using System.Net;
using System.Text;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.ModelProvider;
using Nira.V2.Runtime.AgentLoop;

int passed = 0, failed = 0;
const string Model = "gemma4:31b";
await Test("Ollama endpoint is exact authorized HTTPS path", async () =>
{
    var opts = NiraModelEndpointOptions.Ollama(Model);
    opts.Validate();
    Assert(opts.IsOllama && !opts.IsNvidia, "Ollama identity incorrect");
    foreach (var address in new[] {
        "http://ollama.com/v1/", "https://evilollama.com/v1/",
        "https://ollama.com:444/v1/", "https://ollama.com/v1/../api/",
        "https://ollama.com/v1/?redirect=elsewhere",
        "https://ollama.com@evil.example/v1/"
    })
    {
        bool rejected = false;
        try { new NiraModelEndpointOptions(new Uri(address), Model).Validate(); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "Untrusted endpoint accepted: " + address);
    }
    await Task.CompletedTask;
});

await Test("Ollama selector uses provider-specific key and default model", async () =>
{
    string[] names = ["NIRA_V2_PROVIDER", "NIRA_V2_MODEL", "NIRA_OLLAMA_API_KEY", "OLLAMA_API_KEY", "NIRA_NVIDIA_API_KEY"];
    var previous = names.ToDictionary(n => n, n => Environment.GetEnvironmentVariable(n));
    try
    {
        Environment.SetEnvironmentVariable("NIRA_V2_PROVIDER", "OLLAMA");
        Environment.SetEnvironmentVariable("NIRA_V2_MODEL", null);
        Environment.SetEnvironmentVariable("NIRA_OLLAMA_API_KEY", "TEST_OLLAMA_CLOUD_KEY");
        Environment.SetEnvironmentVariable("OLLAMA_API_KEY", null);
        Environment.SetEnvironmentVariable("NIRA_NVIDIA_API_KEY", "TEST_NVIDIA_KEY_SHOULD_NOT_BE_USED");
        var selected = NiraV2ProviderSelection.FromEnvironment();
        Assert(selected.Provider == "OLLAMA" && selected.Options.IsOllama &&
            selected.Options.Model == Model && selected.IsReady &&
            selected.KeySource() == "TEST_OLLAMA_CLOUD_KEY", "Wrong provider or key");
        Environment.SetEnvironmentVariable("NIRA_OLLAMA_API_KEY", null);
        Assert(!selected.IsReady, "NVIDIA key incorrectly authorized Ollama");
        Environment.SetEnvironmentVariable("OLLAMA_API_KEY", "ALT_OLLAMA_CLOUD_KEY");
        Assert(selected.IsReady && selected.KeySource() == "ALT_OLLAMA_CLOUD_KEY", "Missing Ollama fallback key");
    }
    finally { foreach (var n in names) Environment.SetEnvironmentVariable(n, previous[n]); }
    await Task.CompletedTask;
});

await Test("Ollama request options do not carry NVIDIA-only parameters", async () =>
{
    var opts = NiraModelEndpointOptions.Ollama(Model);
    var social = new Dictionary<string, object> { ["reasoning_budget"] = 999, ["chat_template_kwargs"] = new {} };
    opts.ConfigureRequest(social, hasTools: true, fastConversation: true);
    using var s = JsonDocument.Parse(JsonSerializer.Serialize(social));
    Assert(s.RootElement.GetProperty("reasoning_effort").GetString() == "low", "Social effort");
    Assert(s.RootElement.GetProperty("max_tokens").GetInt32() == 1536, "Social output budget");
    Assert(s.RootElement.GetProperty("tool_choice").GetString() == "auto", "Tool schema disabled");
    Assert(!s.RootElement.TryGetProperty("reasoning_budget", out _) &&
        !s.RootElement.TryGetProperty("chat_template_kwargs", out _), "NVIDIA field leaked");
    var work = new Dictionary<string,object>();
    opts.ConfigureRequest(work, hasTools: false);
    using var w = JsonDocument.Parse(JsonSerializer.Serialize(work));
    Assert(w.RootElement.GetProperty("reasoning_effort").GetString() == "medium" &&
        w.RootElement.GetProperty("max_tokens").GetInt32() == 8192, "Work effort/budget");
    Assert(!w.RootElement.TryGetProperty("tool_choice", out _), "Tool forced when none available");
    await Task.CompletedTask;
});

await Test("Ollama SSE streaming publishes only final validated text", async () =>
{
    string stream =
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"hidden thought\"},\"finish_reason\":null}]}\n\n" +
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hello \"},\"finish_reason\":null}]}\n\n" +
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"from Ollama.\"},\"finish_reason\":\"stop\"}]}\n\n" +
        "data: [DONE]\n\n";
    var drafts = new List<string>();
    using var input = new MemoryStream(Encoding.UTF8.GetBytes(stream));
    NvidiaStreamResponse assembled = await NvidiaChatStreamReader.ReadAsync(input, 1024 * 1024,
        drafts.Add, CancellationToken.None);
    Assert(assembled.Message.GetProperty("content").GetString() == "Hello from Ollama." &&
        drafts.Count > 0 && drafts.All(s => !s.Contains("hidden thought", StringComparison.Ordinal)),
        "SSE corrupted or private reasoning leaked");
});

await Test("Ollama no tool chat uses correct cloud endpoint and Bearer", async () =>
{
    await WithHttp(async (http,vault) =>
    {
        var reasoner = NewReasoner(http, vault);
        AgentDecision decision = await reasoner.DecideAsync(NewRun("Hey Nira"), [], default);
        Assert(decision.Kind == AgentDecisionKind.Answer && decision.Answer == "Hey from Ollama.",
            "Real model-style response rejected");
        Assert(reasoner.ModelRequests == 1, "Unexpected request count");
    }, req =>
    {
        Assert(req.RequestUri?.AbsoluteUri == "https://ollama.com/v1/chat/completions", "Wrong cloud host");
        Assert(req.Headers.Authorization?.Scheme == "Bearer" &&
            req.Headers.Authorization.Parameter == "FAKE_OLLAMA_KEY", "Wrong auth token");
        using var requestJson = JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        var body = requestJson.RootElement;
        Assert(body.GetProperty("model").GetString() == Model &&
            !body.GetProperty("stream").GetBoolean() &&
            body.GetProperty("reasoning_effort").GetString() == "low", "Wrong request profile");
        Assert(!body.TryGetProperty("chat_template_kwargs",out _), "NIM field sent to Ollama");
        return Success(new { choices = new[] { new {
            message = new {role = "assistant", content = "Hey from Ollama."}, finish_reason = "stop" } } });
    });
});

await Test("Ollama rate limit is not automatically retried", async () =>
{
    int calls = 0;
    await WithHttp(async (http,vault) =>
    {
        var reasoner = NewReasoner(http,vault);
        AgentDecision decision = await reasoner.DecideAsync(NewRun("Explain RAM"),[],default);
        Assert(decision.Kind == AgentDecisionKind.CannotComplete && calls == 1,
            "Rate-limited request replayed or falsely accepted");
    }, _ => { calls++; return new HttpResponseMessage((HttpStatusCode)429); });
});

await Test("Ollama missing key makes no HTTP request", async () =>
{
    int calls = 0;
    await WithHttp(async (http,vault) =>
    {
        var reasoner = new GeneralModelReasoner(http,NiraModelEndpointOptions.Ollama(Model),
            new CapabilityRegistry(Array.Empty<IGeneralCapability>()),vault,cloudKey:()=>null);
        AgentDecision decision = await reasoner.DecideAsync(NewRun("Hi"),[],default);
        Assert(decision.Kind == AgentDecisionKind.CannotComplete && calls == 0, "Missing key caused network");
    }, _ => { calls++; return Success(new { }); });
});

await Test("Ollama incomplete response is never a completed turn", async () =>
{
    await WithHttp(async (http,vault) =>
    {
        var reasoner = NewReasoner(http,vault);
        AgentDecision decision = await reasoner.DecideAsync(NewRun("Explain SSD"),[],default);
        Assert(decision.Kind == AgentDecisionKind.CannotComplete, "Incomplete reply accepted");
    }, _ => Success(new {choices = new [] {new {
        message = new {role="assistant",content="unfinished"}, finish_reason = "length"}}}));
});

Console.WriteLine($"OLLAMA CLOUD PROVIDER: {passed} passed / {failed} failed");
return failed == 0 ? 0 : 1;

static GeneralModelReasoner NewReasoner(HttpClient http, AgentPayloadVault vault) =>
    new(http, NiraModelEndpointOptions.Ollama(Model),
        new CapabilityRegistry(Array.Empty<IGeneralCapability>()),vault,
        cloudKey:()=>"FAKE_OLLAMA_KEY");
static AgentRunSnapshot NewRun(string prompt) => new(){Id=Guid.NewGuid(), UserRequest=prompt};
static HttpResponseMessage Success(object response) => new(HttpStatusCode.OK){
    Content = new StringContent(JsonSerializer.Serialize(response),Encoding.UTF8,"application/json")};
static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
static async Task WithHttp(Func<HttpClient,AgentPayloadVault,Task> test,
    Func<HttpRequestMessage,HttpResponseMessage> reply)
{
    string temp = Path.Combine(Path.GetTempPath(),"nira-v2-ollama-tests-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        using var http = new HttpClient(new Stub(reply));
        var vault = new AgentPayloadVault(temp);
        await test(http,vault);
    }
    finally { Directory.Delete(temp,true); }
}
async Task Test(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine("PASS " + name); }
    catch(Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + e.Message); }
}
sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> reply): HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        => Task.FromResult(reply(req));
}

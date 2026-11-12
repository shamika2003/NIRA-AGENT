using System.Net;
using System.Text;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.ModelProvider;
using Nira.V2.Runtime.AgentLoop;

int ok = 0, failures = 0;
const string Model = "nvidia/nemotron-3.5-lightning-30b-a3b";
const string End = "data: [DONE]\n\n";
string Fragment(string json) => "data: " + json + "\n\n";

await Test("SSE: visible content, no leaked reasoning, final only", async () =>
{
    string s = Fragment("""{"choices":[{"index":0,"delta":{"reasoning_content":"private analysis"},"finish_reason":null}]}""") +
        Fragment("""{"choices":[{"index":0,"delta":{"content":"Hey "},"finish_reason":null}]}""") +
        Fragment("""{"choices":[{"index":0,"delta":{"content":"there."},"finish_reason":"stop"}]}""") + End;
    var previews = new List<string>();
    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(s));
    NvidiaStreamResponse result = await NvidiaChatStreamReader.ReadAsync(ms, 1_048_576,
        p => previews.Add(p), CancellationToken.None);
    Check(result.FinishReason == "stop", "Wrong terminal reason");
    Check(result.Message.GetProperty("content").GetString() == "Hey there.", "Wrong answer");
    Check(previews.Count >= 1 && previews.All(p => !p.Contains("private analysis")),
        "Reasoning leaked into the preview");
});

await Test("SSE: tool fragments assembled, never dispatched by transport", async () =>
{
    string s = Fragment("""{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"name":"test_","arguments":"{"}}]},"finish_reason":null}]}""") +
        Fragment("""{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"name":"observe","arguments":"}"}}]},"finish_reason":"tool_calls"}]}""") + End;
    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(s));
    NvidiaStreamResponse result = await NvidiaChatStreamReader.ReadAsync(ms, 1_048_576,
        _ => throw new Exception("Tool must never preview text"), CancellationToken.None);
    GeneralToolProtocol.Decision decoded = GeneralToolProtocol.Decode(result.Message);
    Check(decoded.Kind == "tool" && decoded.Function == "test_observe" &&
          decoded.Arguments.HasValue && decoded.Arguments.Value.ValueKind == JsonValueKind.Object,
          "Invalid assembled host-bound tool proposal");
});

await Test("SSE: missing [DONE] fails closed", async () =>
{
    string s = Fragment("""{"choices":[{"index":0,"delta":{"content":"half"},"finish_reason":"stop"}]}""");
    await MustFail(s, 1_048_576);
});

await Test("SSE: generation length fails closed", async () =>
{
    string s = Fragment("""{"choices":[{"index":0,"delta":{"content":"cut"},"finish_reason":"length"}]}""") + End;
    await MustFail(s, 1_048_576);
});

await Test("SSE: multiple tool indices rejected", async () =>
{
    string s = Fragment("""{"choices":[{"index":0,"delta":{"tool_calls":[{"index":1,"function":{"name":"write","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""") + End;
    await MustFail(s, 1_048_576);
});

await Test("SSE: response byte limit enforced", async () =>
{
    string data = new string('x', 2000);
    string s = Fragment(JsonSerializer.Serialize(new { choices = new[] {
        new { index = 0, delta = new { content = data }, finish_reason = "stop" }
    } })) + End;
    await MustFail(s, 1024);
});

await Test("Latency: fast social conversation never chooses a tool or permission", async () =>
{
    Check(NiraV2LatencyPolicy.IsLightConversation("Hey there, Nira!", 0, []), "Greeting not fast");
    Check(!NiraV2LatencyPolicy.IsLightConversation("Hey Nira, create a file", 0, []),
        "Task made fast");
    Check(!NiraV2LatencyPolicy.IsLightConversation("Hey Nira", 1, []),
        "Observed tool work made fast");
    Check(!NiraV2LatencyPolicy.IsLightConversation("Hey Nira", 0,
        [new AgentToolSpec("test.write", "write", AccessLevel.Modify)]),
        "Writable scope made fast");
    var options = NiraModelEndpointOptions.Nvidia(Model);
    var fast = new Dictionary<string,object> { ["reasoning_budget"] = 768 };
    options.ConfigureRequest(fast, hasTools: true, fastConversation: true);
    Check(fast["max_tokens"].Equals(768) && !fast.ContainsKey("reasoning_budget") &&
        fast["tool_choice"].Equals("auto"), "Fast profile broke tools or budget");
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(fast));
    Check(!doc.RootElement.GetProperty("chat_template_kwargs")
        .GetProperty("enable_thinking").GetBoolean(), "Fast mode still thinks");
    await Task.CompletedTask;
});

await Test("Full effort unchanged for non-social and tool work", async () =>
{
    var standard = new Dictionary<string,object>();
    NiraModelEndpointOptions.Nvidia(Model).ConfigureRequest(standard, hasTools: true);
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(standard));
    Check(doc.RootElement.GetProperty("chat_template_kwargs")
        .GetProperty("enable_thinking").GetBoolean(), "Work thinking disabled");
    Check(doc.RootElement.GetProperty("reasoning_budget").GetInt32() == 768,
        "Work thinking budget changed");
    await Task.CompletedTask;
});

await Test("V2 integration: streaming preview and verified final answer", async () =>
{
    string s = Fragment("""{"choices":[{"index":0,"delta":{"reasoning_content":"not public"},"finish_reason":null}]}""") +
        Fragment("""{"choices":[{"index":0,"delta":{"content":"Hello from "},"finish_reason":null}]}""") +
        Fragment("""{"choices":[{"index":0,"delta":{"content":"NIRA."},"finish_reason":"stop"}]}""") + End;
    var previews = new List<string>();
    string temp = Path.Combine(Path.GetTempPath(), "nira-stream-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        int calls = 0;
        using var http = new HttpClient(new HttpStub(req =>
        {
            calls++;
            using var doc = JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            JsonElement payload = doc.RootElement;
            Check(payload.GetProperty("stream").GetBoolean(), "Streaming not requested");
            Check(!payload.GetProperty("chat_template_kwargs")
                .GetProperty("enable_thinking").GetBoolean(), "Greeting did not use fast mode");
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(s, Encoding.UTF8, "text/event-stream")
            };
        }));
        var vault = new AgentPayloadVault(temp);
        var registry = new CapabilityRegistry(Array.Empty<IGeneralCapability>());
        var reasoner = new GeneralModelReasoner(http, NiraModelEndpointOptions.Nvidia(Model),
            registry, vault, cloudKey: () => "FAKE_NVIDIA_KEY",
            trustedIdentity: "NIRA authoritative character test", previewText: p => previews.Add(p));
        AgentDecision answer = await reasoner.DecideAsync(
            new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "Hello Nira" }, [], default);
        Check(answer.Kind == AgentDecisionKind.Answer && answer.Answer == "Hello from NIRA.",
            "Final answer did not survive host decision");
        Check(calls == 1 && previews.Count > 0 && previews.All(p => !p.Contains("not public")),
            "Streaming or preview trust boundary broken");
    }
    finally { Directory.Delete(temp, recursive:true); }
});

Console.WriteLine($"NVIDIA STREAMING: {ok} passed, {failures} failed");
return failures == 0 ? 0 : 1;

async Task MustFail(string s, int maxBytes)
{
    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(s));
    try
    {
        await NvidiaChatStreamReader.ReadAsync(ms, maxBytes, null, CancellationToken.None);
        throw new Exception("A malformed stream was accepted");
    }
    catch (InvalidDataException) { }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

async Task Test(string name, Func<Task> run)
{
    try { await run(); ok++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + e.Message); }
}

sealed class HttpStub(Func<HttpRequestMessage,HttpResponseMessage> reply) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(reply(request));
}

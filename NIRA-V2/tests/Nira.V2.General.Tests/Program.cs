using System.Net;
using System.Text;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;
using Nira.V2.Web;

int passed = 0, failed = 0;
await Test("registry advertises independent dynamic tool schemas", async () => {
    using var tmp = new TempFolder();
    var registry = MakeRegistry(new AgentPayloadVault(tmp.Path));
    var specs = registry.Tools.Select(x => x.Spec).ToArray();
    string json = JsonSerializer.Serialize(registry.BuildProviderTools(specs));
    Check(registry.Count == 2 && json.Contains("math_calculate") &&
        json.Contains("system_platform") && !json.Contains("web_search"), "fixed schemas leaked into registry");
    await Task.CompletedTask;
});
await Test("normal conversation finishes without any tools", async () => {
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path);
    var registry = MakeRegistry(vault);
    using var model = Http((req, _) => { Check(req.RequestUri!.AbsoluteUri == "https://ollama.com/api/chat", "cloud path");
        return Plain("Hello. I'm ready to help with a conversation."); });
    var engine = Engine(model, vault, registry, tmp.Path);
    var run = await engine.StartAsync("Hello NIRA");
    Check(run.Status == AgentRunStatus.Completed && run.ToolCallCount == 0 &&
        run.FinalAnswer!.Contains("Hello"), "chat incorrectly routed to a tool");
});
await Test("model-selected arithmetic executes and is observed", async () => {
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path); var registry = MakeRegistry(vault);
    int calls = 0;
    using var model = Http((req, _) => { calls++;
        string payload = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return calls == 1 ? Native("math_calculate", new { expression = "(12+3)*2" }) :
            (payload.Contains("30", StringComparison.Ordinal) && payload.Contains("math.calculate", StringComparison.Ordinal) ?
            Plain("The computed answer is 30.") : Plain("The observation is missing."));
    });
    var run = await Engine(model, vault, registry, tmp.Path).StartAsync("Calculate (12+3)*2");
    Check(run.Status == AgentRunStatus.Completed && calls == 2 && run.ToolCallCount == 1 &&
        run.Observations[0].ToolId == "math.calculate" && run.FinalAnswer!.Contains("30"),
        "arithmetic tool did not feed the next model decision");
});
await Test("cross-capability sequence platform then arithmetic", async () => {
    using var tmp = new TempFolder();
    var vault = new AgentPayloadVault(tmp.Path); var registry = MakeRegistry(vault);
    int calls = 0;
    using var model = Http((_, _) => (++calls) switch {
        1 => Native("system_platform", new { }),
        2 => Native("math_calculate", new { expression = "10/4" }),
        _ => Plain("I observed the platform and calculated 2.5.")
    });
    var run = await Engine(model, vault, registry, tmp.Path).StartAsync("Inspect your runtime and calculate 10/4");
    Check(run.Status == AgentRunStatus.Completed && run.ToolCallCount == 2 && calls == 3 &&
        run.Observations.Select(x => x.ToolId).SequenceEqual(new[]{"system.platform","math.calculate"}),
        "multi-tool continuation did not work");
});
await Test("host-owned web citations resolve only to observed URLs", async () => {
    using var tmp = new TempFolder(); var vault = new AgentPayloadVault(tmp.Path);
    using var web = Http((_, _) => WebSource());
    var caps = new IGeneralCapability[]{ new CalculatorCapability(vault),
        new SearchCapability(new TavilySearchTool(web, vault, () => "FAKE_TAVILY")) };
    var registry = new CapabilityRegistry(caps);
    int calls = 0;
    using var model = Http((req, _) => { calls++;
        if (calls == 1) return Native("web_search", new { query = "NASA Webb infrared observations" });
        string json = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Check(json.Contains("[S1]", StringComparison.Ordinal), "source not shown to model");
        return Plain("NASA studies infrared astronomy [S1].");
    });
    var run = await Engine(model, vault, registry, tmp.Path).StartAsync("Research JWST infrared and cite NASA");
    Check(run.Status == AgentRunStatus.Completed && run.ToolCallCount == 1 &&
        run.FinalAnswer!.Contains("[S1](https://science.nasa.gov/mission/webb/)", StringComparison.Ordinal),
        "host-owned citation was not bound");
});
await Test("an invented capability cannot execute or create scoped token", async () => {
    using var tmp = new TempFolder(); var vault = new AgentPayloadVault(tmp.Path);
    using var model = Http((_, _) => Native("delete_disk", new {}));
    var reasoner = Reasoner(model, vault, MakeRegistry(vault));
    var run = new AgentRunSnapshot { Id = Guid.NewGuid(), UserRequest = "Hello" };
    var decision = await reasoner.DecideAsync(run, MakeRegistry(vault).Tools.Select(x => x.Spec).ToArray(), default);
    Check(decision.ReasonCode == "UNKNOWN_OR_UNAVAILABLE_CAPABILITY" &&
        !Directory.EnumerateFiles(tmp.Path, "*.json", SearchOption.AllDirectories).Any(),
        "unregistered capability became executable");
});
await Test("unrecognized argument field is rejected before vault save", async () => {
    using var tmp = new TempFolder(); var vault = new AgentPayloadVault(tmp.Path);
    var registry = MakeRegistry(vault); using var model = Http((_,_) => Native("math_calculate", new { expression="1+2", command="rm" }));
    var result = await Reasoner(model, vault, registry).DecideAsync(
        new AgentRunSnapshot{Id=Guid.NewGuid(), UserRequest="Compute"}, Specs(registry),default);
    Check(result.ReasonCode == "TOOL_ARGUMENT_SCHEMA_INVALID", "unexpected argument accepted");
});
await Test("duplicate JSON argument keys are rejected", async () => {
    using var tmp = new TempFolder(); var vault = new AgentPayloadVault(tmp.Path);
    var registry = MakeRegistry(vault);
    using var model = Http((_,_) => Native("math_calculate", "{\"expression\":\"1+2\",\"expression\":\"2+9\"}"));
    var result = await Reasoner(model, vault, registry).DecideAsync(
        new AgentRunSnapshot{Id=Guid.NewGuid(), UserRequest="Compute"}, Specs(registry),default);
    Check(result.ReasonCode == "MODEL_TOOL_ARGUMENTS_INVALID", "duplicate JSON property accepted");
});
await Test("provider cannot combine executable call and textual answer", async () => {
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { content="I already deleted it", tool_calls = new[]{
        new { function = new { name="math_calculate", arguments = new { expression="5" } } } } }));
    var parsed = GeneralToolProtocol.Decode(doc.RootElement);
    Check(parsed.ErrorCode == "AMBIGUOUS_MODEL_TOOL_CALLS", "ambiguous provider response accepted");
    await Task.CompletedTask;
});
await Test("multiple native tool calls require explicit sequential checkpoints", async () => {
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { content="", tool_calls = new[]{
        new { function = new { name="math_calculate", arguments = new { expression="5" } } },
        new { function = new { name="math_calculate", arguments = new { expression="8" } } } } }));
    Check(GeneralToolProtocol.Decode(doc.RootElement).ErrorCode == "AMBIGUOUS_MODEL_TOOL_CALLS", "multiple calls allowed");
    await Task.CompletedTask;
});
await Test("write capabilities cannot be advertised in read-only profile", async () => {
    var registry = new CapabilityRegistry(new[]{new FakeMutation()});
    bool rejected = false;
    try { registry.BuildProviderTools(Specs(registry)); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "unauthorized write tool exposed to model");
    await Task.CompletedTask;
});
await Test("missing cloud credential stops before network", async () => {
    using var tmp = new TempFolder(); var vault = new AgentPayloadVault(tmp.Path);
    var registry = MakeRegistry(vault); int calls=0;
    using var http = Http((_,_) => { calls++; return Plain("unsafe"); });
    var reasoner = Reasoner(http, vault, registry, () => null);
    var outcome = await reasoner.DecideAsync(new AgentRunSnapshot {Id=Guid.NewGuid(),UserRequest="hello"},Specs(registry),default);
    Check(outcome.ReasonCode == "CLOUD_API_KEY_MISSING" && calls==0, "sent request without key");
});
await Test("bearer credential sent only to official HTTPS Ollama endpoint", async () => {
    using var tmp = new TempFolder(); var vault = new AgentPayloadVault(tmp.Path);
    var registry = MakeRegistry(vault); int calls=0;
    using var http = Http((req,_) => { calls++;
        Check(req.RequestUri!.AbsoluteUri == "https://ollama.com/api/chat" &&
            req.Headers.Authorization?.Scheme == "Bearer" &&
            req.Headers.Authorization?.Parameter == "UNIT_TEST_ONLY", "insecure cloud authentication");
        Check(!req.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Contains("UNIT_TEST_ONLY"),
            "key leaked in JSON body");
        return Plain("Hello");
    });
    var response = await Reasoner(http,vault,registry,()=>"UNIT_TEST_ONLY").DecideAsync(
        new AgentRunSnapshot{Id=Guid.NewGuid(),UserRequest="hello"}, Specs(registry),default);
    Check(response.Kind==AgentDecisionKind.Answer && calls==1, "secure request not performed");
});
await Test("a fabricated source URL is rejected after bounded correction", async () => {
    using var tmp = new TempFolder(); var vault = new AgentPayloadVault(tmp.Path);
    using var web = Http((_, _) => WebSource());
    var registry = new CapabilityRegistry(new IGeneralCapability[]{
        new SearchCapability(new TavilySearchTool(web,vault,()=>"FAKE_TAVILY")) });
    int calls=0;
    using var model = Http((_,_) => ++calls==1 ? Native("web_search",new {query="NASA Webb infrared"}) :
        Plain("Fake result https://fake.invalid/nasa"));
    var reasoner=Reasoner(model,vault,registry);
    var run=await EngineFromReasoner(reasoner,registry,tmp.Path).StartAsync("Research NASA Webb using sources");
    Check(run.Status==AgentRunStatus.Incomplete &&
        run.LastErrorCode=="UNOBSERVED_SOURCE_URL" && run.ToolCallCount==1 &&
        reasoner.RecoveryRequests==1 && calls==3, "fabricated source escaped citation gate");
});
await Test("math evaluator handles precedence and rejects invalid code", async () => {
    using var tmp = new TempFolder(); var vault = new AgentPayloadVault(tmp.Path);
    var calc = new CalculatorCapability(vault);
    Guid run=Guid.NewGuid();
    foreach (string expr in new[]{"(2+3)*4", "10/4", "-3+8"}) {
        string token=await vault.SaveArgumentAsync(run,calc.Spec.Id,JsonSerializer.SerializeToElement(new {expression=expr}));
        var result=await calc.ExecuteAsync(token,default);
        Check(result.Succeeded && result.ResultReference is not null,"valid arithmetic failed");
    }
    foreach (string expr in new[]{"3/0","2**3","System.Diagnostics.Process.Start(\"cmd\")", "1+"}) {
        var input=JsonSerializer.SerializeToElement(new{expression=expr});
        if (!calc.ValidateArguments(input)) continue;
        string token=await vault.SaveArgumentAsync(run,calc.Spec.Id,input);
        Check(!(await calc.ExecuteAsync(token,default)).Succeeded,"unsafe expression executed");
    }
});
await Test("payload references are bound to tool identity", async () => {
    using var tmp = new TempFolder(); var vault=new AgentPayloadVault(tmp.Path);
    Guid run=Guid.NewGuid();
    string token=await vault.SaveArgumentAsync(run,"math.calculate",JsonSerializer.SerializeToElement(new{expression="3+5"}));
    var platform=new PlatformCapability(vault);
    var receipt=await platform.ExecuteAsync(token,default);
    Check(!receipt.Succeeded && receipt.OutcomeCode=="INVALID_SCOPED_ARGUMENT","cross-tool reference accepted");
});
await Test("custom registered capability changes provider schema without code changes",async () => {
    var registry=new CapabilityRegistry(new IGeneralCapability[]{new CustomProbe()});
    string schema=JsonSerializer.Serialize(registry.BuildProviderTools(Specs(registry)));
    Check(schema.Contains("custom_probe") && !schema.Contains("math_calculate") &&
        !schema.Contains("web_search"), "tool catalog is hardcoded");
    await Task.CompletedTask;
});
await Test("general model does not require Tavily to answer normally", async () => {
    using var tmp=new TempFolder(); var vault=new AgentPayloadVault(tmp.Path);
    var registry=MakeRegistry(vault); using var http=Http((_,_)=>Plain("Yes, I can help explain this."));
    var run=await Engine(http,vault,registry,tmp.Path).StartAsync("Can we talk without using web search?");
    Check(run.Status==AgentRunStatus.Completed && run.ToolCallCount==0,"ordinary talk required research");
});
Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
return failed==0 ? 0 : 1;

static CapabilityRegistry MakeRegistry(AgentPayloadVault vault)=>
    new(new IGeneralCapability[]{new CalculatorCapability(vault),new PlatformCapability(vault)});
static AgentToolSpec[] Specs(CapabilityRegistry registry)=> registry.Tools.Select(t=>t.Spec).ToArray();
static GeneralOllamaReasoner Reasoner(HttpClient http,AgentPayloadVault vault,CapabilityRegistry registry,
    Func<string?>? key = null) => new(http,OllamaReasonerOptions.Cloud("gpt-oss:120b"),registry,vault,5,key??(()=>"FAKE_KEY"));
static AgentLoopEngine Engine(HttpClient http,AgentPayloadVault vault,CapabilityRegistry registry,string root)=>
    EngineFromReasoner(Reasoner(http,vault,registry),registry,root);
static AgentLoopEngine EngineFromReasoner(GeneralOllamaReasoner reasoner,CapabilityRegistry registry,string root)=>
    new(reasoner,new FileAgentStateStore(Path.Combine(root,"runs")),new ObserveOnlyPermissionGate(),
        registry.Tools,new AgentLoopLimits(9,5));
static HttpResponseMessage Plain(string answer) => Ok(new{done=true,message=new {role="assistant",content=answer}});
static HttpResponseMessage Native(string fn,object args) => Ok(new{done=true,message=new{
    role="assistant",content="",tool_calls=new[]{new{function=new{name=fn,arguments=args}}}}});
static HttpResponseMessage WebSource()=>Ok(new{results=new[]{new {
    url="https://science.nasa.gov/mission/webb/",title="NASA Webb telescope",
    content="NASA Webb observes infrared wavelengths to study cool and distant astronomical sources."}}});
static HttpResponseMessage Ok<T>(T data)=>new(HttpStatusCode.OK){Content=new StringContent(
    JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
static HttpClient Http(Func<HttpRequestMessage,CancellationToken,HttpResponseMessage> callback)=>
    new(new StubHandler(callback)){Timeout=TimeSpan.FromSeconds(15)};
static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
async Task Test(string label, Func<Task> action){try{await action();passed++;Console.WriteLine("PASS "+label);}
    catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+label+": "+e);}}
sealed class TempFolder : IDisposable {
    public string Path {get;} = System.IO.Path.Combine(System.IO.Path.GetTempPath(),"nira-v2-general-test-"+Guid.NewGuid().ToString("N"));
    public TempFolder()=>Directory.CreateDirectory(Path);
    public void Dispose(){if(Directory.Exists(Path))Directory.Delete(Path,true);}
}
sealed class StubHandler:HttpMessageHandler {
    private readonly Func<HttpRequestMessage,CancellationToken,HttpResponseMessage> _reply;
    public StubHandler(Func<HttpRequestMessage,CancellationToken,HttpResponseMessage> reply)=>_reply=reply;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(_reply(request,ct));
}
sealed class FakeMutation:IGeneralCapability {
    public AgentToolSpec Spec {get;}=new("fake.modify","Dangerous fake write",AccessLevel.Modify);
    public string FunctionName=>"fake_modify";
    public JsonElement InputSchema {get;}=JsonSerializer.SerializeToElement(new{type="object",properties=new{},additionalProperties=false});
    public bool ValidateArguments(JsonElement _) =>true;
    public Task<AgentToolReceipt> ExecuteAsync(string _,CancellationToken ct)=>throw new Exception("Must never execute");
}
sealed class CustomProbe:IGeneralCapability {
    public AgentToolSpec Spec {get;}=new("custom.probe","Custom test capability",AccessLevel.Observe);
    public string FunctionName=>"custom_probe";
    public JsonElement InputSchema {get;}=JsonSerializer.SerializeToElement(new{type="object",properties=new{},additionalProperties=false});
    public bool ValidateArguments(JsonElement _) =>true;
    public Task<AgentToolReceipt> ExecuteAsync(string _,CancellationToken ct)=>Task.FromResult(AgentToolReceipt.ReadOnlyResult());
}

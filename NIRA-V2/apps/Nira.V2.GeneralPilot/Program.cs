using System.Text;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;
using Nira.V2.Web;
using Nira.V2.Windows;

// General-agent DEVELOPMENT vertical slice. Checkpoints and vault are now protected
// by per-user Windows DPAPI; cloud prompts still leave the device. No private PC tools yet.
Console.OutputEncoding = Encoding.UTF8;
string request = args.Length > 0 ? string.Join(' ', args) :
    "Hello, NIRA. Explain what you can and cannot do in this pilot.";
if (string.IsNullOrWhiteSpace(request) || request.Length > 12_000)
{
    Console.Error.WriteLine("Provide a public/non-sensitive prompt under 12,000 characters.");
    return 2;
}
if (string.IsNullOrWhiteSpace(OllamaCloudCredentials.ReadExistingKey()))
{
    Console.Error.WriteLine("Existing NIRA_OLLAMA_API_KEY not available. No request sent.");
    return 2;
}
string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
if (string.IsNullOrWhiteSpace(baseFolder))
    throw new InvalidOperationException("Windows LocalAppData folder unavailable.");
string root = Path.Combine(baseFolder, "NIRA", "V2", "GeneralPilotSecure");
var protector = new WindowsDpapiDataProtector();
var vault = new AgentPayloadVault(Path.Combine(root, "vault"), protector);
using var modelHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
using var searchHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
using var modelHttp = new HttpClient(modelHandler) { Timeout = TimeSpan.FromMinutes(4) };
using var searchHttp = new HttpClient(searchHandler) { Timeout = TimeSpan.FromSeconds(45) };
var capabilities = new List<IGeneralCapability> {
    new CalculatorCapability(vault), new PlatformCapability(vault)
};
if (!string.IsNullOrWhiteSpace(TavilySearchTool.ReadKey()))
    capabilities.Add(new SearchCapability(new TavilySearchTool(searchHttp, vault)));
// Explicit per-process user consent; the model cannot activate observation.
// Only process names of visible windows are observable; no titles or file names.
string? pcConsent = Environment.GetEnvironmentVariable("NIRA_V2_PC_OBSERVATION");
capabilities.AddRange(WindowsObservationRegistration.Create(vault, pcConsent));
var registry = new CapabilityRegistry(capabilities);
var model = Environment.GetEnvironmentVariable("NIRA_V2_MODEL") ?? "gpt-oss:120b";
var reasoner = new GeneralOllamaReasoner(modelHttp, OllamaReasonerOptions.Cloud(model),
    registry, vault, maximumTools: 5);
var engine = new AgentLoopEngine(reasoner, new FileAgentStateStore(root, protector),
    new ObserveOnlyPermissionGate(), registry.Tools, new AgentLoopLimits(9, 5),
    new StatusPrinter());
Console.WriteLine("[NIRA V2 GENERAL PILOT] Dynamic capabilities: " +
    string.Join(", ", registry.Tools.Select(t => t.Spec.Id)));
Console.WriteLine("[PRIVATE STATE] Checkpoints and vault encrypted with Windows CurrentUser DPAPI.");
Console.WriteLine("[CLOUD] User prompts and authorized tool queries are sent to remote providers.");
Console.WriteLine("[WINDOWS OBSERVATION] " +
    (WindowsObservationRegistration.IsConsented(pcConsent) ?
        "Opted in: visible process names/foreground app only; sent to cloud if used." :
        "Disabled. Set NIRA_V2_PC_OBSERVATION=ALLOW_APP_NAMES to opt in for this session."));
Console.WriteLine("[SECURITY] No file reads, window titles, screenshots, browser control or state-changing PC actions registered.");
AgentRunSnapshot run = await engine.StartAsync(request);
Console.WriteLine("STATUS: " + run.Status);
Console.WriteLine("MODEL REQUESTS: " + reasoner.ModelRequests + " | RECOVERIES: " + reasoner.RecoveryRequests);
Console.WriteLine("DECISIONS: " + run.DecisionCount + " | TOOLS: " + run.ToolCallCount);
Console.WriteLine("LAST DECISION: " + (reasoner.LastDecisionType ?? "none"));
if (reasoner.LastFailureCode is not null)
    Console.WriteLine("MODEL DIAGNOSTIC: " + reasoner.LastFailureCode);
Console.WriteLine(run.FinalAnswer ?? "No completed answer. " + (run.LastErrorCode ?? reasoner.LastFailureCode));
Console.WriteLine("CHECKPOINT ID: " + run.Id);
return run.Status == AgentRunStatus.Completed ? 0 : 1;

sealed class StatusPrinter : IProgress<AgentActivityEvent>
{
    public void Report(AgentActivityEvent entry) => Console.WriteLine(
        $"[{entry.AtUtc:HH:mm:ss}] {entry.Code}" +
        (entry.ToolId is null ? "" : " " + entry.ToolId) +
        (entry.DetailCode is null ? "" : " (" + entry.DetailCode + ")"));
}

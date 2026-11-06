using Nira.V2.Contracts;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;

// Pilot deliberately has NO tools, prior chat history, WPF integration, or persistent persona.
// Non-sensitive test prompts only: Phase 0 checkpoint files are not encrypted.
string model = Environment.GetEnvironmentVariable("NIRA_V2_MODEL") ?? "gpt-oss:120b-cloud";
string endpoint = Environment.GetEnvironmentVariable("NIRA_V2_OLLAMA_URL") ?? "http://127.0.0.1:11434/";
string prompt = args.Length > 0 ? string.Join(' ', args) :
    "Explain in three sentences what an agentic execution loop is.";

if (string.IsNullOrWhiteSpace(prompt))
{
    Console.Error.WriteLine("Provide a non-sensitive test question.");
    return 2;
}

try
{
    var options = OllamaReasonerOptions.Local(model, endpoint);
    options.Validate();
    using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(4) };
    string stateRoot = Path.Combine(Path.GetTempPath(), "NIRA-V2-PilotRuns");
    var engine = new AgentLoopEngine(new OllamaReasoner(http, options),
        new FileAgentStateStore(stateRoot), new ObserveOnlyPermissionGate(),
        Array.Empty<IAgentTool>(), new AgentLoopLimits(MaxDecisions: 2, MaxTools: 0),
        new LiveProgress());
    Console.WriteLine($"[NIRA V2 PILOT] Model: {model}. No tools, memory or live research.");
    AgentRunSnapshot run = await engine.StartAsync(prompt);
    Console.WriteLine($"STATUS: {run.Status}");
    Console.WriteLine(run.FinalAnswer ?? $"No completed answer. Reason: {run.LastErrorCode}");
    Console.WriteLine($"CHECKPOINT ID: {run.Id}");
    Console.WriteLine("Stored temporarily at: " + stateRoot);
    return run.Status == AgentRunStatus.Completed ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Live pilot failed: " + ex.GetType().Name + ". " + ex.Message);
    return 1;
}

sealed class LiveProgress : IProgress<AgentActivityEvent>
{
    public void Report(AgentActivityEvent entry) =>
        Console.WriteLine($"[{entry.AtUtc:HH:mm:ss}] {entry.Code}" +
            (entry.DetailCode is null ? "" : " (" + entry.DetailCode + ")"));
}

using Nira.V2.Contracts;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;

// Phase 1B cloud-transport pilot: no tools, user memory, research or persistent persona.
// The Phase 0 JSON checkpoint is UNENCRYPTED. Do not submit private information.
string prompt = args.Length > 0 ? string.Join(' ', args) :
    "Explain in three sentences what an agentic execution loop is.";
if (string.IsNullOrWhiteSpace(prompt))
{
    Console.Error.WriteLine("Provide a non-sensitive test question.");
    return 2;
}

try
{
    string? key = OllamaCloudCredentials.ReadExistingKey();
    string requestedProvider = Environment.GetEnvironmentVariable("NIRA_V2_PROVIDER") ??
        (string.IsNullOrWhiteSpace(key) ? "local" : "cloud");
    bool cloud = requestedProvider.Equals("cloud", StringComparison.OrdinalIgnoreCase);
    bool local = requestedProvider.Equals("local", StringComparison.OrdinalIgnoreCase);
    if (!cloud && !local)
        throw new ArgumentException("NIRA_V2_PROVIDER must be cloud or local.");
    if (cloud && string.IsNullOrWhiteSpace(key))
        throw new InvalidOperationException("No existing Ollama Cloud API key found. " +
            "Configure NIRA_OLLAMA_API_KEY in Windows User environment.");

    string model = Environment.GetEnvironmentVariable("NIRA_V2_MODEL") ??
        (cloud ? "gpt-oss:120b" : "gpt-oss:120b-cloud");
    var options = cloud ? OllamaReasonerOptions.Cloud(model) :
        OllamaReasonerOptions.Local(model,
            Environment.GetEnvironmentVariable("NIRA_V2_OLLAMA_URL") ??
            "http://127.0.0.1:11434/");
    options.Validate();
    if (cloud && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRA_V2_OLLAMA_URL")))
        throw new ArgumentException("Remove NIRA_V2_OLLAMA_URL in cloud mode. " +
            "Cloud credentials are restricted to https://ollama.com/.");

    using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(4) };
    var reasoner = new OllamaReasoner(http, options);
    string stateRoot = Path.Combine(Path.GetTempPath(), "NIRA-V2-PilotRuns");
    var engine = new AgentLoopEngine(reasoner,
        new FileAgentStateStore(stateRoot), new ObserveOnlyPermissionGate(),
        Array.Empty<IAgentTool>(), new AgentLoopLimits(MaxDecisions: 2, MaxTools: 0),
        new LiveProgress());
    Console.WriteLine($"[NIRA V2 PILOT] Provider: {(cloud ? "Ollama Cloud" : "Ollama Local")}; " +
        $"model: {options.Model}. No tools, memory or live research.");
    AgentRunSnapshot run = await engine.StartAsync(prompt);
    Console.WriteLine($"STATUS: {run.Status}");
    Console.WriteLine(run.FinalAnswer ?? $"No completed answer. Reason: {run.LastErrorCode}");
    if (reasoner.LastFailureCode is not null)
        Console.WriteLine("PROVIDER DIAGNOSTIC: " + reasoner.LastFailureCode);
    Console.WriteLine($"CHECKPOINT ID: {run.Id}");
    Console.WriteLine("Stored temporarily at: " + stateRoot);
    return run.Status == AgentRunStatus.Completed ? 0 : 1;
}
catch (Exception ex)
{
    // The API credential is NEVER printed; do not surface raw HTTP bodies.
    Console.Error.WriteLine("Live pilot failed: " + ex.GetType().Name + ". " + ex.Message);
    return 1;
}

sealed class LiveProgress : IProgress<AgentActivityEvent>
{
    public void Report(AgentActivityEvent entry) =>
        Console.WriteLine($"[{entry.AtUtc:HH:mm:ss}] {entry.Code}" +
            (entry.DetailCode is null ? "" : " (" + entry.DetailCode + ")"));
}

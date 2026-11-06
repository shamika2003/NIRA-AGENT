using Nira.V2.Contracts;
using Nira.V2.Ollama;
using Nira.V2.Runtime.AgentLoop;
using Nira.V2.Web;

// Development pilot. NOT a replacement for NIRA's real persona, persisted memory,
// privacy store or desktop capability executor. Use only public/non-sensitive tasks.
string prompt = args.Length == 0 ?
    "Research why the James Webb Space Telescope uses infrared instruments, " +
    "and explain the main reasons with credible cited sources." : string.Join(' ', args);
if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 5000)
{
    Console.Error.WriteLine("Provide a non-sensitive research question under 5000 characters.");
    return 2;
}

string? cloudKey = OllamaCloudCredentials.ReadExistingKey();
string? tavilyKey = TavilySearchTool.ReadKey();
if (string.IsNullOrWhiteSpace(cloudKey))
{
    Console.Error.WriteLine("Ollama Cloud key missing: set Windows User NIRA_OLLAMA_API_KEY.");
    return 2;
}
if (string.IsNullOrWhiteSpace(tavilyKey))
{
    Console.Error.WriteLine("Tavily search key missing: set Windows User NIRA_TAVILY_API_KEY.");
    Console.Error.WriteLine("No search request or cloud model call was made.");
    return 2;
}
var model = Environment.GetEnvironmentVariable("NIRA_V2_MODEL") ?? "gpt-oss:120b";
var options = OllamaReasonerOptions.Cloud(model);
string stateRoot = Path.Combine(Path.GetTempPath(), "NIRA-V2-ResearchPilotRuns");
var vault = new AgentPayloadVault(Path.Combine(stateRoot, "vault"));
using var modelHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
using var searchHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
using var modelHttp = new HttpClient(modelHandler) { Timeout = TimeSpan.FromMinutes(4) };
using var searchHttp = new HttpClient(searchHandler) { Timeout = TimeSpan.FromSeconds(45) };
var reasoner = new OllamaResearchReasoner(modelHttp, options, vault);
var search = new TavilySearchTool(searchHttp, vault);
var engine = new AgentLoopEngine(reasoner, new FileAgentStateStore(stateRoot),
    new ObserveOnlyPermissionGate(), new IAgentTool[] { search },
    new AgentLoopLimits(MaxDecisions: 7, MaxTools: 4), new ProgressPrinter());
Console.WriteLine("[NIRA V2 RESEARCH PILOT] Ollama Cloud + Tavily read-only web.search");
Console.WriteLine("[DEVELOPMENT] Checkpoints and source excerpts are stored UNENCRYPTED in Temp.");
Console.WriteLine("[TOOLS] No filesystem writes, browser automation, or desktop actions are enabled.");
AgentRunSnapshot run = await engine.StartAsync(prompt);
Console.WriteLine("STATUS: " + run.Status);
Console.WriteLine("MODEL DECISIONS: " + run.DecisionCount + " | WEB SEARCH CALLS: " + run.ToolCallCount);
Console.WriteLine("MODEL HTTP REQUESTS: " + reasoner.TotalModelRequestCount +
    " | PROTOCOL RECOVERIES: " + reasoner.TotalProtocolRecoveryCount);
Console.WriteLine("LAST DECISION PROTOCOL: " + (reasoner.LastProtocol ?? "none"));
Console.WriteLine("CITATION DIAGNOSTIC: candidate_chars=" + reasoner.LastCandidateAnswerChars +
    " source_id_mentions=" + reasoner.LastCitationIdMentions +
    " url_mentions=" + reasoner.LastUrlMentions);
Console.WriteLine("EVIDENCE DIAGNOSTIC: observed_sources=" + reasoner.LastEvidenceSourceCount +
    " presented_sources=" + reasoner.LastEvidencePresentedCount +
    " successful_searches=" + reasoner.LastSuccessfulSearches +
    " duplicate_sources=" + reasoner.LastEvidenceDuplicateCount);
Console.WriteLine("CONTEXT BUDGET: raw_chars=" + reasoner.LastEvidenceRawCharacters +
    " compact_chars=" + reasoner.LastEvidenceCompactCharacters +
    " compacted=" + reasoner.LastEvidenceWasCompacted +
    " synthesis_required=" + reasoner.LastSynthesisRequired);
if (run.FinalAnswer is not null) Console.WriteLine("\n" + run.FinalAnswer);
else Console.WriteLine("No verified completed answer. Reason: " + run.LastErrorCode);
if (reasoner.LastFailureCode is not null) Console.WriteLine("MODEL DIAGNOSTIC: " + reasoner.LastFailureCode);
Console.WriteLine("CHECKPOINT ID: " + run.Id);
Console.WriteLine("STATE ROOT: " + stateRoot);
return run.Status == AgentRunStatus.Completed ? 0 : 1;

sealed class ProgressPrinter : IProgress<AgentActivityEvent>
{
    public void Report(AgentActivityEvent entry) =>
        Console.WriteLine($"[{entry.AtUtc:HH:mm:ss}] {entry.Code}" +
            (entry.ToolId is null ? "" : " " + entry.ToolId) +
            (entry.DetailCode is null ? "" : " (" + entry.DetailCode + ")"));
}

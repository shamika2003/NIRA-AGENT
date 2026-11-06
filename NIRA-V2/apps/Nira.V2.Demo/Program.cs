using Nira.V2.Contracts;
using Nira.V2.Runtime.AgentLoop;

// An honest OFFLINE demonstration of the agent loop, not actual external research.
// No LLM provider, real websites, private sources or permissions are used.
string folder = Path.Combine(Path.GetTempPath(), "nira-v2-demo-" + Guid.NewGuid().ToString("N"));
var engine = new AgentLoopEngine(new DemoReasoner(), new FileAgentStateStore(folder),
    new ObserveOnlyPermissionGate(), new IAgentTool[] { new DemoResearchTool() },
    progress: new PrintProgress());
AgentRunSnapshot result = await engine.StartAsync("Demonstrate an observable multi-step NIRA task");
Console.WriteLine();
Console.WriteLine("STATUS: " + result.Status);
Console.WriteLine("OUTPUT: " + (result.FinalAnswer ?? "<no completed answer>"));
Console.WriteLine("CHECKPOINT: " + Path.Combine(folder, result.Id.ToString("N") + ".json"));

sealed class DemoReasoner : IAgentReasoner
{
    public Task<AgentDecision> DecideAsync(AgentRunSnapshot state,
        IReadOnlyList<AgentToolSpec> tools, CancellationToken ct) => Task.FromResult(
            state.Observations.Count == 0
                ? AgentDecision.Tool("demo.observe", "fixture:known")
                : AgentDecision.Respond("Offline demonstration completed. One read-only tool result was recorded. " +
                    "This is not a live researched answer."));
}
sealed class DemoResearchTool : IAgentTool
{
    public AgentToolSpec Spec { get; } = new("demo.observe", "Synthetic read-only demonstration", AccessLevel.Observe);
    public Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct) =>
        Task.FromResult(AgentToolReceipt.ReadOnlyResult("fixture:synthetic-evidence"));
}
sealed class PrintProgress : IProgress<AgentActivityEvent>
{
    public void Report(AgentActivityEvent value) =>
        Console.WriteLine($"{value.AtUtc:HH:mm:ss} {value.Code}" +
            (value.ToolId is null ? "" : $" ({value.ToolId})"));
}

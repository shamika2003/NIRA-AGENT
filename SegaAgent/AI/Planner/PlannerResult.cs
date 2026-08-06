/*
 * filename: PlannerResult.cs
 */

namespace SegaAgent.AI.Planner;

public class PlannerResult
{
    public string Intent { get; set; } = "";

    public bool RequiresTools { get; set; }

    public List<ToolCall> ToolCalls { get; set; } = new();

    public string ResponseStyle { get; set; } = "normal";
}

public class ToolCall
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public Dictionary<string, object> Parameters { get; set; } = new();
}
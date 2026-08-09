/*
 * filename: AgentRequest.cs
 */

using SegaAgent.Perception;

namespace SegaAgent.Agent;

public abstract record AgentRequest
{
    public abstract AgentRequestSource Source { get; }
}


// =========================================================
// USER
// =========================================================

public sealed record UserAgentRequest(
    string UserInput
) : AgentRequest
{
    public override AgentRequestSource Source =>
        AgentRequestSource.User;
}


// =========================================================
// PC PERCEPTION
// =========================================================

public sealed record PerceptionAgentRequest(
    PerceptionEvent Perception
) : AgentRequest
{
    public override AgentRequestSource Source =>
        AgentRequestSource.Perception;
}


// =========================================================
// PROACTIVE / COMPANION
// =========================================================

public sealed record ProactiveAgentRequest(
    PerceptionEvent Perception
) : AgentRequest
{
    public override AgentRequestSource Source =>
        AgentRequestSource.Proactive;
}


// =========================================================
// SOURCE
// =========================================================

public enum AgentRequestSource
{
    User,
    Perception,
    Proactive
}
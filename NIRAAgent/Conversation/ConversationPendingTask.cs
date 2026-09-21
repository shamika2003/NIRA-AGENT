namespace NIRAAgent.Conversation;

// In-session user objective awaiting a conversational answer. No secrets,
// grants, planned side effects or world-state claims belong in this record.
public sealed record ConversationPendingTask(
    string Objective,
    string LastQuestion);

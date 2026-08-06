/*
 * filename: ChatMessage.cs
 */


namespace SegaAgent.UI.Models;

public sealed class ChatMessage
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Role { get; }

    public string Content { get; set; }

    public DateTime Timestamp { get; }

    public bool IsUser =>
        Role.Equals(
            "user",
            StringComparison.OrdinalIgnoreCase
        );

    public bool IsAssistant =>
        Role.Equals(
            "assistant",
            StringComparison.OrdinalIgnoreCase
        );

    public bool IsError { get; set; }

    public ChatMessage(
        string role,
        string content)
    {
        Role = role;
        Content = content;
        Timestamp = DateTime.Now;
    }
}
/*
 * filename: ConversationManager.cs
 */

namespace SegaAgent.Conversation;

public class ConversationManager
{
    private readonly List<ConversationMessage> _messages = new();

    public IReadOnlyList<ConversationMessage> Messages => _messages;

    public void AddUserMessage(string content)
    {
        _messages.Add(
            new ConversationMessage(
                "user",
                content
            )
        );
    }

    public void AddAssistantMessage(string content)
    {
        _messages.Add(
            new ConversationMessage(
                "assistant",
                content
            )
        );
    }

    public void Clear()
    {
        _messages.Clear();
    }

    public List<ConversationMessage> GetMessages()
    {
        return new List<ConversationMessage>(_messages);
    }

    public string BuildContext()
    {
        if (_messages.Count == 0)
            return "";

        var lines = new List<string>();

        foreach (var message in _messages)
        {
            lines.Add(
                $"{message.Role}: {message.Content}"
            );
        }

        return string.Join(
            Environment.NewLine,
            lines
        );
    }
}

public record ConversationMessage(
    string Role,
    string Content
);
using System.Text.Json;

namespace Nira.Core;

/// <summary>Bounded local references, not a file upload or permission grant.</summary>
public static class ChatAttachmentMessage
{
    public const int MaximumFiles = 8;
    public static ChatEntry Create(string? text, IReadOnlyList<string>? paths)
    {
        if (paths is not { Count: > 0 }) return new ChatEntry("user", text);
        if (paths.Count > MaximumFiles || paths.Any(p => string.IsNullOrWhiteSpace(p) ||
            p.Length > 1024 || !Path.IsPathFullyQualified(p) || p.Any(char.IsControl)))
            throw new ArgumentException("Select up to eight valid absolute local file paths.");
        string[] selected = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string display = text?.Trim() ?? "";
        string content = (display.Length == 0 ? "Examine the attached files and ask if a specific task is needed." : display) +
            "\n\nUser-selected local file references (JSON paths; use the normal authorized tools, not permission grants; " +
            "do not send file contents to an external service without host approval):\n" + JsonSerializer.Serialize(selected);
        return new ChatEntry("user", content, AttachmentPaths: selected, UserDisplayText: display);
    }
}

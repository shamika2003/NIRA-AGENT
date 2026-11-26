using System.Text;
using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

internal static class ChatCompletionStream
{
    private sealed class CallParts
    {
        public string? Id;
        public readonly StringBuilder Name = new();
        public readonly StringBuilder Arguments = new();
    }

    public static async Task<ModelReply> ReadAsync(HttpContent content, Action<string?> preview, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var visible = new StringBuilder();
        var calls = new SortedDictionary<int, CallParts>();
        string? finish = null;
        ModelTokenUsage? usage = null;
        int total = 0;
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            total = checked(total + line.Length + 1);
            if (total > 4 * 1024 * 1024) throw new AgentException("Model stream exceeded transport limit.");
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line[5..].TrimStart(' '));
                continue;
            }
            if (line.Length != 0 || data.Length == 0) continue;
            string packet = data.ToString(); data.Clear();
            if (packet == "[DONE]") break;
            using var doc = JsonDocument.Parse(packet);
            var root = doc.RootElement;
            usage = OpenAiCompatibleModel.ReadUsage(root) ?? usage;
            if (root.TryGetProperty("error", out _)) throw new AgentException("Model stream returned an error.");
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                throw new AgentException("Invalid model stream event.");
            if (choices.GetArrayLength() == 0) continue; // usage-only event
            var choice = choices[0];
            if (finish is not null) throw new AgentException("Model stream emitted content after its terminal choice.");
            if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
            {
                if (OpenAiCompatibleModel.ExtractVisibleText(delta) is { Length: > 0 } part)
                {
                    visible.Append(part);
                    if (calls.Count == 0) preview(part);
                }
                if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
                {
                    if (calls.Count == 0) preview(null);
                    foreach (var call in toolCalls.EnumerateArray())
                    {
                        int index = call.GetProperty("index").GetInt32();
                        if (index < 0 || index >= 32) throw new AgentException("Invalid streamed tool index.");
                        if (!calls.TryGetValue(index, out var parts)) { parts = new CallParts(); calls.Add(index, parts); }
                        if (call.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } received)
                        {
                            if (parts.Id is not null && parts.Id != received) throw new AgentException("Streamed tool identity changed.");
                            parts.Id = received;
                        }
                        if (call.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
                        {
                            if (function.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String) parts.Name.Append(name.GetString());
                            if (function.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String) parts.Arguments.Append(args.GetString());
                        }
                    }
                }
            }
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String) finish = reason.GetString();
        }
        if (data.Length != 0 || finish is not ("stop" or "tool_calls"))
            throw new AgentException("Model stream ended without a complete response; no partial tool was dispatched.");
        var result = new List<ModelToolCall>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parts in calls.Values)
        {
            if (string.IsNullOrWhiteSpace(parts.Id) || parts.Name.Length == 0 || !ids.Add(parts.Id))
                throw new AgentException("Incomplete or duplicate streamed tool identity.");
            string args = parts.Arguments.ToString();
            using var parsed = JsonDocument.Parse(args);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new AgentException("Invalid streamed tool arguments.");
            result.Add(new ModelToolCall(parts.Id, parts.Name.ToString(), args));
        }
        if (finish == "tool_calls" && result.Count == 0) throw new AgentException("Missing streamed tool calls.");
        return new ModelReply(visible.Length == 0 ? null : visible.ToString(), result) { Usage = usage };
    }
}

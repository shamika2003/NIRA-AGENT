using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>
/// A bounded OpenAI Chat Completions tool-calling transport. Compatible with
/// Ollama's /v1/chat/completions subset; credentials supplied at runtime only.
/// </summary>
public sealed class OpenAiCompatibleModel(
    HttpClient http, Uri baseUri, string model, Func<string?> getKey) : IChatModel
{
    public async Task<ModelReply> CompleteAsync(IReadOnlyList<ChatEntry> messages,
        IReadOnlyList<ToolDescriptor> tools, CancellationToken ct)
    {
        var wireMessages = new List<Dictionary<string, object?>>(messages.Count);
        foreach (var item in messages)
        {
            var row = new Dictionary<string, object?> {
                ["role"] = item.Role, ["content"] = item.Content ?? "" };
            if (item.Role == "tool") row["tool_call_id"] = item.ToolCallId;
            if (item.Role == "assistant" && item.ToolCalls is { Count: > 0 })
                row["tool_calls"] = item.ToolCalls.Select(call => new {
                    id = call.Id, type = "function",
                    function = new { name = call.Name, arguments = call.ArgumentsJson }
                }).ToArray();
            wireMessages.Add(row);
        }
        var functions = tools.Select(tool => new {
            type = "function", function = new {
                name = tool.Name, description = tool.Description, parameters = tool.Parameters
            }
        }).ToArray();
        // Some OpenAI-compatible providers reject or mishandle tool_choice when
        // the list is empty. A tool-free recovery must be an ordinary chat request.
        var payload = new Dictionary<string, object?> {
            ["model"] = model, ["messages"] = wireMessages, ["stream"] = false
        };
        if (functions.Length > 0)
        {
            payload["tools"] = functions;
            payload["tool_choice"] = "auto";
        }
        string json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(baseUri, "chat/completions"));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        string? key = getKey();
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new ModelAuthenticationException(
                "The model endpoint rejected its API credential (HTTP 401). " +
                "The key may be missing, expired or for a different account.");
        if (!response.IsSuccessStatusCode)
            throw new AgentException("Model provider HTTP " + (int)response.StatusCode +
                ". Verify model endpoint, credentials and account access.");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length > 4 * 1024 * 1024)
            throw new AgentException("Model response exceeded the 4 MiB transport limit.");
        using var document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new AgentException("Model response had no choices.");
        JsonElement message = choices[0].GetProperty("message");
        string? content = ExtractVisibleText(message);
        var calls = new List<ModelToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCalls) &&
            toolCalls.ValueKind == JsonValueKind.Array)
        {
            int n = 0;
            foreach (JsonElement call in toolCalls.EnumerateArray())
            {
                string id = call.TryGetProperty("id", out var callId) &&
                    callId.ValueKind == JsonValueKind.String ? callId.GetString()! : "nira-" + Guid.NewGuid().ToString("N");
                JsonElement function = call.GetProperty("function");
                string name = function.GetProperty("name").GetString() ?? "";
                JsonElement arguments = function.GetProperty("arguments");
                string argumentJson = arguments.ValueKind == JsonValueKind.String
                    ? arguments.GetString() ?? "{}" : arguments.GetRawText();
                calls.Add(new ModelToolCall(id + "-" + n++, name, argumentJson));
            }
        }
        return new ModelReply(content, calls);
    }
    // Some OpenAI-compatible providers return text as an array of content parts.
    // Keep reasoning fields private; only an actual visible content part is an answer.
    private static string? ExtractVisibleText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out JsonElement content)) return null;
        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind != JsonValueKind.Array) return null;
        var parts = new List<string>();
        foreach (JsonElement part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object ||
                !part.TryGetProperty("type", out JsonElement type) ||
                type.ValueKind != JsonValueKind.String ||
                type.GetString() != "text" ||
                !part.TryGetProperty("text", out JsonElement text) ||
                text.ValueKind != JsonValueKind.String)
                continue;
            string? value = text.GetString();
            if (!string.IsNullOrEmpty(value)) parts.Add(value);
        }
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

}

/// <summary>A provider refused authentication; the user may supply a new key.</summary>
public sealed class ModelAuthenticationException(string message) : Exception(message);

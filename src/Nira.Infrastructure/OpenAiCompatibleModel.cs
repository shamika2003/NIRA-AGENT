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
    HttpClient http, Uri baseUri, string model, Func<string?> getKey) : IChatModel, IWorkflowPlanningModel, IStreamingChatModel
{
    // Once a provider demonstrates a tight context budget, avoid submitting
    // the same oversized transcript on every subsequent model round.
    private volatile bool _preferCompactInference;

    // The planning pass exposes only a decision schema, never executable tools.
    // Uses the same authenticated provider and native function-call transport.
    public Task<ModelReply> ProposeWorkflowAsync(IReadOnlyList<ChatEntry> messages,
        ToolDescriptor decisionTool, CancellationToken ct) =>
        CompleteAsync(messages, [decisionTool], ct);

    public Task<ModelReply> CompleteAsync(IReadOnlyList<ChatEntry> messages,
        IReadOnlyList<ToolDescriptor> tools, CancellationToken ct)
        => CompleteCoreAsync(messages, tools, null, ct);

    public Task<ModelReply> CompleteStreamingAsync(IReadOnlyList<ChatEntry> messages,
        IReadOnlyList<ToolDescriptor> tools, Action<string?> preview, CancellationToken ct) =>
        CompleteCoreAsync(messages, tools, preview, ct);

    private Task<ModelReply> CompleteCoreAsync(IReadOnlyList<ChatEntry> messages,
        IReadOnlyList<ToolDescriptor> tools, Action<string?>? preview, CancellationToken ct)
    {
        if (_preferCompactInference && ModelContextRecovery.TryReduce(messages, tools,
            out IReadOnlyList<ChatEntry> shorter, out IReadOnlyList<ToolDescriptor> narrowed))
            return SendWithContextRecoveryAsync(shorter, narrowed, allowRecovery: false, ct, preview);
        return SendWithContextRecoveryAsync(messages, tools, allowRecovery: true, ct, preview);
    }

    private async Task<ModelReply> SendWithContextRecoveryAsync(
        IReadOnlyList<ChatEntry> messages, IReadOnlyList<ToolDescriptor> tools,
        bool allowRecovery, CancellationToken ct, Action<string?>? preview)
    {
        preview?.Invoke(null);
        var wireMessages = new List<Dictionary<string, object?>>(messages.Count);
        foreach (var item in messages)
        {
            // The OpenAI-compatible wire protocol uses null (not an empty
            // string) when an assistant response consists solely of tool calls.
            // Several provider templates reject or mis-render content: "" here.
            bool toolOnlyAssistant = item.Role == "assistant" &&
                item.ToolCalls is { Count: > 0 } && string.IsNullOrEmpty(item.Content);
            var row = new Dictionary<string, object?> {
                ["role"] = item.Role,
                ["content"] = toolOnlyAssistant ? null : item.Content ?? "" };
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
            ["model"] = model, ["messages"] = wireMessages, ["stream"] = preview is not null
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
        {
            // A 400 may be due to the provider's context window, tool transcript,
            // or schema validation. Never echo raw provider errors: they can
            // contain user content, file paths, tokens, or request fragments.
            string category = await SafeProviderErrorCategoryAsync(response.Content, ct);
            // An HTTP 400 with explicit context-limit evidence is a failed MODEL
            // request, not a dispatched action. One smaller model-only retry is
            // safe. This never changes saved state, call IDs, or approval rules.
            if (allowRecovery && response.StatusCode == HttpStatusCode.BadRequest &&
                category == "possible_context_limit" &&
                ModelContextRecovery.TryReduce(messages, tools,
                    out IReadOnlyList<ChatEntry> shorter,
                    out IReadOnlyList<ToolDescriptor> narrowed))
            {
                _preferCompactInference = true;
                return await SendWithContextRecoveryAsync(shorter, narrowed,
                    allowRecovery: false, ct, preview);
            }
            throw new AgentException($"Model provider HTTP {(int)response.StatusCode} " +
                $"(category={category}; messages={wireMessages.Count}; " +
                $"tools={functions.Length}; requestBytes={Encoding.UTF8.GetByteCount(json)}). " +
                "No failed request was treated as executed. The existing task and " +
                "previously observed tool receipts remain saved. Use :status to " +
                "inspect the checkpoint before :retry.");
        }
        ModelReply result;
        if (preview is not null && string.Equals(response.Content.Headers.ContentType?.MediaType,
                "text/event-stream", StringComparison.OrdinalIgnoreCase))
            result = await ChatCompletionStream.ReadAsync(response.Content, preview, ct);
        else
        {
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
            var seenCallIds = new HashSet<string>(StringComparer.Ordinal);
            if (message.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement call in toolCalls.EnumerateArray())
                {
                    // Preserve the call ID supplied by the model provider exactly.
                    // Rewriting it before echoing tool results can confuse adapters.
                    string? receivedId = call.TryGetProperty("id", out var callId) &&
                        callId.ValueKind == JsonValueKind.String ? callId.GetString() : null;
                    string id = string.IsNullOrWhiteSpace(receivedId)
                        ? "nira-" + Guid.NewGuid().ToString("N") : receivedId;
                    if (!seenCallIds.Add(id))
                        throw new AgentException("Model provider returned duplicate tool-call IDs. " +
                            "No tool from the malformed response was dispatched.");
                    JsonElement function = call.GetProperty("function");
                    string name = function.GetProperty("name").GetString() ?? "";
                    JsonElement arguments = function.GetProperty("arguments");
                    string argumentJson = arguments.ValueKind == JsonValueKind.String
                        ? arguments.GetString() ?? "{}" : arguments.GetRawText();
                    calls.Add(new ModelToolCall(id, name, argumentJson));
                }
            }
            result = new ModelReply(content, calls);
        }
        string? visible = result.Content;
        // A provider can silently exhaust its output budget or return no
        // visible content with HTTP 200. The exact cause is not known here.
        // Retry only a genuinely smaller model request; no tool has been
        // dispatched from this reply. Keep the retry bounded to one.
        if (allowRecovery && result.ToolCalls.Count == 0 && string.IsNullOrWhiteSpace(visible) &&
            ModelContextRecovery.TryReduce(messages, tools,
                out IReadOnlyList<ChatEntry> shorterEmpty,
                out IReadOnlyList<ToolDescriptor> narrowedEmpty))
        {
            _preferCompactInference = true;
            return await SendWithContextRecoveryAsync(shorterEmpty, narrowedEmpty,
                allowRecovery: false, ct, preview);
        }
        return result;
    }
    // Classify (do not display) at most the first 12 KiB of a failed response.
    // This deliberately never returns the provider's message, error object,
    // authorization header, request payload, or arbitrary untrusted text.
    private static async Task<string> SafeProviderErrorCategoryAsync(
        HttpContent content, CancellationToken ct)
    {
        try
        {
            await using Stream stream = await content.ReadAsStreamAsync(ct);
            byte[] buffer = new byte[12 * 1024];
            int count = await stream.ReadAsync(buffer, ct);
            string failure = Encoding.UTF8.GetString(buffer, 0, count).ToLowerInvariant();
            if (failure.Contains("context_length_exceeded", StringComparison.Ordinal) ||
                failure.Contains("exceed_context_size", StringComparison.Ordinal) ||
                failure.Contains("context window", StringComparison.Ordinal) ||
                failure.Contains("context length", StringComparison.Ordinal) ||
                failure.Contains("maximum context", StringComparison.Ordinal) ||
                failure.Contains("prompt is too long", StringComparison.Ordinal) ||
                failure.Contains("too many tokens", StringComparison.Ordinal))
                return "possible_context_limit";
            if (failure.Contains("tool_call_id", StringComparison.Ordinal) ||
                failure.Contains("unexpected tool call", StringComparison.Ordinal) ||
                failure.Contains("tool result", StringComparison.Ordinal) ||
                failure.Contains("invalid tool call", StringComparison.Ordinal))
                return "possible_tool_transcript";
            if (failure.Contains("tool_choice", StringComparison.Ordinal) ||
                failure.Contains("tool schema", StringComparison.Ordinal) ||
                failure.Contains("invalid function", StringComparison.Ordinal))
                return "possible_tool_schema";
            if (failure.Contains("rate_limit", StringComparison.Ordinal) ||
                failure.Contains("rate limit", StringComparison.Ordinal) ||
                failure.Contains("quota exceeded", StringComparison.Ordinal))
                return "possible_rate_limit";
            if (failure.Contains("failed to read request body", StringComparison.Ordinal) ||
                failure.Contains("unexpected eof", StringComparison.Ordinal))
                return "possible_request_size_or_format";
        }
        catch (IOException) { /* Classification is optional; do not leak raw response. */ }
        catch (HttpRequestException) { /* Network failure while reading error. */ }
        return "unknown";
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

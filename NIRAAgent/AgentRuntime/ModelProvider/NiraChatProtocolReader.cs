using System.Text;
using System.Text.Json;

namespace NIRAAgent.Runtime.ModelProvider;

/// <summary>
/// One boundary for provider responses. Tool invocation is not considered valid
/// until a complete OpenAI-compatible envelope is parsed. Supports JSON and SSE
/// on the same HTTP response; never resends a model request to switch protocol.
/// </summary>
public static class NiraChatProtocolReader
{
    public static async Task<NvidiaStreamResponse> ReadAsync(Stream input,
        string? mediaType, int maxBytes, Action<string>? preview,
        CancellationToken ct, Action<string>? diagnosticPayload = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (mediaType?.Contains("event-stream", StringComparison.OrdinalIgnoreCase) == true)
            return await NvidiaChatStreamReader.ReadAsync(input, maxBytes,
                preview, ct, diagnosticPayload);
        if (maxBytes is < 1024 or > 4 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        using var buffer = new MemoryStream();
        byte[] block = new byte[8192];
        int received;
        while ((received = await input.ReadAsync(block, ct)) != 0)
        {
            if (buffer.Length + received > maxBytes)
                throw new InvalidDataException("MODEL_RESPONSE_TOO_LARGE");
            buffer.Write(block, 0, received);
        }
        if (buffer.Length == 0) throw new InvalidDataException("MODEL_RESPONSE_EMPTY");
        string raw;
        try { raw = new UTF8Encoding(false, true).GetString(buffer.ToArray()); }
        catch (DecoderFallbackException) { throw new InvalidDataException("MODEL_UTF8_INVALID"); }
        // Some OpenAI-compatible proxies incorrectly return SSE as application/json.
        // Sniff the already-received bounded body; do not replay the inference.
        if (raw.TrimStart('\uFEFF', ' ', '\t', '\r', '\n')
            .StartsWith("data:", StringComparison.Ordinal))
        {
            buffer.Position = 0;
            return await NvidiaChatStreamReader.ReadAsync(buffer, maxBytes,
                preview, ct, diagnosticPayload);
        }
        diagnosticPayload?.Invoke(raw);
        JsonElement root;
        try { using JsonDocument doc = JsonDocument.Parse(raw); root = doc.RootElement.Clone(); }
        catch (JsonException) { throw new InvalidDataException("MODEL_JSON_ENVELOPE_INVALID"); }
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("choices", out JsonElement choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1 ||
            choices[0].ValueKind != JsonValueKind.Object ||
            !choices[0].TryGetProperty("message", out JsonElement message) ||
            message.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("MODEL_JSON_CHOICES_INVALID");
        JsonElement choice = choices[0];
        if (!choice.TryGetProperty("finish_reason", out JsonElement finish) ||
            finish.ValueKind != JsonValueKind.String ||
            finish.GetString() is not ("stop" or "tool_calls"))
            throw new InvalidDataException("MODEL_JSON_UNSAFE_FINISH");
        bool hasTools = message.TryGetProperty("tool_calls", out JsonElement tools) &&
            tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() > 0;
        // Some providers label a complete tool response "stop". The single-call
        // decoder still enforces valid arguments, nonambiguous text, and known IDs.
        if (finish.GetString() == "tool_calls" && !hasTools)
            throw new InvalidDataException("MODEL_JSON_TOOL_MISSING");
        // Providers may return several tool intents in one complete response even
        // when one-at-a-time execution is requested. Preserve the bounded batch;
        // the model decoder validates it and emits only ONE checkpointed intent.
        // Never execute a raw provider batch directly or grant parallel writes.
        if (hasTools && tools.GetArrayLength() > 8)
            throw new InvalidDataException("MODEL_JSON_TOOL_BATCH_TOO_LARGE");
        if (!hasTools && (!message.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(content.GetString())))
            throw new InvalidDataException("MODEL_JSON_ANSWER_EMPTY");
        return new NvidiaStreamResponse(message.Clone(), hasTools ? "tool_calls" : "stop");
    }
}

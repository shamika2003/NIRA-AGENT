using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NIRAAgent.Runtime.ModelProvider;

public sealed record NvidiaStreamResponse(JsonElement Message, string FinishReason);

/// <summary>
/// Bounded OpenAI-compatible SSE assembler. Never executes tool calls, never
/// publishes internal reasoning fields, and requires a terminal finish signal.
/// Accepts a clean EOF after a valid finish event (some providers omit [DONE]),
/// but does not accept a truncated tool call or an unmarked partial response.
/// </summary>
public static class NvidiaChatStreamReader
{
    public static async Task<NvidiaStreamResponse> ReadAsync(Stream input, int maxBytes,
        Action<string>? preview, CancellationToken ct, Action<string>? diagnosticFrame = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (maxBytes is < 1024 or > 4 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        using var bounded = new ByteLimitReadStream(input, maxBytes);
        using var reader = new StreamReader(bounded, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var content = new StringBuilder();
        var name = new StringBuilder();
        var args = new StringBuilder();
        bool hasTool = false, done = false;
        string? finish = null;
        int lastPublishedLength = 0;
        long lastPublishedAt = 0;
        var frameLines = new List<string>(4);
        int events = 0;

        void ProcessFrame()
        {
            if (frameLines.Count == 0) return;
            string data = string.Join("\n", frameLines);
            frameLines.Clear();
            if (data == "[DONE]") { done = true; return; }
            diagnosticFrame?.Invoke(data);
            if (++events > 10_000) throw new InvalidDataException("MODEL_SSE_EVENT_LIMIT");
            JsonElement root;
            try { using JsonDocument chunk = JsonDocument.Parse(data); root = chunk.RootElement.Clone(); }
            catch (JsonException) { throw new InvalidDataException("MODEL_SSE_FRAME_JSON_INVALID"); }
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("choices", out JsonElement choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() > 1)
                throw new InvalidDataException("MODEL_SSE_CHOICES_INVALID");
            if (choices.GetArrayLength() == 0) return; // Usage-only terminal frame.
            JsonElement choice = choices[0];
            if (choice.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("MODEL_SSE_CHOICE_INVALID");
            if (choice.TryGetProperty("index", out JsonElement index) &&
                (index.ValueKind != JsonValueKind.Number || index.GetInt32() != 0))
                throw new InvalidDataException("MODEL_SSE_CHOICE_INDEX_INVALID");
            if (choice.TryGetProperty("finish_reason", out JsonElement reason) &&
                reason.ValueKind == JsonValueKind.String)
            {
                string value = reason.GetString() ?? "";
                if (finish is not null && finish != value)
                    throw new InvalidDataException("MODEL_SSE_CONFLICTING_FINISH");
                finish = value;
            }
            if (!choice.TryGetProperty("delta", out JsonElement delta) ||
                delta.ValueKind != JsonValueKind.Object) return;
            if (delta.TryGetProperty("tool_calls", out JsonElement tools) &&
                tools.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                if (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() > 1)
                    throw new InvalidDataException("MODEL_SSE_MULTIPLE_TOOLS_UNSUPPORTED");
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    if (tool.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("MODEL_SSE_TOOL_FRAGMENT_INVALID");
                    if (tool.TryGetProperty("index", out JsonElement ti) &&
                        (ti.ValueKind != JsonValueKind.Number || ti.GetInt32() != 0))
                        throw new InvalidDataException("MODEL_SSE_TOOL_INDEX_INVALID");
                    hasTool = true;
                    if (tool.TryGetProperty("function", out JsonElement fn) &&
                        fn.ValueKind == JsonValueKind.Object)
                    {
                        if (fn.TryGetProperty("name", out JsonElement n) &&
                            n.ValueKind == JsonValueKind.String)
                            name.Append(n.GetString());
                        if (fn.TryGetProperty("arguments", out JsonElement a) &&
                            a.ValueKind == JsonValueKind.String)
                            args.Append(a.GetString());
                    }
                    if (name.Length > 300 || args.Length > 16_000)
                        throw new InvalidDataException("MODEL_SSE_TOOL_TOO_LARGE");
                }
            }
            // Reasoning fields are ignored. Tool call arguments are not previews.
            if (delta.TryGetProperty("content", out JsonElement partial) &&
                partial.ValueKind == JsonValueKind.String)
            {
                content.Append(partial.GetString());
                if (content.Length > 20_000)
                    throw new InvalidDataException("MODEL_SSE_ANSWER_TOO_LARGE");
                long now = Stopwatch.GetTimestamp();
                if (!hasTool && preview is not null && content.Length > lastPublishedLength &&
                    (lastPublishedLength == 0 || content.Length - lastPublishedLength >= 48 ||
                     Stopwatch.GetElapsedTime(lastPublishedAt, now) >= TimeSpan.FromMilliseconds(110)))
                {
                    lastPublishedLength = content.Length;
                    lastPublishedAt = now;
                    try { preview(content.ToString()); }
                    catch (Exception) { /* UI previews never invalidate an agent decision. */ }
                }
            }
        }

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            if (line.Length == 0) { ProcessFrame(); if (done) break; continue; }
            if (line.StartsWith(":", StringComparison.Ordinal)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            string payload = line[5..];
            if (payload.StartsWith(' ')) payload = payload[1..];
            frameLines.Add(payload);
            if (frameLines.Sum(x => x.Length) > 256_000)
                throw new InvalidDataException("MODEL_SSE_EVENT_TOO_LARGE");
        }
        if (!done) ProcessFrame();
        if (finish is not ("stop" or "tool_calls"))
            throw new InvalidDataException(finish is null ?
                "MODEL_SSE_MISSING_FINISH" : "MODEL_SSE_UNSAFE_FINISH");
        if (hasTool && (name.Length == 0 || args.Length == 0 || content.Length != 0))
            throw new InvalidDataException("MODEL_SSE_TOOL_INCOMPLETE_OR_AMBIGUOUS");
        if (!hasTool && (finish == "tool_calls" || content.Length == 0))
            throw new InvalidDataException("MODEL_SSE_ANSWER_INCOMPLETE");
        if (hasTool)
        {
            try
            {
                using JsonDocument parsedArgs = JsonDocument.Parse(args.ToString());
                if (parsedArgs.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("MODEL_SSE_TOOL_ARGUMENTS_INVALID");
            }
            catch (JsonException) { throw new InvalidDataException("MODEL_SSE_TOOL_ARGUMENTS_INVALID"); }
        }
        JsonElement message = hasTool
            ? JsonSerializer.SerializeToElement(new {
                content = (string?)null,
                tool_calls = new[] { new { function = new {
                    name = name.ToString(), arguments = args.ToString()
                } } }
            })
            : JsonSerializer.SerializeToElement(new { content = content.ToString() });
        return new NvidiaStreamResponse(message, hasTool ? "tool_calls" : "stop");
    }

    private sealed class ByteLimitReadStream(Stream inner, long maximum) : Stream
    {
        private long _seen;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            long remaining = maximum - _seen;
            int count = (int)Math.Min(buffer.Length, remaining + 1);
            int n = await inner.ReadAsync(buffer[..count], cancellationToken);
            _seen += n;
            if (_seen > maximum) throw new InvalidDataException("MODEL_SSE_TOO_LARGE");
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { base.Dispose(disposing); }
    }
}

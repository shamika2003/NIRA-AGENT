using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Nira.V2.ModelProvider;

public sealed record NvidiaStreamResponse(JsonElement Message, string FinishReason);

/// <summary>
/// Strict, bounded OpenAI-compatible SSE response assembler. Thought/reasoning
/// fields are deliberately ignored. Native tool calls are assembled but NEVER
/// dispatched by this transport. The caller validates the finished decision.
/// </summary>
public static class NvidiaChatStreamReader
{
    public static async Task<NvidiaStreamResponse> ReadAsync(Stream input, int maxBytes,
        Action<string>? preview, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (maxBytes is < 1024 or > 4 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        using var bounded = new ByteLimitReadStream(input, maxBytes);
        using var reader = new StreamReader(bounded, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var content = new StringBuilder();
        var functionName = new StringBuilder();
        var arguments = new StringBuilder();
        bool hasTool = false, done = false;
        string? finish = null;
        int lastPublishedLength = 0;
        long lastPublishedAt = 0;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            if (line.Length == 0 || line[0] == ':') continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            string data = line[5..].Trim();
            if (data == "[DONE]") { done = true; break; }
            if (data.Length == 0) continue;
            using JsonDocument chunk = JsonDocument.Parse(data);
            JsonElement root = chunk.RootElement;
            if (!root.TryGetProperty("choices", out JsonElement choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() > 1)
                throw new InvalidDataException("NVIDIA_STREAM_CHOICES_INVALID");
            if (choices.GetArrayLength() == 0) continue; // Optional usage frame.
            JsonElement choice = choices[0];
            if (choice.ValueKind != JsonValueKind.Object ||
                !choice.TryGetProperty("index", out JsonElement index) ||
                index.ValueKind != JsonValueKind.Number || index.GetInt32() != 0)
                throw new InvalidDataException("NVIDIA_STREAM_CHOICE_INDEX_INVALID");
            if (choice.TryGetProperty("finish_reason", out JsonElement reason) &&
                reason.ValueKind == JsonValueKind.String)
            {
                if (finish is not null) throw new InvalidDataException("NVIDIA_STREAM_DUPLICATE_FINISH");
                finish = reason.GetString();
            }
            if (!choice.TryGetProperty("delta", out JsonElement delta) ||
                delta.ValueKind != JsonValueKind.Object) continue;
            if (delta.TryGetProperty("tool_calls", out JsonElement tools) &&
                tools.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                if (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() > 1)
                    throw new InvalidDataException("NVIDIA_STREAM_TOOLS_INVALID");
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    if (tool.ValueKind != JsonValueKind.Object ||
                        !tool.TryGetProperty("index", out JsonElement toolIndex) ||
                        toolIndex.ValueKind != JsonValueKind.Number || toolIndex.GetInt32() != 0)
                        throw new InvalidDataException("NVIDIA_STREAM_TOOL_INDEX_INVALID");
                    hasTool = true;
                    if (tool.TryGetProperty("function", out JsonElement fn) &&
                        fn.ValueKind == JsonValueKind.Object)
                    {
                        if (fn.TryGetProperty("name", out JsonElement name) &&
                            name.ValueKind == JsonValueKind.String)
                            functionName.Append(name.GetString());
                        if (fn.TryGetProperty("arguments", out JsonElement args) &&
                            args.ValueKind == JsonValueKind.String)
                            arguments.Append(args.GetString());
                    }
                    if (functionName.Length > 300 || arguments.Length > 8192)
                        throw new InvalidDataException("NVIDIA_STREAM_TOOL_TOO_LARGE");
                }
            }
            // Never publish chain-of-thought, reasoning_content or native tool arguments.
            if (delta.TryGetProperty("content", out JsonElement partial) &&
                partial.ValueKind == JsonValueKind.String)
            {
                content.Append(partial.GetString());
                if (content.Length > 20_000)
                    throw new InvalidDataException("NVIDIA_STREAM_ANSWER_TOO_LARGE");
                long now = Stopwatch.GetTimestamp();
                if (!hasTool && preview is not null && content.Length > lastPublishedLength &&
                    (lastPublishedLength == 0 || content.Length - lastPublishedLength >= 48 ||
                     Stopwatch.GetElapsedTime(lastPublishedAt, now) >= TimeSpan.FromMilliseconds(110)))
                {
                    lastPublishedLength = content.Length;
                    lastPublishedAt = now;
                    try { preview(content.ToString()); }
                    catch (Exception) { /* Optional UI preview cannot invalidate verified run. */ }
                }
            }
        }
        if (!done || finish is not ("stop" or "tool_calls") ||
            (hasTool != (finish == "tool_calls")))
            throw new InvalidDataException("NVIDIA_STREAM_INCOMPLETE");
        JsonElement message = hasTool
            ? JsonSerializer.SerializeToElement(new {
                content = (string?)null,
                tool_calls = new[] { new {
                    function = new { name = functionName.ToString(), arguments = arguments.ToString() }
                } }
            })
            : JsonSerializer.SerializeToElement(new { content = content.ToString() });
        return new NvidiaStreamResponse(message, finish);
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
            if (_seen > maximum) throw new InvalidDataException("NVIDIA_STREAM_TOO_LARGE");
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { /* Caller owns HTTP response stream. */ base.Dispose(disposing); }
    }
}

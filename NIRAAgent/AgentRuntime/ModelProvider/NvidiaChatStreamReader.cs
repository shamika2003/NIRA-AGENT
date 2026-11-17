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
        // Tool fragments may be interleaved and can contain several proposed
        // calls. Their entire batch must be assembled before the existing
        // one-tool-per-checkpoint decoder chooses anything to execute.
        var calls = new SortedDictionary<int, ToolFragments>();
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
                if (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() > 8)
                    throw new InvalidDataException("MODEL_SSE_TOO_MANY_TOOL_FRAGMENTS");
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    if (tool.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("MODEL_SSE_TOOL_FRAGMENT_INVALID");
                    int i = 0;
                    if (tool.TryGetProperty("index", out JsonElement ti))
                    {
                        if (ti.ValueKind != JsonValueKind.Number || !ti.TryGetInt32(out i))
                            throw new InvalidDataException("MODEL_SSE_TOOL_INDEX_INVALID");
                    }
                    if (i is < 0 or > 7)
                        throw new InvalidDataException("MODEL_SSE_TOOL_INDEX_INVALID");
                    if (!calls.TryGetValue(i, out ToolFragments? call))
                    {
                        if (calls.Count >= 8)
                            throw new InvalidDataException("MODEL_SSE_TOO_MANY_TOOLS");
                        call = new ToolFragments();
                        calls.Add(i, call);
                    }
                    if (!hasTool)
                    {
                        hasTool = true;
                        // An early text candidate is NEVER authoritative. A
                        // provider that later switches to tools must withdraw
                        // the provisional preview immediately.
                        if (preview is not null)
                            try { preview(string.Empty); } catch (Exception) { }
                    }
                    if (tool.TryGetProperty("id", out JsonElement id) &&
                        id.ValueKind == JsonValueKind.String)
                        call.Id.Append(id.GetString());
                    if (tool.TryGetProperty("function", out JsonElement fn) &&
                        fn.ValueKind == JsonValueKind.Object)
                    {
                        if (fn.TryGetProperty("name", out JsonElement n) &&
                            n.ValueKind == JsonValueKind.String)
                            call.Name.Append(n.GetString());
                        if (fn.TryGetProperty("arguments", out JsonElement a))
                        {
                            if (a.ValueKind == JsonValueKind.String)
                                call.Arguments.Append(a.GetString());
                            else if (a.ValueKind == JsonValueKind.Object &&
                                     call.Arguments.Length == 0)
                                call.Arguments.Append(a.GetRawText());
                            else if (a.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                                throw new InvalidDataException("MODEL_SSE_TOOL_ARGUMENT_FRAGMENT_INVALID");
                        }
                    }
                    if (call.Id.Length > 256 || call.Name.Length > 300 ||
                        call.Arguments.Length > 16_000)
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
                // The provider's public tool-note marker is NOT a draft answer.
                // Hold the first few characters so a fragmented marker cannot
                // flash as chat text before tool_calls arrive.
                string textSoFar = content.ToString();
                bool possibleToolNote = NIRAAgent.Runtime.General.NiraPublicThinkingNote.Prefix
                    .StartsWith(textSoFar, StringComparison.Ordinal) ||
                    textSoFar.StartsWith(NIRAAgent.Runtime.General.NiraPublicThinkingNote.Prefix,
                        StringComparison.Ordinal);
                if (!hasTool && !possibleToolNote && preview is not null &&
                    content.Length >= 24 && content.Length > lastPublishedLength &&
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
        if (hasTool && (calls.Count == 0 || calls.Keys.First() != 0 ||
            calls.Keys.Last() != calls.Count - 1))
            throw new InvalidDataException("MODEL_SSE_TOOL_BATCH_INCOMPLETE");
        if (!hasTool && (finish == "tool_calls" || content.Length == 0))
            throw new InvalidDataException("MODEL_SSE_ANSWER_INCOMPLETE");
        if (hasTool && content.Length > 0 &&
            !NIRAAgent.Runtime.General.NiraPublicThinkingNote.TryParse(
                content.ToString(), out _))
            throw new InvalidDataException("MODEL_SSE_TOOL_CONTENT_AMBIGUOUS");
        if (hasTool)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ToolFragments call in calls.Values)
            {
                if (call.Name.Length == 0 || call.Arguments.Length == 0)
                    throw new InvalidDataException("MODEL_SSE_TOOL_INCOMPLETE");
                if (call.Id.Length > 0 && !ids.Add(call.Id.ToString()))
                    throw new InvalidDataException("MODEL_SSE_TOOL_DUPLICATE_ID");
                try
                {
                    using JsonDocument parsed = JsonDocument.Parse(call.Arguments.ToString());
                    if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("MODEL_SSE_TOOL_ARGUMENTS_INVALID");
                }
                catch (JsonException)
                { throw new InvalidDataException("MODEL_SSE_TOOL_ARGUMENTS_INVALID"); }
            }
        }
        JsonElement message = hasTool
            ? JsonSerializer.SerializeToElement(new {
                content = content.Length > 0 ? content.ToString() : (string?)null,
                tool_calls = calls.Values.Select(call => new {
                    function = new {
                        name = call.Name.ToString(),
                        arguments = call.Arguments.ToString()
                    }
                }).ToArray()
            })
            : JsonSerializer.SerializeToElement(new { content = content.ToString() });
        return new NvidiaStreamResponse(message, hasTool ? "tool_calls" : "stop");
    }

    private sealed class ToolFragments
    {
        public StringBuilder Id { get; } = new();
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();
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

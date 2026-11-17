using System.Text;
using System.Text.Json;

namespace NIRAAgent.Runtime.ModelProvider;

/// <summary>
/// Pure, offline protocol contract checks. No model calls or external tools.
/// Invoked by the existing WPF acceptance mode before a release is committed.
/// </summary>
public static class NiraProtocolSelfCheck
{
    public static async Task<int> RunAsync(CancellationToken ct = default)
    {
        int passed = 0;
        async Task Verify(string name, string data, string contentType,
            string expectedFinish, string expectedFunction = "")
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            NvidiaStreamResponse result = await NiraChatProtocolReader.ReadAsync(
                stream, contentType, 64 * 1024, null, ct);
            if (result.FinishReason != expectedFinish)
                throw new InvalidDataException(name + ": wrong finish state");
            if (expectedFunction.Length > 0)
            {
                JsonElement fn = result.Message.GetProperty("tool_calls")[0]
                    .GetProperty("function");
                if (fn.GetProperty("name").GetString() != expectedFunction)
                    throw new InvalidDataException(name + ": wrong function");
            }
            else if (result.Message.GetProperty("content").GetString() != "OK")
                throw new InvalidDataException(name + ": wrong answer");
            passed++;
        }
        async Task VerifyReject(string name, string data, string contentType)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            try
            {
                _ = await NiraChatProtocolReader.ReadAsync(
                    stream, contentType, 64 * 1024, null, ct);
            }
            catch (InvalidDataException) { passed++; return; }
            throw new InvalidDataException(name + ": invalid response accepted");
        }
        const string jsonText = """
          {"choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"OK","reasoning_content":"not public"}}]}
          """;
        const string jsonTool = """
          {"choices":[{"index":0,"finish_reason":"tool_calls","message":{"role":"assistant","content":null,"tool_calls":[{"id":"x","type":"function","function":{"name":"nira_list_directory","arguments":"{\"path\":\"Documents\"}"}}]}}]}
          """;
        string sseText =
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"O\"}}]}\n\n" +
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"K\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";
        string sseTool =
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"nira_list_directory\",\"arguments\":\"{\\\"path\\\":\"}}]}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"Documents\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}\n\n";
        await Verify("JSON_TEXT", jsonText, "application/json", "stop");
        await Verify("JSON_TOOL", jsonTool, "application/json", "tool_calls", "nira_list_directory");
        // An OpenAI-compatible provider can return parallel proposals. Their
        // envelope is valid, but the runtime serializes execution at a durable
        // one-tool checkpoint boundary. No second proposal may auto-dispatch.
        // Build independent JSON to avoid depending on a provider's formatting.
        string jsonBatch = JsonSerializer.Serialize(new { choices = new[] { new {
            index = 0, finish_reason = "tool_calls", message = new {
                role = "assistant", content = (string?)null,
                tool_calls = new object[] {
                    new { id = "x", type = "function", function = new {
                        name = "nira_list_directory", arguments = "{\"path\":\"Documents\"}" } },
                    new { id = "y", type = "function", function = new {
                        name = "nira_file_metadata", arguments = "{\"path\":\"Documents\"}" } }
                } } } } });
        await Verify("JSON_MULTI_TOOL_ENVELOPE", jsonBatch, "application/json", "tool_calls",
            "nira_list_directory");
        using (var batchDocument = JsonDocument.Parse(jsonBatch))
        {
            var parsed = NIRAAgent.Runtime.General.GeneralToolProtocol.Decode(
                batchDocument.RootElement.GetProperty("choices")[0].GetProperty("message"));
            if (parsed.Kind != "tool" || parsed.Function != "nira_list_directory" ||
                parsed.Arguments is not JsonElement args ||
                args.GetProperty("path").GetString() != "Documents")
                throw new InvalidDataException("JSON_MULTI_TOOL_SERIALIZATION: one-tool gate failed");
            passed++;
        }
        // A malformed parallel proposal fails closed without dispatching any
        // tool, even if its first call looked structurally valid.
        using (var duplicateDocument = JsonDocument.Parse(jsonBatch.Replace(
            "\"id\":\"y\"", "\"id\":\"x\"")))
        {
            var duplicate = NIRAAgent.Runtime.General.GeneralToolProtocol.Decode(
                duplicateDocument.RootElement.GetProperty("choices")[0].GetProperty("message"));
            if (duplicate.Kind != "invalid" ||
                duplicate.ErrorCode != "MODEL_TOOL_BATCH_DUPLICATE_ID")
                throw new InvalidDataException("JSON_MULTI_TOOL_DUPLICATE_ID: invalid batch accepted");
            passed++;
        }
        await Verify("SSE_TEXT", sseText, "text/event-stream", "stop");
        await Verify("SSE_SNIFF", sseText, "application/json", "stop");
        await Verify("SSE_TOOL_NO_DONE", sseTool, "text/event-stream", "tool_calls", "nira_list_directory");
        await VerifyReject("NO_FINISH", "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n",
            "text/event-stream");
        await VerifyReject("JSON_LENGTH_FINISH", jsonText.Replace("\"stop\"", "\"length\""),
            "application/json");
        await VerifyReject("JSON_TOOL_MISSING", jsonTool.Replace("tool_calls", "not_tool_calls"),
            "application/json");
        return passed;
    }
}

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
        string publicContent = NIRAAgent.Runtime.General.NiraPublicThinkingNote.Prefix +
            "The evidence I need is about separate landing methods, so I'll check the two recovery systems.";
        string annotatedTool = jsonTool.Replace("\"content\":null",
            "\"content\":" + JsonSerializer.Serialize(publicContent));
        using (var annotatedDoc = JsonDocument.Parse(annotatedTool))
        {
            var publicDecision = NIRAAgent.Runtime.General.GeneralToolProtocol.Decode(
                annotatedDoc.RootElement.GetProperty("choices")[0].GetProperty("message"));
            if (publicDecision.Kind != "tool" || publicDecision.PublicUpdate is null ||
                !publicDecision.PublicUpdate.Contains("separate landing methods", StringComparison.Ordinal))
                throw new InvalidDataException("PUBLIC_THINKING_NOTE: model-authored commentary was lost.");
            passed++;
        }
        using (var rawDoc = JsonDocument.Parse(jsonTool.Replace("\"content\":null",
            "\"content\":\"These are my private internal steps\"")))
        {
            var invalid = NIRAAgent.Runtime.General.GeneralToolProtocol.Decode(
                rawDoc.RootElement.GetProperty("choices")[0].GetProperty("message"));
            if (invalid.Kind != "invalid")
                throw new InvalidDataException("PUBLIC_THINKING_NOTE: unmarked tool content was accepted.");
            passed++;
        }
        await Verify("SSE_TEXT", sseText, "text/event-stream", "stop");
        await Verify("SSE_SNIFF", sseText, "application/json", "stop");
        await Verify("SSE_TOOL_NO_DONE", sseTool, "text/event-stream", "tool_calls", "nira_list_directory");
        // Multi-call SSE may interleave fragments for multiple indexes. No
        // incomplete second call may reach the checkpointed one-tool executor.
        string Frame(object delta, string? finish = null) => "data: " +
            JsonSerializer.Serialize(new { choices = new[] { new {
                index = 0, delta, finish_reason = finish
            } } }) + "\n\n";
        string ToolFragment(int index, string? id, string? name, string? args) =>
            Frame(new { tool_calls = new[] { new {
                index, id, function = new { name, arguments = args }
            } } });
        string sseBatch =
            ToolFragment(0, "x", "nira_list_directory", "{\"path\":") +
            ToolFragment(1, "y", "nira_file_metadata", "{\"path\":") +
            ToolFragment(0, null, null, "\"Documents\"}") +
            ToolFragment(1, null, null, "\"Documents\"}") +
            Frame(new { }, "tool_calls") + "data: [DONE]\n\n";
        using (var batchStream = new MemoryStream(Encoding.UTF8.GetBytes(sseBatch)))
        {
            var parsedBatch = await NiraChatProtocolReader.ReadAsync(batchStream,
                "text/event-stream", 64 * 1024, null, ct);
            if (parsedBatch.Message.GetProperty("tool_calls").GetArrayLength() != 2)
                throw new InvalidDataException("SSE multi-tool batch was truncated.");
            var firstOnly = NIRAAgent.Runtime.General.GeneralToolProtocol.Decode(parsedBatch.Message);
            if (firstOnly.Kind != "tool" || firstOnly.Function != "nira_list_directory")
                throw new InvalidDataException("SSE tool batch bypassed one-tool checkpoint.");
            passed++;
        }
        // Some compatible providers emit a complete argument JSON object in
        // one SSE delta instead of the usual arguments string fragments.
        string objectArgs = Frame(new { tool_calls = new[] { new { index = 0,
            function = new { name = "nira_list_directory", arguments = new { path = "Documents" } }
        } } }) + Frame(new { }, "tool_calls");
        await Verify("SSE_OBJECT_ARGUMENTS", objectArgs, "text/event-stream",
            "tool_calls", "nira_list_directory");
        string malformedSecond =
            ToolFragment(0, "x", "nira_list_directory", "{\"path\":\"Documents\"}") +
            ToolFragment(1, "y", "nira_file_metadata", "{\"path\":") +
            Frame(new { }, "tool_calls");
        await VerifyReject("SSE_SECOND_TOOL_TRUNCATED", malformedSecond, "text/event-stream");
        // R83: an upstream error/shape frame must NOT become a model answer
        // or an executable tool. The reasoner separately decides whether the
        // Ollama inference is eligible for one non-streaming JSON retry.
        string badChoices = "data: {\"error\":{\"message\":\"provider interrupted\"}}\n\n";
        bool badChoicesRejected = false;
        using (var badChoicesStream = new MemoryStream(Encoding.UTF8.GetBytes(badChoices)))
        {
            try
            {
                _ = await NiraChatProtocolReader.ReadAsync(badChoicesStream,
                    "text/event-stream", 64 * 1024, null, ct);
            }
            catch (InvalidDataException ex) when (ex.Message == "MODEL_SSE_CHOICES_INVALID")
            { badChoicesRejected = true; }
        }
        if (!badChoicesRejected)
            throw new InvalidDataException(
                "R83: malformed choices SSE frame was accepted or misclassified.");
        passed++;
        string annotatedSse =
            Frame(new { content = publicContent }) +
            ToolFragment(0, "x", "nira_list_directory", "{\"path\":\"Documents\"}") +
            Frame(new { }, "tool_calls");
        using (var noteStream = new MemoryStream(Encoding.UTF8.GetBytes(annotatedSse)))
        {
            var parsedNote = await NiraChatProtocolReader.ReadAsync(noteStream,
                "text/event-stream", 64 * 1024, null, ct);
            var noteDecision = NIRAAgent.Runtime.General.GeneralToolProtocol.Decode(parsedNote.Message);
            if (noteDecision.Kind != "tool" ||
                !string.Equals(noteDecision.PublicUpdate,
                    publicContent[NIRAAgent.Runtime.General.NiraPublicThinkingNote.Prefix.Length..],
                    StringComparison.Ordinal))
                throw new InvalidDataException("SSE public model commentary was discarded.");
            passed++;
        }
        // Provisional answer previews are permitted, but must be withdrawn if
        // the provider switches to a tool proposal and never used as evidence.
        string provisional = Frame(new { content = "A preliminary unverified answer that may change before verification." }) +
            ToolFragment(0, "x", "nira_list_directory", "{\"path\":\"Documents\"}") +
            Frame(new { }, "tool_calls");
        var previews = new List<string>();
        using (var mixedStream = new MemoryStream(Encoding.UTF8.GetBytes(provisional)))
        {
            var mixed = await NiraChatProtocolReader.ReadAsync(mixedStream,
                "text/event-stream", 64 * 1024, null, ct);
            var selected = NIRAAgent.Runtime.General.GeneralToolProtocol.Decode(mixed.Message);
            if (selected.Kind != "tool" || selected.Function != "nira_list_directory" ||
                mixed.Message.GetProperty("content").ValueKind != JsonValueKind.Null)
                throw new InvalidDataException("Mixed prose/tool SSE did not discard unmarked text.");
            passed++;
        }
        using (var streamPreview = new MemoryStream(Encoding.UTF8.GetBytes(provisional)))
        {
            try
            {
                _ = await NiraChatProtocolReader.ReadAsync(streamPreview,
                    "text/event-stream", 64 * 1024, text => previews.Add(text), ct);
            }
            catch (InvalidDataException) { }
        }
        if (previews.Count < 2 || previews[0].Length == 0 || previews[^1].Length != 0)
            throw new InvalidDataException("Streamed provisional answer was not withdrawn.");
        passed++;
        // Provider variants legitimately use finish_reason=stop for a
        // completely formed tool proposal. Its structured arguments still
        // require the SAME strict validation; transport wording alone cannot
        // turn it into a text answer or grant execution authority.
        string stopWithTool = ToolFragment(0, "x", "nira_list_directory",
            "{\"path\":\"Documents\"}") + Frame(new { }, "stop");
        await Verify("SSE_TOOL_STOP_VARIANT", stopWithTool, "text/event-stream",
            "tool_calls", "nira_list_directory");
        // A malformed tool argument must still fail closed even if prose exists.
        string malformedMixed = Frame(new { content = "Planning the task." }) +
            ToolFragment(0, "x", "nira_list_directory", "{invalid") +
            Frame(new { }, "tool_calls");
        await VerifyReject("SSE_MIXED_TOOL_ARGUMENTS_INVALID", malformedMixed,
            "text/event-stream");
        // The provider may stream ordinary final prose after a read-only tool;
        // text shows up incrementally, while the final binding gate still runs.
        string sseLong = Frame(new { content = "An introductory explanation of a reusable rocket. " }) +
            Frame(new { content = "The landing and tower catch are different methods." }, "stop") +
            "data: [DONE]\n\n";
        var textUpdates = new List<string>();
        using (var textStream = new MemoryStream(Encoding.UTF8.GetBytes(sseLong)))
        {
            var textResult = await NiraChatProtocolReader.ReadAsync(textStream,
                "text/event-stream", 64 * 1024, text => textUpdates.Add(text), ct);
            if (textResult.Message.GetProperty("content").GetString()?.Length < 70 ||
                textUpdates.Count == 0 || textUpdates[^1].Length < 30)
                throw new InvalidDataException("SSE text streaming did not emit a preview.");
            passed++;
        }
        // Never allow model-private fields to flow into public text updates.
        string hidden = Frame(new { reasoning_content = "PRIVATE_TRACE", content = "A verified-looking public explanation is being generated." }) +
            Frame(new { }, "stop");
        using (var hiddenStream = new MemoryStream(Encoding.UTF8.GetBytes(hidden)))
        {
            var received = new List<string>();
            _ = await NiraChatProtocolReader.ReadAsync(hiddenStream,
                "text/event-stream", 64 * 1024, text => received.Add(text), ct);
            if (received.Any(t => t.Contains("PRIVATE_TRACE", StringComparison.Ordinal)))
                throw new InvalidDataException("Model private trace leaked into streaming callback.");
            passed++;
        }
        // A model can terminate after private reasoning without emitting a
        // PUBLIC answer or tool call. Never convert reasoning into an answer.
        string reasoningOnly = Frame(new { reasoning = "private analysis", content = "" }) +
            Frame(new { reasoning = "still planning", content = "" }, "stop") +
            "data: [DONE]\n\n";
        await VerifyReject("SSE_REASONING_ONLY_STOP", reasoningOnly,
            "text/event-stream");
        string toolsWithoutCall = Frame(new { reasoning = "tool intent missing", content = "" },
            "tool_calls") + "data: [DONE]\n\n";
        await VerifyReject("SSE_TOOL_FINISH_WITHOUT_TOOL", toolsWithoutCall,
            "text/event-stream");
        string reasoningThenTool = Frame(new { reasoning = "private analysis", content = "" }) +
            ToolFragment(0, "x", "nira_list_directory", "{\"path\":\"Documents\"}") +
            Frame(new { }, "tool_calls") + "data: [DONE]\n\n";
        await Verify("SSE_REASONING_THEN_VALID_TOOL", reasoningThenTool,
            "text/event-stream", "tool_calls", "nira_list_directory");
        string jsonEmpty = "{\"choices\":[{\"index\":0,\"finish_reason\":\"stop\"," +
            "\"message\":{\"role\":\"assistant\",\"content\":\"\"}}]}";
        await VerifyReject("JSON_EMPTY_FINISHED_MESSAGE", jsonEmpty,
            "application/json");
        await VerifyReject("NO_FINISH", "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n",
            "text/event-stream");
        await VerifyReject("JSON_LENGTH_FINISH", jsonText.Replace("\"stop\"", "\"length\""),
            "application/json");
        await VerifyReject("JSON_TOOL_MISSING", jsonTool.Replace("tool_calls", "not_tool_calls"),
            "application/json");
        return passed;
    }
}

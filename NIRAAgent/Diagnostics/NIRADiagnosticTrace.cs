using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NIRAAgent.Diagnostics;

/// <summary>
/// Focused, best-effort execution diagnostics separate from VS Code's noisy
/// .NET module-load debug output. No prompts, typed text, credentials or
/// response bodies are logged unless the developer explicitly opts in.
/// </summary>
public static class NIRADiagnosticTrace
{
    private static readonly object Gate = new();
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NIRAAgent", "diagnostics");
    public static string EventLogPath => Path.Combine(DirectoryPath,
        $"nira-events-{Environment.ProcessId}.log");
    public static string LlmLogPath => Path.Combine(DirectoryPath,
        $"nira-llm-{Environment.ProcessId}.log");

    // Visual Studio / VS Code debugging sessions capture transcripts by default.
    // Outside an attached debugger they remain opt-in, including Release builds.
    // NIRA_DEBUG_LLM_TRANSCRIPTS=0 explicitly turns all payload capture off.
    // These local files may contain private prompts, tool evidence and user data.
    private static bool TranscriptSwitch =>
        Environment.GetEnvironmentVariable("NIRA_DEBUG_LLM_TRANSCRIPTS") switch
        {
            "1" => true,
            "0" => false,
            _ => Debugger.IsAttached
        };

    public static bool LlmResponseCaptureEnabled =>
        Environment.GetEnvironmentVariable("NIRA_DEBUG_LLM_TRANSCRIPTS") != "0" &&
        (TranscriptSwitch || Environment.GetEnvironmentVariable("NIRA_DEBUG_LLM_RESPONSES") == "1");

    public static bool LlmPromptCaptureEnabled =>
        Environment.GetEnvironmentVariable("NIRA_DEBUG_LLM_TRANSCRIPTS") != "0" &&
        (TranscriptSwitch || Environment.GetEnvironmentVariable("NIRA_DEBUG_LLM_PROMPTS") == "1");

    public static bool DetailedToolCaptureEnabled =>
        Environment.GetEnvironmentVariable("NIRA_DEBUG_TOOL_DETAILS") == "1" || TranscriptSwitch;

    // Explicit local development capture of what was ACTUALLY delivered.
    // Distinct from the raw LLM response transcript (which may include rejected
    // candidates). Default off because conversations can contain private data.
    public static bool PublicResponseCaptureEnabled =>
        Environment.GetEnvironmentVariable("NIRA_DEBUG_PUBLIC_RESPONSES") == "1" &&
        Environment.GetEnvironmentVariable("NIRA_DEBUG_LLM_TRANSCRIPTS") != "0";
    public static string PublicResponseLogPath => Path.Combine(DirectoryPath,
        $"nira-public-{Environment.ProcessId}.jsonl");

    public static void PublicResponse(Guid runId, string stage,
        string markdownReply, string speech, object? displayBlocks,
        object? verifiedImageMetadata)
    {
        if (!PublicResponseCaptureEnabled) return;
        try
        {
            Append(PublicResponseLogPath, JsonSerializer.Serialize(new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "PUBLIC-DELIVERY",
                RunId = runId,
                Stage = stage,
                MarkdownReply = markdownReply,
                Speech = speech,
                DisplayBlocks = displayBlocks,
                VerifiedImageMetadata = verifiedImageMetadata
            }));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Event("PUBLIC-DELIVERY-CAPTURE", $"Status=Failed Kind={ex.GetType().Name}");
        }
    }

    public static void PublicVisualEmitted(Guid runId, string subject,
        string sourceUri, string localPath)
    {
        if (!PublicResponseCaptureEnabled) return;
        try
        {
            Append(PublicResponseLogPath, JsonSerializer.Serialize(new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "PUBLIC-IMAGE-EMITTED",
                RunId = runId,
                Subject = subject,
                SourceUri = sourceUri,
                VerifiedLocalPath = localPath
            }));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Event("PUBLIC-IMAGE-CAPTURE", $"Status=Failed Kind={ex.GetType().Name}");
        }
    }

    public static string StatusPath => Path.Combine(DirectoryPath,
        $"nira-diagnostics-status-{Environment.ProcessId}.txt");
    private static bool _statusWritten;

    public static void ModelRequest(Guid callId, string model, string systemPrompt,
        string userPrompt, bool jsonMode, string inputKind, int imageCount = 0)
    {
        if (!LlmPromptCaptureEnabled) return;
        // Full source prompts on purpose: this is developer-owned, local-only
        // diagnostic capture. Images are NEVER persisted in this transcript.
        Append(LlmLogPath, JsonSerializer.Serialize(new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = "LLM-REQUEST",
            Call = callId,
            Model = model,
            Kind = inputKind,
            JsonMode = jsonMode,
            ImageCount = imageCount,
            SystemChars = systemPrompt.Length,
            UserChars = userPrompt.Length,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt
        }));
    }

    public static void Event(string category, string metadata)
    {
        string line = $"{DateTimeOffset.UtcNow:O} [{category}] {OneLine(metadata, 2000)}";
        // The ordinary debugger stream already contains the main runtime
        // events. Keep this structured stream in its own readable file, unless
        // the developer explicitly asks to mirror it into the Debug window.
        if (Environment.GetEnvironmentVariable("NIRA_DEBUG_TRACE_CONSOLE") == "1")
            Debug.WriteLine("[NIRA-TRACE] " + line);
        Append(EventLogPath, line);
    }

    public static void ModelResponse(Guid callId, string model, string response)
    {
        if (!LlmResponseCaptureEnabled) return;
        Append(LlmLogPath, JsonSerializer.Serialize(new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = "LLM-RESPONSE",
            Call = callId,
            Model = model,
            ResponseChars = response.Length,
            Response = response
        }));
        // Avoid doubling enormous transcripts into Visual Studio debug output.
        Debug.WriteLine($"[NIRA-LLM] Captured response | Call={callId:D} | Chars={response.Length}");
    }

    public static string ArgumentKeys(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return "<invalid>";
        return string.Join(",", arguments.EnumerateObject().Select(p => p.Name).Take(40));
    }

    public static string DesktopArgumentShape(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return "InvalidArguments";
        string action = Get(arguments, "action");
        string hwnd = Get(arguments, "windowHandle");
        bool name = HasValue(arguments, "controlName");
        bool id = HasValue(arguments, "automationId");
        bool type = HasValue(arguments, "controlType");
        int chars = arguments.TryGetProperty("text", out JsonElement text) &&
            text.ValueKind == JsonValueKind.String ? (text.GetString()?.Length ?? 0) : 0;
        // Never log control names, titles, typed text, or paths by default.
        return $"Action={OneLine(action, 40)} HWND={OneLine(hwnd, 30)} " +
            $"HasControlName={name} HasAutomationId={id} HasControlType={type} " +
            $"TextChars={chars}";
    }

    private static string Get(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static bool HasValue(JsonElement obj, string name) =>
        !string.IsNullOrWhiteSpace(Get(obj, name));
    private static string OneLine(string value, int max) =>
        (value.Length > max ? value[..max] : value)
        .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    private static void Append(string filePath, string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                if (!_statusWritten)
                {
                    _statusWritten = true;
                    File.WriteAllText(StatusPath,
                        "NIRA diagnostic capture for this process\n" +
                        $"PID={Environment.ProcessId}\n" +
                        $"EventLog={EventLogPath}\n" +
                        $"LlmTranscript={LlmLogPath}\n" +
                        $"PromptCapture={LlmPromptCaptureEnabled}\n" +
                        $"ResponseCapture={LlmResponseCaptureEnabled}\n" +
                        $"DetailedToolCapture={DetailedToolCaptureEnabled}\n" +
                        $"PublicResponseCapture={PublicResponseCaptureEnabled}\n" +
                        $"PublicResponseLog={PublicResponseLogPath}\n" +
                        "To capture delivered Markdown and native blocks: NIRA_DEBUG_PUBLIC_RESPONSES=1\n" +
                        "To capture the raw model response: NIRA_DEBUG_LLM_RESPONSES=1\n" +
                        "To enable full LLM prompts/responses before launch: " +
                        "NIRA_DEBUG_LLM_TRANSCRIPTS=1\n" +
                        "To disable capture even under a debugger: NIRA_DEBUG_LLM_TRANSCRIPTS=0\n" +
                        "IMPORTANT: transcripts contain private conversation and tool context. " +
                        "Keep locally; inspect/redact before sharing.\n", Encoding.UTF8);
                }
                // Keep logs bounded, but allow a full multi-round prompt trace
                // to stay intact during one ordinary debugging run.
                long limit = string.Equals(filePath, LlmLogPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(filePath, PublicResponseLogPath, StringComparison.OrdinalIgnoreCase)
                    ? 32_000_000 : 4_000_000;
                if (File.Exists(filePath) && new FileInfo(filePath).Length > limit)
                    File.Move(filePath, filePath + ".previous", overwrite: true);
                File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException) { }
    }
}
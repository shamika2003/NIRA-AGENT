using System.Net.Http.Json;
using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.Ollama;

/// <summary>
/// Read-only Phase 1A pilot. It cannot request tools, browse the web, or modify user data.
/// The Phase 0 engine remains the source of truth for checkpoints and activity events.
/// </summary>
public sealed class OllamaReasoner : IAgentReasoner
{
    private readonly HttpClient _http;
    private readonly OllamaReasonerOptions _options;
    private const int MaximumResponseBytes = 1024 * 1024;
    private const int MaximumPromptChars = 12000;

    private const string SystemPrompt = """
        You are a temporary NIRA V2 model-connection pilot, not the full NIRA personality or memory system.
        Choose whether you can answer the user's request without external tools. This session has NO web,
        filesystem, image, computer-control, or source-verification tools. Do not claim to have used them.
        Explain the answer clearly and use uncertainty when necessary. If the user asks you to research
        current or source-dependent facts you cannot verify, choose cannot_complete instead of inventing.
        Return JSON only: kind = answer or cannot_complete. Supply answer for answer; supply reason_code
        for cannot_complete. Never output a list of search results or unverified citations.
        """;

    public OllamaReasoner(HttpClient http, OllamaReasonerOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task<AgentDecision> DecideAsync(AgentRunSnapshot state,
        IReadOnlyList<AgentToolSpec> availableTools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        // This release intentionally does NOT expose tools until the typed argument vault is ready.
        if (availableTools.Count != 0)
            return AgentDecision.Stop("PHASE1A_TOOLS_DISABLED");
        if (state.UserRequest.Length > MaximumPromptChars)
            return AgentDecision.Stop("PROMPT_TOO_LARGE");
        if (state.Observations.Count != 0)
            return AgentDecision.Stop("PHASE1A_OBSERVATIONS_UNSUPPORTED");

        var payload = new
        {
            model = _options.Model,
            stream = false,
            format = new
            {
                type = "object",
                properties = new
                {
                    kind = new { type = "string", @enum = new[] { "answer", "cannot_complete" } },
                    answer = new { type = "string" },
                    reason_code = new { type = "string" }
                },
                required = new[] { "kind", "answer", "reason_code" },
                additionalProperties = false
            },
            options = new { temperature = 0.2 },
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = state.UserRequest }
            }
        };

        // No auto-redirects, cookies, external URL fetches or bearer tokens are configured here.
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(_options.Endpoint, "api/chat"))
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Ollama returned an unsuccessful status.", null, response.StatusCode);
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidDataException("Model response exceeds size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        byte[] block = new byte[8192];
        int received;
        while ((received = await stream.ReadAsync(block, cancellationToken)) != 0)
        {
            if (buffer.Length + received > MaximumResponseBytes)
                throw new InvalidDataException("Model response exceeds size limit.");
            buffer.Write(block, 0, received);
        }
        buffer.Position = 0;
        using var doc = await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True ||
            !doc.RootElement.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Incomplete or invalid Ollama chat response.");

        string json = content.GetString() ?? string.Empty;
        if (json.Length > 100_000)
            throw new InvalidDataException("Model decision too large.");
        using var decision = JsonDocument.Parse(json);
        var root = decision.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
            return AgentDecision.Stop("INVALID_MODEL_DECISION");

        string? type = kind.GetString();
        if (type == "answer" && root.TryGetProperty("answer", out var answer) &&
            answer.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(answer.GetString()))
            return AgentDecision.Respond(answer.GetString()!);
        if (type == "cannot_complete")
            return AgentDecision.Stop("MODEL_CANNOT_COMPLETE_WITHOUT_TOOLS");
        return AgentDecision.Stop("INVALID_MODEL_DECISION");
    }
}

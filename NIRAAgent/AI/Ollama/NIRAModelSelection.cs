using System.Text.Json;
using System.Text.Json.Nodes;

namespace NIRAAgent.AI.Ollama;

// Stable semantic roles belong to NIRA/ELVARA, never to the cloud model provider.
// They describe why a reasoning resource is selected, not a second personality.
public enum NIRAReasoningRole
{
    Adaptive,
    DeepWork
}

// NIRA has one identity and runtime. These are interchangeable reasoning
// resources, not separate agents or character/memory owners.
public static class NIRAModelSelection
{
    public const string PrimaryDefault = "gemma4:31b-cloud";
    public const string DeepDefault = "nemotron-3-ultra:cloud";

    // Used only for identifying source/runtime alignment in diagnostics and tests.
    public const string RoutingRevision = "NIRA-DEEP-EXEC-20261014-R5";

    public static string Primary => Resolve("NIRA_OLLAMA_PRIMARY_MODEL", PrimaryDefault);
    public static string Deep => Resolve("NIRA_OLLAMA_DEEP_MODEL", DeepDefault);

    // Stable product/runtime role identities. Changing the underlying provider
    // or version never renames NIRA or changes her character and shared Executive.
    public const string AdaptiveRoleName = "NIRA Adaptive";
    public const string DeepWorkRoleName = "NIRA Deep Work";

    public static string NameForRole(NIRAReasoningRole role) => role switch
    {
        NIRAReasoningRole.Adaptive => AdaptiveRoleName,
        NIRAReasoningRole.DeepWork => DeepWorkRoleName,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    // Provider selection and role naming stay separate. Compatibility accessors
    // Primary/Deep remain intact for existing response/vision/memory consumers.
    public static string ModelForRole(NIRAReasoningRole role) => role switch
    {
        NIRAReasoningRole.Adaptive => Primary,
        NIRAReasoningRole.DeepWork => Deep,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    // A run selects its reasoning resource by persisted delegation state, never
    // by user text, tool names, or an additional classifier LLM call.
    public static string ForCognition(bool deepRunActive) =>
        ModelForRole(deepRunActive ? NIRAReasoningRole.DeepWork : NIRAReasoningRole.Adaptive);

    private static string Resolve(string variable, string fallback)
    {
        string? configured = Environment.GetEnvironmentVariable(variable)?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }

    // A request originates from the CURRENT main cognition JSON, never from
    // matching user phrases, web content, or a second classification model.
    // The entire provisional Gemma decision is discarded if delegation occurs;
    // NIRA's Executive only receives the deep model's validated decision.
    public static bool TryReadDelegation(string response, out string focus)
    {
        focus = string.Empty;
        if (string.IsNullOrWhiteSpace(response)) return false;
        try
        {
            string json = response.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = json.IndexOf('\n');
                int end = json.LastIndexOf("```", StringComparison.Ordinal);
                if (newline < 0 || end <= newline) return false;
                json = json[(newline + 1)..end].Trim();
            }
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("deepReasoningRequest", out JsonElement request) ||
                request.ValueKind != JsonValueKind.Object ||
                !request.TryGetProperty("focus", out JsonElement value) ||
                value.ValueKind != JsonValueKind.String)
                return false;

            string? proposed = value.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(proposed) || proposed.Length > 600)
                return false;
            focus = proposed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Syntax-only preflight. No heuristic changes to a model's actions,
    // permissions, tool arguments or other untrusted source material.
    public static bool HasValidJsonObjectSyntax(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return false;
        try
        {
            string json = response.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = json.IndexOf('\n');
                int end = json.LastIndexOf("```", StringComparison.Ordinal);
                if (newline < 0 || end <= newline) return false;
                json = json[(newline + 1)..end].Trim();
            }
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The specialist has a SEPARATE reasoning-only contract. Never treat its
    // output as a capability proposal, instruction, or NIRA personality state.
    // Parsing happens entirely locally: no extra LLM classification call.
    public static bool TryReadDeepAnalysis(string response, out string analysis)
    {
        analysis = string.Empty;
        if (string.IsNullOrWhiteSpace(response)) return false;
        try
        {
            string json = response.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = json.IndexOf('\n');
                int end = json.LastIndexOf("```", StringComparison.Ordinal);
                if (newline < 0 || end <= newline) return false;
                json = json[(newline + 1)..end].Trim();
            }

            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("analysis", out JsonElement result) ||
                result.ValueKind != JsonValueKind.String)
                return false;

            string? proposed = result.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(proposed) || proposed.Length < 25 ||
                proposed.Length > 16000)
                return false;
            analysis = proposed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Strict local preflight before the Executive sees an Ultra decision.
    // An analysis-only JSON object (or a nested routing instruction) is NOT
    // a completed tool/action decision, even though deserialization would
    // otherwise supply default record values. No extra LLM call is used.
    public static bool IsUsableDeepDecision(string response, bool allowSilentTerminal = false)
    {
        if (string.IsNullOrWhiteSpace(response)) return false;
        try
        {
            string json = response.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = json.IndexOf('\n');
                int end = json.LastIndexOf("```", StringComparison.Ordinal);
                if (newline < 0 || end <= newline) return false;
                json = json[(newline + 1)..end].Trim();
            }
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("state", out JsonElement stateElement) ||
                stateElement.ValueKind != JsonValueKind.String)
                return false;
            string? state = stateElement.GetString();
            bool emit = root.TryGetProperty("emitReply", out JsonElement emission) &&
                emission.ValueKind == JsonValueKind.True;
            bool hasReply = root.TryGetProperty("reply", out JsonElement reply) &&
                reply.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(reply.GetString());
            if (state is "Complete" or "NeedUser" or "Blocked")
            {
                if (emit && hasReply) return true;
                // Background work may commit a truthful internal result without
                // producing a user-facing message. This never applies to a user turn.
                return allowSilentTerminal && !emit &&
                    root.TryGetProperty("decisionSummary", out JsonElement internalSummary) &&
                    internalSummary.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(internalSummary.GetString());
            }
            if (state == "Wait")
                return root.TryGetProperty("decisionSummary", out JsonElement summary) &&
                    summary.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(summary.GetString());
            if (state != "Continue") return false;
            string[] arrays = {
                "capabilityRequests", "capabilityIds", "contextRequests", "memorySearches",
                "conversationSearches", "controlRequests", "goalProposals", "branchProposals",
                "branchWorkProposals", "dynamicToolProposals", "dynamicToolInvocations",
                "visualPresentations"
            };
            return arrays.Any(key => root.TryGetProperty(key, out JsonElement requests) &&
                requests.ValueKind == JsonValueKind.Array && requests.GetArrayLength() > 0);
        }
        catch (JsonException) { return false; }
    }

    // Defensive, zero-call repair for the specialist's response only.
    // We do NOT trust a bare request to delegate again as a completed answer.
    // Only remove a redundant top-level field if the model also supplied a
    // visible terminal decision or a real next-step request for the Executive.
    public static bool TryRecoverDeepDecision(string response, out string recoveredJson)
    {
        recoveredJson = string.Empty;
        if (!TryReadDelegation(response, out _)) return false;

        try
        {
            string json = response.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = json.IndexOf('\n');
                int end = json.LastIndexOf("```", StringComparison.Ordinal);
                if (newline < 0 || end <= newline) return false;
                json = json[(newline + 1)..end].Trim();
            }

            if (JsonNode.Parse(json) is not JsonObject decision) return false;
            string state = ReadString(decision, "state");
            bool emitReply = decision["emitReply"] is JsonValue flag &&
                flag.TryGetValue<bool>(out bool emit) && emit;
            bool hasVisibleResult =
                (state is "Complete" or "NeedUser" or "Blocked") &&
                emitReply && !string.IsNullOrWhiteSpace(ReadString(decision, "reply"));
            bool hasNextStep = state == "Continue" && new[]
            {
                "capabilityRequests", "capabilityIds", "contextRequests", "memorySearches",
                "conversationSearches", "controlRequests", "goalProposals",
                "branchProposals", "branchWorkProposals", "dynamicToolProposals",
                "dynamicToolInvocations", "visualPresentations"
            }.Any(key => decision[key] is JsonArray requests && requests.Count > 0);
            if (!hasVisibleResult && !hasNextStep) return false;

            decision.Remove("deepReasoningRequest");
            recoveredJson = decision.ToJsonString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string ReadString(JsonObject obj, string property) =>
        obj[property] is JsonValue value && value.TryGetValue<string>(out string? text)
            ? text?.Trim() ?? string.Empty : string.Empty;

}

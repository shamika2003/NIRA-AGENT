using System.Text.Json;

namespace NIRAAgent.AI.Ollama;

// NIRA has one identity and runtime. These are interchangeable reasoning
// resources, not separate agents or character/memory owners.
public static class NIRAModelSelection
{
    public const string PrimaryDefault = "gemma4:31b-cloud";
    public const string DeepDefault = "nemotron-3-ultra:cloud";

    public static string Primary => Resolve("NIRA_OLLAMA_PRIMARY_MODEL", PrimaryDefault);
    public static string Deep => Resolve("NIRA_OLLAMA_DEEP_MODEL", DeepDefault);

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
}

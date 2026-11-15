namespace NIRAAgent.AI.Models;

/// <summary>
/// Single active model endpoint for the legacy adapter during the V2 transition.
/// This module owns provider protocol defaults, not NIRA personality, memories or tools.
/// Replace this adapter to change providers; no cognition service hardcodes a vendor.
/// </summary>
public static class NIRAActiveModelProvider
{
    public const string DefaultModel = "gemma4:31b";
    public const string ProcessCredentialName = "NIRA_OLLAMA_API_KEY";
    public const string AlternateCredentialName = "OLLAMA_API_KEY";
    public static Uri ChatEndpoint { get; } =
        new("https://ollama.com/v1/chat/completions");
    public static void ConfigureRequest(IDictionary<string,object> payload)
    {
        // Ollama Cloud OpenAI-compatible chat. Reasoning is a model resource,
        // not NIRA's identity or authority. Do not send NVIDIA NIM-only fields.
        payload["max_tokens"] = 8192;
        payload["reasoning_effort"] = "medium";
    }
}

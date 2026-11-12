namespace NIRAAgent.AI.Models;

/// <summary>
/// Single active model endpoint for the legacy adapter during the V2 transition.
/// This module owns provider protocol defaults, not NIRA personality, memories or tools.
/// Replace this adapter to change providers; no cognition service hardcodes a vendor.
/// </summary>
public static class NIRAActiveModelProvider
{
    public const string DefaultModel = "nvidia/nemotron-3.5-lightning-30b-a3b";
    public const string ProcessCredentialName = "NIRA_NVIDIA_API_KEY";
    public const string AlternateCredentialName = "NVIDIA_API_KEY";
    public static Uri ChatEndpoint { get; } =
        new("https://integrate.api.nvidia.com/v1/chat/completions");
    public static void ConfigureRequest(IDictionary<string,object> payload)
    {
        payload["max_tokens"] = 4096;
        payload["reasoning_budget"] = 768;
        payload["chat_template_kwargs"] = new { enable_thinking = true };
    }
}

namespace Nira.V2.ModelProvider;

/// <summary>
/// Transport-only endpoint and model. Never contains credentials, NIRA identity, or permissions.
/// New providers are registered here without changing NIRA agent state or tool execution.
/// </summary>
public sealed record NiraModelEndpointOptions(Uri Endpoint, string Model)
{
    public static NiraModelEndpointOptions Nvidia(string model) =>
        new(new Uri("https://integrate.api.nvidia.com/v1/"), model);

    public bool IsNvidia => Endpoint.Scheme == Uri.UriSchemeHttps &&
        Endpoint.Host.Equals("integrate.api.nvidia.com", StringComparison.OrdinalIgnoreCase) &&
        Endpoint.IsDefaultPort && Endpoint.AbsolutePath == "/v1/";

    // Host-neutral reasoner calls this provider adapter for protocol-specific settings.
    // NIRA's memory, capability registry and authorization never depend on these.
    public Func<string?> CredentialSource => NvidiaCloudCredentials.ReadExistingKey;

    public void ConfigureRequest(IDictionary<string,object> payload, bool hasTools, bool fastConversation = false)
    {
        Validate();
        // Only host-selected light social turns use the low-latency profile.
        // Tools and all authorization gates remain available and unchanged.
        payload["temperature"] = fastConversation ? 0.55 : 0.15;
        payload["max_tokens"] = fastConversation ? 768 : 4096;
        if (fastConversation) payload.Remove("reasoning_budget");
        else payload["reasoning_budget"] = 768;
        payload["chat_template_kwargs"] = new { enable_thinking = !fastConversation };
        if (hasTools) payload["tool_choice"] = "auto";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 160 ||
            !System.Text.RegularExpressions.Regex.IsMatch(Model, @"^[A-Za-z0-9][A-Za-z0-9._/-]{0,159}$"))
            throw new ArgumentException("Invalid model identifier.", nameof(Model));
        // Strictly one authorized hosted endpoint. No user-controlled URL or redirect.
        if (!IsNvidia || !string.IsNullOrEmpty(Endpoint.UserInfo) ||
            !string.IsNullOrEmpty(Endpoint.Query) || !string.IsNullOrEmpty(Endpoint.Fragment))
            throw new ArgumentException("Unsupported model transport endpoint.", nameof(Endpoint));
    }
}

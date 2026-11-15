namespace Nira.V2.ModelProvider;

/// <summary>
/// Active model-provider selection only: one NIRA identity, one host, one authorization system.
/// A future provider adapter changes this module, never NIRA's lifecycle or memory owners.
/// </summary>
public sealed record NiraV2ProviderSelection(
    string Provider, NiraModelEndpointOptions Options, Func<string?> KeySource,
    bool RequiresCredential)
{
    public static NiraV2ProviderSelection FromEnvironment()
    {
        string provider = (Environment.GetEnvironmentVariable("NIRA_V2_PROVIDER") ?? "OLLAMA")
            .Trim().ToUpperInvariant();
        string? requested = Environment.GetEnvironmentVariable("NIRA_V2_MODEL")?.Trim();
        if (provider == "OLLAMA")
        {
            string model = string.IsNullOrWhiteSpace(requested) ? "gemma4:31b" : requested;
            var ollama = NiraModelEndpointOptions.Ollama(model);
            ollama.Validate();
            return new NiraV2ProviderSelection("OLLAMA", ollama,
                OllamaCloudCredentials.ReadExistingKey, true);
        }
        if (provider == "NVIDIA")
        {
            string model = string.IsNullOrWhiteSpace(requested)
                ? "nvidia/nemotron-3.5-lightning-30b-a3b" : requested;
            var nvidia = NiraModelEndpointOptions.Nvidia(model);
            nvidia.Validate();
            return new NiraV2ProviderSelection("NVIDIA", nvidia,
                NvidiaCloudCredentials.ReadExistingKey, true);
        }
        throw new ArgumentException("Unsupported model provider. Use OLLAMA or NVIDIA.");
    }

    public bool IsReady => !RequiresCredential || !string.IsNullOrWhiteSpace(KeySource());
}

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
        string provider = (Environment.GetEnvironmentVariable("NIRA_V2_PROVIDER") ?? "NVIDIA")
            .Trim().ToUpperInvariant();
        if (provider != "NVIDIA")
            throw new ArgumentException("Unsupported model provider. The current installation uses NVIDIA.");
        string? requested = Environment.GetEnvironmentVariable("NIRA_V2_MODEL")?.Trim();
        string model = string.IsNullOrWhiteSpace(requested)
            ? "nvidia/nemotron-3.5-lightning-30b-a3b" : requested;
        var options = NiraModelEndpointOptions.Nvidia(model);
        options.Validate();
        return new NiraV2ProviderSelection("NVIDIA", options,
            NvidiaCloudCredentials.ReadExistingKey, true);
    }

    public bool IsReady => !RequiresCredential || !string.IsNullOrWhiteSpace(KeySource());
}

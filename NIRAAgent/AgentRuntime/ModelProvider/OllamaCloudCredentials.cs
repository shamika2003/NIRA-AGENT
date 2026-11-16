namespace NIRAAgent.Runtime.ModelProvider;

/// <summary>Ollama Cloud key is process-only, never checkpointed or given to a model.</summary>
public static class OllamaCloudCredentials
{
    public static string? ReadExistingKey()
    {
        foreach (string name in new[] { "NIRA_OLLAMA_API_KEY", "OLLAMA_API_KEY" })
        {
            string? value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }
}

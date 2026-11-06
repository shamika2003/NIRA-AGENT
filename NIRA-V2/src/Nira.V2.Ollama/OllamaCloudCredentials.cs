namespace Nira.V2.Ollama;

/// <summary>
/// Reads an existing operator-managed API key. Never writes credentials to checkpoints,
/// configuration, telemetry, exception messages, or the chat transcript.
/// </summary>
public static class OllamaCloudCredentials
{
    public static string? ReadExistingKey()
    {
        foreach (string name in new[] { "NIRA_OLLAMA_API_KEY", "OLLAMA_API_KEY" })
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        if (OperatingSystem.IsWindows())
        {
            foreach (string name in new[] { "NIRA_OLLAMA_API_KEY", "OLLAMA_API_KEY" })
            {
                string? value = Environment.GetEnvironmentVariable(name,
                    EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
        }
        return null;
    }
}

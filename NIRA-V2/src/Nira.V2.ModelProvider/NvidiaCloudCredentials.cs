namespace Nira.V2.ModelProvider;

/// <summary>Credentials never flow through the model prompt, checkpoint or capability registry.</summary>
public static class NvidiaCloudCredentials
{
    public static string? ReadExistingKey()
    {
        foreach (string name in new[] { "NIRA_NVIDIA_API_KEY", "NVIDIA_API_KEY" })
        {
            string? value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }
}

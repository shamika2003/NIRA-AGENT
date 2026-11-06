namespace Nira.V2.Ollama;

/// <summary>Server root and model. Never includes an API key.</summary>
public sealed record OllamaReasonerOptions(Uri Endpoint, string Model)
{
    public static OllamaReasonerOptions Local(string model,
        string? endpoint = null) => new(new Uri(endpoint ?? "http://127.0.0.1:11434/"), model);

    // Ollama's direct-cloud model IDs omit the -cloud suffix used by the local CLI.
    public static OllamaReasonerOptions Cloud(string model) => new(
        new Uri("https://ollama.com/"),
        model.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase)
            ? model[..^6] : model);

    public bool IsDirectCloud => Endpoint.Scheme == Uri.UriSchemeHttps &&
        Endpoint.Host.Equals("ollama.com", StringComparison.OrdinalIgnoreCase) &&
        Endpoint.IsDefaultPort && Endpoint.AbsolutePath == "/";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 160)
            throw new ArgumentException("Choose a model.", nameof(Model));
        if (!Endpoint.IsAbsoluteUri || Endpoint.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(Endpoint.UserInfo) || !string.IsNullOrEmpty(Endpoint.Query) ||
            !string.IsNullOrEmpty(Endpoint.Fragment))
            throw new ArgumentException("Invalid Ollama API endpoint.", nameof(Endpoint));
        if (Endpoint.Scheme != "https" && !Endpoint.IsLoopback)
            throw new ArgumentException("Remote Ollama endpoints must use HTTPS.", nameof(Endpoint));
        if (Endpoint.AbsolutePath != "/")
            throw new ArgumentException("Ollama endpoint must be the server root URL.", nameof(Endpoint));
        if (Endpoint.Host.Equals("ollama.com", StringComparison.OrdinalIgnoreCase) &&
            !IsDirectCloud)
            throw new ArgumentException("Direct Ollama Cloud requires https://ollama.com/ on the default HTTPS port.", nameof(Endpoint));
    }
}

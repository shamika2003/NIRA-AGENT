namespace Nira.V2.Ollama;

public sealed record OllamaReasonerOptions(Uri Endpoint, string Model)
{
    public static OllamaReasonerOptions Local(string model,
        string? endpoint = null) => new(new Uri(endpoint ?? "http://127.0.0.1:11434/"), model);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 160)
            throw new ArgumentException("Choose an installed Ollama model.", nameof(Model));
        if (!Endpoint.IsAbsoluteUri || Endpoint.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(Endpoint.UserInfo) || !string.IsNullOrEmpty(Endpoint.Query) ||
            !string.IsNullOrEmpty(Endpoint.Fragment))
            throw new ArgumentException("Invalid Ollama API endpoint.", nameof(Endpoint));
        if (Endpoint.Scheme != "https" && !Endpoint.IsLoopback)
            throw new ArgumentException("Remote Ollama endpoints must use HTTPS.", nameof(Endpoint));
        if (Endpoint.AbsolutePath != "/")
            throw new ArgumentException("Ollama endpoint must be the server root URL.", nameof(Endpoint));
    }
}

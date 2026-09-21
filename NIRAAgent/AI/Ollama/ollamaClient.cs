/*
 * filename: OllamaClient.cs
 */

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NIRAAgent.AI.Ollama;

public sealed class OllamaClient : IDisposable
{
    private const string BaseUrl = "https://ollama.com";

    private const string ModelName =
        "gpt-oss:120b-cloud";

    private readonly HttpClient _httpClient;
    private readonly NIRAOllamaApiKeyService _apiKeys;
    private bool _disposed;

    public OllamaClient(
        NIRAOllamaApiKeyService apiKeys)
    {
        _apiKeys = apiKeys ?? throw new ArgumentNullException(nameof(apiKeys));

        // Dedicated client: an Ollama bearer token is never placed on the
        // application's shared HttpClient or leaked to another provider.
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    // =========================================================
    // NORMAL CHAT
    // =========================================================

    public async Task<string> ChatAsync(
        string ModelName,
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var request = new
        {
            model = ModelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            stream = false
        };

        string json = JsonSerializer.Serialize(request);

        Guid callId = Guid.NewGuid();
        Stopwatch timer = Stopwatch.StartNew();
        Debug.WriteLine($"[LLM] START | Call={callId:D} | Model={ModelName} | " +
            $"SystemChars={systemPrompt.Length} | UserChars={userMessage.Length}");
        try
        {
            string answer = await SendNonStreamingChatJsonAsync(json, cancellationToken);
            Debug.WriteLine($"[LLM] END | Call={callId:D} | " +
                $"ElapsedMs={timer.ElapsedMilliseconds} | ResponseChars={answer.Length}");
            return answer;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LLM] FAILED | Call={callId:D} | " +
                $"ElapsedMs={timer.ElapsedMilliseconds} | ErrorType={ex.GetType().Name}");
            throw;
        }
    }

    // =========================================================
    // MULTIMODAL CHAT
    //
    // Ollama's REST chat API accepts base64-encoded image data in
    // the user message's `images` array. This transport method does
    // not interpret the image itself; callers still own prompting,
    // grounding, structured-output validation and evidence policy.
    // =========================================================

    public async Task<string> ChatWithImagesAsync(
        string modelName,
        string systemPrompt,
        string userMessage,
        IReadOnlyList<string> imagePaths,
        bool jsonMode = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(imagePaths);

        string[] paths = imagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
        {
            throw new InvalidOperationException(
                "Multimodal Ollama chat requires at least one image.");
        }

        if (paths.Length > 4)
        {
            throw new InvalidOperationException(
                "Multimodal Ollama chat is limited to four images per request.");
        }

        const long maximumImageBytes = 20L * 1024L * 1024L;
        const long maximumTotalBytes = 32L * 1024L * 1024L;
        long totalBytes = 0;
        List<string> images = new(paths.Length);

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo file = new(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException(
                    "Multimodal Ollama image file does not exist.",
                    path);
            }

            if (file.Length <= 0 || file.Length > maximumImageBytes)
            {
                throw new InvalidOperationException(
                    $"Vision image '{file.Name}' has unsupported size {file.Length} bytes.");
            }

            totalBytes += file.Length;
            if (totalBytes > maximumTotalBytes)
            {
                throw new InvalidOperationException(
                    "Combined multimodal image payload is too large.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(
                path,
                cancellationToken);

            images.Add(Convert.ToBase64String(bytes));
        }

        object[] messages =
        {
            new
            {
                role = "system",
                content = systemPrompt
            },
            new
            {
                role = "user",
                content = userMessage,
                images = images.ToArray()
            }
        };

        Dictionary<string, object?> request = new()
        {
            ["model"] = modelName.Trim(),
            ["messages"] = messages,
            ["stream"] = false
        };

        if (jsonMode)
        {
            request["format"] = "json";
        }

        string json = JsonSerializer.Serialize(request);

        Guid callId = Guid.NewGuid();
        Stopwatch timer = Stopwatch.StartNew();
        Debug.WriteLine($"[LLM] START | Call={callId:D} | Model={modelName} | " +
            $"Mode=Vision | Images={paths.Length} | " +
            $"SystemChars={systemPrompt.Length} | UserChars={userMessage.Length}");
        try
        {
            string answer = await SendNonStreamingChatJsonAsync(json, cancellationToken);
            Debug.WriteLine($"[LLM] END | Call={callId:D} | " +
                $"ElapsedMs={timer.ElapsedMilliseconds} | ResponseChars={answer.Length}");
            return answer;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LLM] FAILED | Call={callId:D} | " +
                $"ElapsedMs={timer.ElapsedMilliseconds} | ErrorType={ex.GetType().Name}");
            throw;
        }
    }

    private async Task<string> SendNonStreamingChatJsonAsync(
        string json,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response =
            await SendWithCredentialRecoveryAsync(
                json,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

        string responseBody =
            await response.Content.ReadAsStringAsync(cancellationToken);

        using JsonDocument document =
            JsonDocument.Parse(responseBody);

        if (!document.RootElement.TryGetProperty(
                "message",
                out JsonElement message))
        {
            throw new InvalidOperationException(
                "Ollama response did not contain a message.");
        }

        if (!message.TryGetProperty(
                "content",
                out JsonElement contentElement))
        {
            throw new InvalidOperationException(
                "Ollama response did not contain message content.");
        }

        return contentElement.GetString() ?? string.Empty;
    }

    // =========================================================
    // STREAMING CHAT
    // =========================================================

    public async IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt,
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var request = new
        {
            model = ModelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            stream = true
        };

        string json = JsonSerializer.Serialize(request);

        using HttpResponseMessage response =
            await SendWithCredentialRecoveryAsync(
                json,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        using StreamReader reader =
            new(stream, Encoding.UTF8);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line =
                await reader.ReadLineAsync(cancellationToken);

            if (line == null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("done", out JsonElement doneElement) &&
                    doneElement.ValueKind == JsonValueKind.True)
                {
                    yield break;
                }

                if (!root.TryGetProperty("message", out JsonElement messageElement) ||
                    !messageElement.TryGetProperty("content", out JsonElement contentElement))
                {
                    continue;
                }

                string? text = contentElement.GetString();

                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                }
            }
        }
    }

    // =========================================================
    // AUTH / USAGE RECOVERY
    //
    // A failed cloud request may need user action. We prompt once, update the
    // environment-backed key, and retry exactly once. Generic rate/concurrency
    // failures are NOT treated as exhausted credits.
    // =========================================================

    private async Task<HttpResponseMessage> SendWithCredentialRecoveryAsync(
        string json,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        string key = _apiKeys.GetRequiredKey();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            HttpResponseMessage response =
                await SendAsync(
                    json,
                    key,
                    completionOption,
                    cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                // Ollama cloud has historically had edge cases where an
                // exhausted usage endpoint can answer 200 with an explicitly
                // empty body. /api/chat should never produce a zero-byte
                // successful response, so treat that as a usage-attention
                // signal instead of letting NIRA fail later with JSON errors.
                if (response.Content.Headers.ContentLength != 0)
                {
                    return response;
                }

                if (attempt > 0)
                {
                    response.Dispose();
                    throw new HttpRequestException(
                        "Ollama returned an empty successful response after credential retry.");
                }

                response.Dispose();

                key = await _apiKeys.ResolveProblemAsync(
                    NIRAOllamaCredentialProblem.UsageUnavailable,
                    key,
                    "Ollama returned an empty cloud response; cloud usage may be unavailable.",
                    cancellationToken);

                continue;
            }

            string errorBody =
                await response.Content.ReadAsStringAsync(cancellationToken);

            NIRAOllamaCredentialProblem? problem =
                ClassifyCredentialProblem(
                    response.StatusCode,
                    errorBody);

            if (problem == null || attempt > 0)
            {
                string message = BuildFailureMessage(response, errorBody);
                response.Dispose();
                throw new HttpRequestException(message);
            }

            response.Dispose();

            key = await _apiKeys.ResolveProblemAsync(
                problem.Value,
                key,
                BuildSafeDetail(problem.Value, errorBody),
                cancellationToken);
        }

        throw new InvalidOperationException(
            "Ollama request retry state was invalid.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        string json,
        string key,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request =
            new(
                HttpMethod.Post,
                $"{BaseUrl}/api/chat");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                key);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        request.Content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

        return await _httpClient.SendAsync(
            request,
            completionOption,
            cancellationToken);
    }

    private static NIRAOllamaCredentialProblem? ClassifyCredentialProblem(
        HttpStatusCode statusCode,
        string responseBody)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return NIRAOllamaCredentialProblem.InvalidOrRevoked;
        }

        string normalized = responseBody.ToLowerInvariant();

        bool usageLanguage =
            normalized.Contains("credit", StringComparison.Ordinal) ||
            normalized.Contains("insufficient", StringComparison.Ordinal) ||
            normalized.Contains("balance", StringComparison.Ordinal) ||
            normalized.Contains("usage limit", StringComparison.Ordinal) ||
            normalized.Contains("usage quota", StringComparison.Ordinal) ||
            normalized.Contains("quota exceeded", StringComparison.Ordinal) ||
            normalized.Contains("out of usage", StringComparison.Ordinal) ||
            normalized.Contains("exhausted", StringComparison.Ordinal);

        if ((int)statusCode == 402 || usageLanguage)
        {
            return NIRAOllamaCredentialProblem.UsageUnavailable;
        }

        // Do not classify a plain 429 as exhausted credits. Ollama also uses
        // plan concurrency limits, and replacing a key is not the right fix.
        return null;
    }

    private static string BuildSafeDetail(
        NIRAOllamaCredentialProblem problem,
        string responseBody)
    {
        if (problem == NIRAOllamaCredentialProblem.InvalidOrRevoked)
        {
            return "Ollama rejected the current API credential.";
        }

        return "Ollama reported that cloud usage is unavailable for the current credential/account.";
    }

    private static string BuildFailureMessage(
        HttpResponseMessage response,
        string responseBody)
    {
        const int maxBody = 2400;
        string body = responseBody.Length <= maxBody
            ? responseBody
            : responseBody[..maxBody] + "…";

        return
            $"Ollama request failed. " +
            $"Status: {(int)response.StatusCode} {response.ReasonPhrase}. " +
            $"Response: {body}";
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }
}

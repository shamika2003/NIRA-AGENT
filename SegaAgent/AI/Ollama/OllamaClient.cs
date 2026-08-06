/*
 * filename: OllamaClient.cs
 */

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SegaAgent.AI.Ollama;

public sealed class OllamaClient
{
    private const string BaseUrl = "https://ollama.com";

    private const string ModelName =
        "gpt-oss:120b-cloud";

    private readonly HttpClient _httpClient;

    private readonly string _apiKey;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public OllamaClient(
        HttpClient httpClient)
    {
        _httpClient = httpClient;


        _apiKey =
            Environment.GetEnvironmentVariable(
                "SEGA_OLLAMA_API_KEY"
            )
            ?? throw new InvalidOperationException(
                "SEGA_OLLAMA_API_KEY environment variable is not configured."
            );


        _httpClient.BaseAddress =
            new Uri(BaseUrl);


        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _apiKey
            );


        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"
                )
            );
        }


        _httpClient.Timeout =
            TimeSpan.FromMinutes(5);
    }


    // =========================================================
    // NORMAL CHAT
    //
    // This waits for the complete response.
    //
    // Planner can continue using this.
    // =========================================================

    public async Task<string> ChatAsync(
        string ModelName,
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            model = ModelName,

            messages = new[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },

                new
                {
                    role = "user",
                    content = userMessage
                }
            },

            stream = false
        };


        var json =
            JsonSerializer.Serialize(request);


        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );


        using var response =
            await _httpClient.PostAsync(
                "/api/chat",
                content,
                cancellationToken
            );


        var responseBody =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );


        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Ollama request failed. " +
                $"Status: {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}. " +
                $"Response: {responseBody}"
            );
        }


        using var document =
            JsonDocument.Parse(
                responseBody
            );


        if (!document.RootElement.TryGetProperty(
                "message",
                out var message))
        {
            throw new InvalidOperationException(
                "Ollama response did not contain a message."
            );
        }


        if (!message.TryGetProperty(
                "content",
                out var contentElement))
        {
            throw new InvalidOperationException(
                "Ollama response did not contain message content."
            );
        }


        return contentElement.GetString()
            ?? string.Empty;
    }


    // =========================================================
    // STREAMING CHAT
    //
    // Ollama sends multiple JSON objects.
    //
    // Example:
    //
    // {"message":{"content":"Hello"},"done":false}
    // {"message":{"content":" there"},"done":false}
    // {"message":{"content":"!"},"done":false}
    // {"done":true}
    //
    // We expose only the text chunks.
    // =========================================================

    public async IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt,
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            model = ModelName,

            messages = new[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },

                new
                {
                    role = "user",
                    content = userMessage
                }
            },

            stream = true
        };


        var json =
            JsonSerializer.Serialize(request);


        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );


        using var requestMessage =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/chat"
            )
            {
                Content = content
            };


        using var response =
            await _httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );


        if (!response.IsSuccessStatusCode)
        {
            var errorBody =
                await response.Content.ReadAsStringAsync(
                    cancellationToken
                );


            throw new HttpRequestException(
                $"Ollama streaming request failed. " +
                $"Status: {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}. " +
                $"Response: {errorBody}"
            );
        }


        await using var stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken
            );


        using var reader =
            new StreamReader(
                stream,
                Encoding.UTF8
            );


        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line =
                await reader.ReadLineAsync(
                    cancellationToken
                );

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
                document =
                    JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root =
                    document.RootElement;

                if (root.TryGetProperty(
                        "done",
                        out var doneElement)
                    &&
                    doneElement.ValueKind ==
                        JsonValueKind.True)
                {
                    yield break;
                }

                if (!root.TryGetProperty(
                        "message",
                        out var messageElement))
                {
                    continue;
                }

                if (!messageElement.TryGetProperty(
                        "content",
                        out var contentElement))
                {
                    continue;
                }

                var text =
                    contentElement.GetString();

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                yield return text;
            }
        }
    }
}
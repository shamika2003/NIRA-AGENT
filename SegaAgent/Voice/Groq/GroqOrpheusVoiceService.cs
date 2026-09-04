/*
 * filename: GroqOrpheusVoiceService.cs
 */

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SegaAgent.Voice.Groq;

public sealed class GroqOrpheusVoiceService
    : IVoiceService
{
    // =========================================================
    // GROQ
    // =========================================================

    private const string Endpoint =
        "https://api.groq.com/openai/v1/audio/speech";


    private const string Model =
        "canopylabs/orpheus-v1-english";


    private const string DefaultVoice =
        "hannah";


    private const int MaximumInputCharacters =
        200;


    private static readonly TimeSpan
        RequestTimeout =
            TimeSpan.FromSeconds(
                45);


    private static readonly HashSet<string>
        SupportedVoices =
            new(
                StringComparer.OrdinalIgnoreCase)
            {
                "autumn",
                "diana",
                "hannah",
                "austin",
                "daniel",
                "troy"
            };


    // =========================================================
    // DEPENDENCY
    // =========================================================

    private readonly HttpClient
        _httpClient;


    // =========================================================
    // CONFIGURATION
    // =========================================================

    private readonly string
        _apiKey;


    private readonly string
        _voice;


    // =========================================================
    // HEALTH
    // =========================================================

    private readonly object
        _stateLock =
            new();


    private DateTimeOffset
        _retryAfterUtc =
            DateTimeOffset.MinValue;


    private bool
        _permanentlyUnavailable;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public GroqOrpheusVoiceService(
        HttpClient httpClient)
    {
        _httpClient =
            httpClient
            ?? throw new ArgumentNullException(
                nameof(httpClient));


        _apiKey =
            ReadEnvironment(
                "GROQ_API_KEY");


        string configuredVoice =
            ReadEnvironment(
                "SEGA_GROQ_VOICE");


        _voice =
            string.IsNullOrWhiteSpace(
                configuredVoice)
                ? DefaultVoice
                : configuredVoice
                    .Trim()
                    .ToLowerInvariant();


        Debug.WriteLine(
            $"[GroqVoiceConfig] " +
            $"KeyAvailable=" +
            $"{!string.IsNullOrWhiteSpace(_apiKey)} | " +
            $"Voice='{_voice}' | " +
            $"Configured={IsConfigured}");
    }


    // =========================================================
    // CONFIGURATION
    // =========================================================

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(
            _apiKey)
        &&
        SupportedVoices.Contains(
            _voice);


    public string Voice =>
        _voice;


    // =========================================================
    // AVAILABILITY
    // =========================================================

    public string AvailabilityDescription
    {
        get
        {
            if (!IsConfigured)
            {
                return BuildConfigurationError();
            }


            lock (_stateLock)
            {
                if (_permanentlyUnavailable)
                {
                    return
                        "Groq Orpheus is disabled for this " +
                        "session after an authorization " +
                        "failure.";
                }


                DateTimeOffset now =
                    DateTimeOffset.UtcNow;


                if (now <
                    _retryAfterUtc)
                {
                    TimeSpan remaining =
                        _retryAfterUtc -
                        now;


                    return
                        $"Groq Orpheus is temporarily " +
                        $"cooling down for approximately " +
                        $"{Math.Ceiling(remaining.TotalSeconds)} " +
                        $"seconds.";
                }
            }


            return "Ready";
        }
    }


    public bool CanAttempt
    {
        get
        {
            if (!IsConfigured)
            {
                return false;
            }


            lock (_stateLock)
            {
                if (_permanentlyUnavailable)
                {
                    return false;
                }


                return DateTimeOffset.UtcNow >=
                    _retryAfterUtc;
            }
        }
    }


    // =========================================================
    // PREPARE
    // =========================================================

    public async Task<PreparedVoiceAudio>
        PrepareAsync(
            VoiceUtterance utterance,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            utterance);


        if (!SpeechChunker
            .ContainsSpeakableContent(
                utterance.Text))
        {
            throw new ArgumentException(
                "Groq received a non-speakable utterance.",
                nameof(utterance));
        }


        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                BuildConfigurationError());
        }


        if (!CanAttempt)
        {
            throw new InvalidOperationException(
                AvailabilityDescription);
        }


        cancellationToken
            .ThrowIfCancellationRequested();


        SegaVoiceExpression expression =
            utterance
                .Expression
                .Normalize();


        string directionPrefix =
            GroqVocalDirectionMapper
                .BuildPrefix(
                    expression);


        int availableCharacters =
            MaximumInputCharacters -
            directionPrefix.Length;


        if (availableCharacters <
            40)
        {
            throw new InvalidOperationException(
                "Groq vocal direction consumed too much " +
                "of the Orpheus input limit.");
        }


        IReadOnlyList<string> pieces =
            SplitText(
                utterance.Text,
                availableCharacters);


        List<string> generatedPaths =
            new();


        try
        {
            Debug.WriteLine(
                $"[GroqVoice] PREPARE | " +
                $"Response={utterance.ResponseId} | " +
                $"Sequence={utterance.Sequence} | " +
                $"Voice='{_voice}' | " +
                $"Parts={pieces.Count}");


            for (
                int index = 0;
                index < pieces.Count;
                index++)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();


                string piece =
                    pieces[index];


                if (!SpeechChunker
                    .ContainsSpeakableContent(
                        piece))
                {
                    continue;
                }


                string input =
                    directionPrefix +
                    piece;


                string outputPath =
                    Path.Combine(
                        Path.GetTempPath(),
                        $"sega_groq_" +
                        $"{Guid.NewGuid():N}.wav");


                try
                {
                    Debug.WriteLine(
                        $"[GroqVoice] " +
                        $"Part={index + 1}/{pieces.Count} | " +
                        $"Characters={input.Length}");


                    await SynthesizeAsync(
                        input,
                        outputPath,
                        cancellationToken);


                    generatedPaths.Add(
                        outputPath);
                }
                catch
                {
                    DeleteTemporaryFile(
                        outputPath);


                    throw;
                }
            }


            if (generatedPaths.Count ==
                0)
            {
                throw new InvalidOperationException(
                    "Groq did not produce any speakable audio.");
            }


            return new PreparedVoiceAudio(
                utterance,
                generatedPaths,
                $"Groq Orpheus/{_voice}");
        }
        catch
        {
            foreach (
                string path
                in generatedPaths)
            {
                DeleteTemporaryFile(
                    path);
            }


            throw;
        }
    }


    // =========================================================
    // SYNTHESIZE
    // =========================================================

    private async Task SynthesizeAsync(
        string input,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource
            timeoutCancellation =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);


        timeoutCancellation.CancelAfter(
            RequestTimeout);


        var payload =
            new
            {
                model =
                    Model,

                input,

                voice =
                    _voice,

                response_format =
                    "wav"
            };


        string json =
            JsonSerializer.Serialize(
                payload);


        using HttpRequestMessage request =
            new(
                HttpMethod.Post,
                Endpoint);


        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _apiKey);


        request.Content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json");


        HttpResponseMessage response;


        try
        {
            response =
                await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption
                        .ResponseHeadersRead,
                    timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken
                .IsCancellationRequested)
        {
            MarkTemporaryFailure(
                TimeSpan.FromMinutes(
                    1));


            throw new TimeoutException(
                "Groq Orpheus speech request timed out.");
        }
        catch
        {
            MarkTemporaryFailure(
                TimeSpan.FromSeconds(
                    30));


            throw;
        }


        using (response)
        {
            LogRateLimitHeaders(
                response);


            if (!response.IsSuccessStatusCode)
            {
                string diagnostic;


                try
                {
                    diagnostic =
                        await response
                            .Content
                            .ReadAsStringAsync(
                                timeoutCancellation.Token);
                }
                catch
                {
                    diagnostic =
                        string.Empty;
                }


                HandleFailureResponse(
                    response.StatusCode,
                    response.Headers.RetryAfter);


                throw new HttpRequestException(
                    $"Groq Orpheus returned HTTP " +
                    $"{(int)response.StatusCode} " +
                    $"{response.ReasonPhrase}. " +
                    $"{TrimDiagnostic(diagnostic)}");
            }


            byte[] audio =
                await response
                    .Content
                    .ReadAsByteArrayAsync(
                        timeoutCancellation.Token);


            if (audio.Length ==
                0)
            {
                MarkTemporaryFailure(
                    TimeSpan.FromSeconds(
                        30));


                throw new InvalidOperationException(
                    "Groq Orpheus returned empty audio.");
            }


            await File.WriteAllBytesAsync(
                outputPath,
                audio,
                cancellationToken);


            MarkSuccess();
        }
    }


    // =========================================================
    // FAILURE
    // =========================================================

    private void HandleFailureResponse(
        HttpStatusCode statusCode,
        RetryConditionHeaderValue? retryAfter)
    {
        if (
            statusCode ==
                HttpStatusCode.Unauthorized
            ||
            statusCode ==
                HttpStatusCode.Forbidden)
        {
            lock (_stateLock)
            {
                _permanentlyUnavailable =
                    true;
            }


            return;
        }


        if (
            statusCode ==
                HttpStatusCode.TooManyRequests)
        {
            MarkTemporaryFailure(
                ResolveRetryDelay(
                    retryAfter));


            return;
        }


        if ((int)statusCode >=
            500)
        {
            MarkTemporaryFailure(
                TimeSpan.FromSeconds(
                    30));


            return;
        }


        MarkTemporaryFailure(
            TimeSpan.FromMinutes(
                1));
    }


    // =========================================================
    // RETRY
    // =========================================================

    private static TimeSpan ResolveRetryDelay(
        RetryConditionHeaderValue? retryAfter)
    {
        if (
            retryAfter?.Delta is
                TimeSpan delta
            &&
            delta >
                TimeSpan.Zero)
        {
            return delta;
        }


        if (
            retryAfter?.Date is
                DateTimeOffset date)
        {
            TimeSpan remaining =
                date -
                DateTimeOffset.UtcNow;


            if (remaining >
                TimeSpan.Zero)
            {
                return remaining;
            }
        }


        return TimeSpan.FromMinutes(
            1);
    }


    private void MarkTemporaryFailure(
        TimeSpan duration)
    {
        lock (_stateLock)
        {
            _retryAfterUtc =
                DateTimeOffset.UtcNow +
                duration;
        }
    }


    private void MarkSuccess()
    {
        lock (_stateLock)
        {
            _retryAfterUtc =
                DateTimeOffset.MinValue;
        }
    }


    // =========================================================
    // RATE LIMIT
    // =========================================================

    private static void LogRateLimitHeaders(
        HttpResponseMessage response)
    {
        string requestsRemaining =
            ReadHeader(
                response.Headers,
                "x-ratelimit-remaining-requests");


        string requestsLimit =
            ReadHeader(
                response.Headers,
                "x-ratelimit-limit-requests");


        string tokensRemaining =
            ReadHeader(
                response.Headers,
                "x-ratelimit-remaining-tokens");


        Debug.WriteLine(
            $"[GroqVoiceQuota] " +
            $"RequestsRemaining=" +
            $"{ValueOrUnknown(requestsRemaining)} | " +
            $"RequestLimit=" +
            $"{ValueOrUnknown(requestsLimit)} | " +
            $"TokensRemaining=" +
            $"{ValueOrUnknown(tokensRemaining)}");
    }


    private static string ReadHeader(
        HttpResponseHeaders headers,
        string name)
    {
        if (!headers.TryGetValues(
                name,
                out IEnumerable<string>?
                    values))
        {
            return string.Empty;
        }


        return values
            .FirstOrDefault()?
            .Trim()
            ?? string.Empty;
    }


    private static string ValueOrUnknown(
        string value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "?"
            : value;
    }


    // =========================================================
    // TEXT SPLIT
    // =========================================================

    private static IReadOnlyList<string>
        SplitText(
            string rawText,
            int maximumLength)
    {
        string remaining =
            rawText.Trim();


        List<string> pieces =
            new();


        while (remaining.Length >
            maximumLength)
        {
            int splitIndex =
                FindSplitIndex(
                    remaining,
                    maximumLength);


            if (splitIndex <=
                0)
            {
                splitIndex =
                    maximumLength;
            }


            string piece =
                remaining[
                    ..splitIndex]
                    .Trim();


            if (!string.IsNullOrWhiteSpace(
                    piece))
            {
                pieces.Add(
                    piece);
            }


            remaining =
                remaining[
                    splitIndex..]
                    .TrimStart();
        }


        if (!string.IsNullOrWhiteSpace(
                remaining))
        {
            pieces.Add(
                remaining);
        }


        return pieces;
    }


    // =========================================================
    // FIND SPLIT
    // =========================================================

    private static int FindSplitIndex(
        string text,
        int maximumLength)
    {
        int end =
            Math.Min(
                maximumLength,
                text.Length);


        for (
            int index = end - 1;
            index >= 0;
            index--)
        {
            char character =
                text[index];


            if (
                character ==
                    '.'
                ||
                character ==
                    '!'
                ||
                character ==
                    '?'
                ||
                character ==
                    ';')
            {
                return index +
                    1;
            }
        }


        for (
            int index = end - 1;
            index >= 0;
            index--)
        {
            char character =
                text[index];


            if (
                character ==
                    ','
                ||
                character ==
                    ':'
                ||
                character ==
                    '—')
            {
                return index +
                    1;
            }
        }


        for (
            int index = end - 1;
            index >= 0;
            index--)
        {
            if (char.IsWhiteSpace(
                    text[index]))
            {
                return index +
                    1;
            }
        }


        return end;
    }


    // =========================================================
    // CONFIGURATION ERROR
    // =========================================================

    public string BuildConfigurationError()
    {
        if (string.IsNullOrWhiteSpace(
                _apiKey))
        {
            return
                "GROQ_API_KEY is not configured.";
        }


        if (!SupportedVoices.Contains(
                _voice))
        {
            return
                $"Unsupported Groq voice '{_voice}'.";
        }


        return
            "Groq Orpheus is not configured.";
    }


    // =========================================================
    // ENVIRONMENT
    // =========================================================

    private static string ReadEnvironment(
        string name)
    {
        string? value =
            Environment
                .GetEnvironmentVariable(
                    name);


        if (!string.IsNullOrWhiteSpace(
                value))
        {
            return value.Trim();
        }


        try
        {
            value =
                Environment
                    .GetEnvironmentVariable(
                        name,
                        EnvironmentVariableTarget.User);


            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                return value.Trim();
            }
        }
        catch
        {
        }


        try
        {
            value =
                Environment
                    .GetEnvironmentVariable(
                        name,
                        EnvironmentVariableTarget.Machine);


            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                return value.Trim();
            }
        }
        catch
        {
        }


        return string.Empty;
    }


    // =========================================================
    // DIAGNOSTIC
    // =========================================================

    private static string TrimDiagnostic(
        string value)
    {
        const int maximum =
            500;


        string clean =
            value.Trim();


        return clean.Length <=
            maximum
                ? clean
                : clean[..maximum] +
                    "...";
    }


    // =========================================================
    // TEMP FILE
    // =========================================================

    private static void DeleteTemporaryFile(
        string path)
    {
        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
        }
    }
}
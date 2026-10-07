/*
 * filename: TradeAIClient.cs
 */

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NIRAAgent.Integrations.Elvara.Apps.TradeAI;


// =============================================================
// TRADEAI READ-ONLY CLIENT
//
// NIRA talks only to TradeAI's localhost read-only API.
//
// TradeAI remains the sole owner of:
// - trading decisions
// - MT5 / broker access
// - execution
// - positions
// - risk controls
//
// This client cannot place, modify or close trades.
// =============================================================

public sealed class TradeAIClient
{
    public const string BaseUrl =
        "http://127.0.0.1:8765";


    private const int MaximumResponseCharacters =
        22000;


    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(
            8);


    private readonly HttpClient
        _http;


    public TradeAIClient(
        HttpClient http)
    {
        _http =
            http
            ?? throw new ArgumentNullException(
                nameof(http));
    }


    public async Task<TradeAIReadResult> ReadAsync(
        string resource,
        string? symbol = null,
        int limit = 50,
        bool includeOpen = true,
        CancellationToken cancellationToken = default)
    {
        string normalizedResource =
            NormalizeResource(
                resource);


        string normalizedSymbol =
            NormalizeSymbol(
                symbol);


        limit =
            Math.Clamp(
                limit,
                1,
                500);


        string relativePath =
            BuildRelativePath(
                normalizedResource,
                normalizedSymbol,
                limit,
                includeOpen);


        Uri uri =
            new(
                BaseUrl +
                relativePath,
                UriKind.Absolute);


        using HttpRequestMessage request =
            new(
                HttpMethod.Get,
                uri);


        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));


        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);


        timeout.CancelAfter(
            RequestTimeout);


        try
        {
            using HttpResponseMessage response =
                await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);


            string body =
                await response.Content.ReadAsStringAsync(
                    timeout.Token);


            body =
                ValidateAndBoundJson(
                    body);


            return new TradeAIReadResult
            {
                Resource =
                    normalizedResource,

                RequestUri =
                    uri.AbsoluteUri,

                StatusCode =
                    (int)response.StatusCode,

                Succeeded =
                    response.IsSuccessStatusCode,

                Body =
                    body,

                Error =
                    response.IsSuccessStatusCode
                        ? string.Empty
                        : $"TradeAI returned HTTP {(int)response.StatusCode} ({response.StatusCode})."
            };
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new TradeAIReadResult
            {
                Resource =
                    normalizedResource,

                RequestUri =
                    uri.AbsoluteUri,

                StatusCode =
                    null,

                Succeeded =
                    false,

                Body =
                    string.Empty,

                Error =
                    "TradeAI did not respond before the local connector timeout."
            };
        }
        catch (HttpRequestException ex)
        {
            return new TradeAIReadResult
            {
                Resource =
                    normalizedResource,

                RequestUri =
                    uri.AbsoluteUri,

                StatusCode =
                    ex.StatusCode is HttpStatusCode status
                        ? (int)status
                        : null,

                Succeeded =
                    false,

                Body =
                    string.Empty,

                Error =
                    "TradeAI localhost API is unavailable: " +
                    ex.Message
            };
        }
    }


    private static string BuildRelativePath(
        string resource,
        string symbol,
        int limit,
        bool includeOpen)
    {
        return resource switch
        {
            "health" =>
                "/health",

            "status" =>
                "/status",

            "account" =>
                "/account",

            "positions" =>
                "/positions",

            "signals" =>
                string.IsNullOrWhiteSpace(
                    symbol)
                    ? "/signals"
                    : "/signals?symbol=" +
                      Uri.EscapeDataString(
                          symbol),

            "symbols" =>
                "/symbols",

            "trades" =>
                BuildTradesPath(
                    symbol,
                    limit,
                    includeOpen),

            "performance" =>
                "/performance",

            _ =>
                throw new InvalidOperationException(
                    $"Unsupported TradeAI resource '{resource}'.")
        };
    }


    private static string BuildTradesPath(
        string symbol,
        int limit,
        bool includeOpen)
    {
        string path =
            "/trades?limit=" +
            limit.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            +
            "&include_open=" +
            (
                includeOpen
                    ? "true"
                    : "false"
            );


        if (!string.IsNullOrWhiteSpace(
                symbol))
        {
            path +=
                "&symbol=" +
                Uri.EscapeDataString(
                    symbol);
        }


        return path;
    }


    private static string NormalizeResource(
        string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            resource);


        string value =
            resource
                .Trim()
                .ToLowerInvariant();


        return value switch
        {
            "health" or
            "status" or
            "account" or
            "positions" or
            "signals" or
            "symbols" or
            "trades" or
            "performance" =>
                value,

            _ =>
                throw new InvalidOperationException(
                    "TradeAI resource must be one of: " +
                    "health, status, account, positions, signals, " +
                    "symbols, trades, performance.")
        };
    }


    private static string NormalizeSymbol(
        string? symbol)
    {
        string value =
            symbol?
                .Trim()
                .ToUpperInvariant()
            ??
            string.Empty;


        if (value.Length ==
            0)
        {
            return string.Empty;
        }


        if (value.Length >
            32)
        {
            throw new InvalidOperationException(
                "TradeAI symbol is too long.");
        }


        foreach (char character in value)
        {
            if (
                char.IsLetterOrDigit(
                    character)
                ||
                character is
                    '.' or
                    '_' or
                    '-')
            {
                continue;
            }


            throw new InvalidOperationException(
                "TradeAI symbol contains unsupported characters.");
        }


        return value;
    }


    private static string ValidateAndBoundJson(
        string? body)
    {
        string value =
            body?.Trim()
            ??
            string.Empty;


        if (value.Length ==
            0)
        {
            return string.Empty;
        }


        try
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    value);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "TradeAI returned a non-JSON response.");
        }


        if (value.Length <=
            MaximumResponseCharacters)
        {
            return value;
        }


        return value[
            ..MaximumResponseCharacters]
            +
            "\n...[TradeAI response truncated by NIRA]";
    }
}


public sealed record TradeAIReadResult
{
    public required string Resource
    {
        get;
        init;
    }


    public required string RequestUri
    {
        get;
        init;
    }


    public int? StatusCode
    {
        get;
        init;
    }


    public bool Succeeded
    {
        get;
        init;
    }


    public string Body
    {
        get;
        init;
    } =
        string.Empty;


    public string Error
    {
        get;
        init;
    } =
        string.Empty;
}

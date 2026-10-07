/*
 * filename: TradeAIReadCapabilityHandler.cs
 */

using System.Text.Json;

using NIRAAgent.Capabilities;

namespace NIRAAgent.Integrations.Elvara.Apps.TradeAI;


// =============================================================
// TRADEAI CAPABILITY IDS
// =============================================================

public static class TradeAICapabilityIds
{
    public const string Read =
        "elvara.tradeai.read";
}


// =============================================================
// TRADEAI READ CAPABILITY
//
// One fixed read-only capability covers the public TradeAI
// localhost API resources. The model cannot provide an arbitrary
// URL or HTTP method.
//
// Allowed resources:
// health
// status
// account
// positions
// signals
// symbols
// trades
// performance
// =============================================================

public sealed class TradeAIReadCapabilityHandler
    : INIRACapabilityHandler
{
    private readonly TradeAIClient
        _client;


    public TradeAIReadCapabilityHandler(
        TradeAIClient client)
    {
        _client =
            client
            ?? throw new ArgumentNullException(
                nameof(client));
    }


    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                TradeAICapabilityIds.Read,

            Description =
                "Read authoritative current data from the local ELVARA TradeAI API. " +
                "This capability is observation-only and cannot place, modify or close trades. " +
                "resource must be one of health, status, account, positions, signals, symbols, trades, performance. " +
                "Use symbol with signals or trades when the user asks about one symbol.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter(
                        "resource",
                        "string",
                        true,
                        "TradeAI resource: health, status, account, positions, signals, symbols, trades, or performance."),

                    Parameter(
                        "symbol",
                        "string",
                        false,
                        "Optional symbol filter such as EURUSD. Used by signals and trades."),

                    Parameter(
                        "limit",
                        "integer",
                        false,
                        "Trades result limit from 1 to 500. Default 50."),

                    Parameter(
                        "includeOpen",
                        "boolean",
                        false,
                        "For trades, include currently open trade records. Default true.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Observe;
    }


    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        string resource =
            NIRACapabilityArguments.RequireString(
                request,
                "resource",
                40);


        string? symbol =
            NIRACapabilityArguments.GetOptionalString(
                request,
                "symbol",
                32);


        int limit =
            ReadOptionalInteger(
                request,
                "limit",
                defaultValue:
                    50,
                minimum:
                    1,
                maximum:
                    500);


        bool includeOpen =
            ReadOptionalBoolean(
                request,
                "includeOpen",
                defaultValue:
                    true);


        TradeAIReadResult result =
            await _client.ReadAsync(
                resource,
                symbol,
                limit,
                includeOpen,
                cancellationToken);


        TradeAIReadResult? health =
            null;


        if (!resource.Equals(
                "health",
                StringComparison.OrdinalIgnoreCase))
        {
            health =
                await _client.ReadAsync(
                    "health",
                    cancellationToken:
                        cancellationToken);
        }


        string freshness =
            BuildFreshnessContext(
                resource,
                result,
                health);


        string summary =
            result.Succeeded
                ? $"TradeAI read-only resource '{result.Resource}' returned authoritative localhost data. {freshness}"
                : $"TradeAI read-only resource '{result.Resource}' could not be read: {result.Error}";


        string output =
            result.Succeeded
                ? string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "TRADEAI AUTHORITATIVE READ-ONLY DATA",
                        freshness,
                        "",
                        "IMPORTANT INTERPRETATION RULE:",
                        "Broker/MT5 connectivity and broker account fields may still be live even when TradeAI engine telemetry is stale. " +
                        "Signals, engine decisions, forward-audit state and telemetry-derived trading state must NOT be described as current when telemetry_fresh=false.",
                        "",
                        $"RequestedResource={result.Resource}",
                        result.Body
                    })
                : string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        result.Error,
                        result.Body
                    }.Where(
                        value =>
                            !string.IsNullOrWhiteSpace(
                                value)));


        return new NIRACapabilityHandlerResult
        {
            Succeeded =
                result.Succeeded,

            HttpStatusCode =
                result.StatusCode,

            Summary =
                summary,

            Output =
                output,

            ChangedSystemState =
                false
        };
    }


    private static string BuildFreshnessContext(
        string requestedResource,
        TradeAIReadResult requested,
        TradeAIReadResult? health)
    {
        string healthJson =
            requestedResource.Equals(
                "health",
                StringComparison.OrdinalIgnoreCase)
                ? requested.Body
                : health?.Body
                    ??
                    string.Empty;


        if (string.IsNullOrWhiteSpace(
                healthJson))
        {
            return
                "TradeAI telemetry freshness could not be independently verified for this read.";
        }


        try
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    healthJson);


            JsonElement root =
                document.RootElement;


            if (
                !root.TryGetProperty(
                    "tradeai",
                    out JsonElement tradeai)
                ||
                tradeai.ValueKind !=
                    JsonValueKind.Object)
            {
                return
                    "TradeAI telemetry freshness metadata was not present in /health.";
            }


            bool fresh =
                tradeai.TryGetProperty(
                    "telemetry_fresh",
                    out JsonElement freshElement)
                &&
                freshElement.ValueKind is
                    JsonValueKind.True or
                    JsonValueKind.False
                &&
                freshElement.GetBoolean();


            string state =
                ReadJsonText(
                    tradeai,
                    "state");


            string lastTelemetry =
                ReadJsonText(
                    tradeai,
                    "last_telemetry_utc");


            string age =
                tradeai.TryGetProperty(
                    "telemetry_age_seconds",
                    out JsonElement ageElement)
                &&
                ageElement.ValueKind ==
                    JsonValueKind.Number
                    ? ageElement
                        .GetRawText()
                    : "-";


            if (fresh)
            {
                return
                    $"TradeAI engine telemetry is FRESH. " +
                    $"State={state}; LastTelemetryUtc={lastTelemetry}; AgeSeconds={age}.";
            }


            return
                $"WARNING: TradeAI engine telemetry is STALE. " +
                $"State={state}; LastTelemetryUtc={lastTelemetry}; AgeSeconds={age}. " +
                "Do not describe telemetry-derived signals/decisions as current.";
        }
        catch (JsonException)
        {
            return
                "TradeAI telemetry freshness metadata could not be parsed.";
        }
    }


    private static string ReadJsonText(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return "-";
        }


        return value.ValueKind switch
        {
            JsonValueKind.String =>
                value.GetString()
                ??
                "-",

            JsonValueKind.Null =>
                "-",

            _ =>
                value.GetRawText()
        };
    }


    private static int ReadOptionalInteger(
        NIRACapabilityRequest request,
        string name,
        int defaultValue,
        int minimum,
        int maximum)
    {
        if (!TryGetProperty(
                request,
                name,
                out JsonElement value))
        {
            return defaultValue;
        }


        int parsed;


        if (
            value.ValueKind ==
                JsonValueKind.Number
            &&
            value.TryGetInt32(
                out parsed))
        {
            return Math.Clamp(
                parsed,
                minimum,
                maximum);
        }


        if (
            value.ValueKind ==
                JsonValueKind.String
            &&
            int.TryParse(
                value.GetString(),
                out parsed))
        {
            return Math.Clamp(
                parsed,
                minimum,
                maximum);
        }


        throw new InvalidOperationException(
            $"Capability argument '{name}' must be an integer.");
    }


    private static bool ReadOptionalBoolean(
        NIRACapabilityRequest request,
        string name,
        bool defaultValue)
    {
        if (!TryGetProperty(
                request,
                name,
                out JsonElement value))
        {
            return defaultValue;
        }


        if (value.ValueKind ==
            JsonValueKind.True)
        {
            return true;
        }


        if (value.ValueKind ==
            JsonValueKind.False)
        {
            return false;
        }


        if (
            value.ValueKind ==
                JsonValueKind.String
            &&
            bool.TryParse(
                value.GetString(),
                out bool parsed))
        {
            return parsed;
        }


        throw new InvalidOperationException(
            $"Capability argument '{name}' must be a boolean.");
    }


    private static bool TryGetProperty(
        NIRACapabilityRequest request,
        string name,
        out JsonElement value)
    {
        if (request.Arguments.ValueKind !=
            JsonValueKind.Object)
        {
            value =
                default;

            return false;
        }


        foreach (
            JsonProperty property
            in request.Arguments.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                value =
                    property.Value;

                return true;
            }
        }


        value =
            default;

        return false;
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name =
                name,

            Type =
                type,

            Required =
                required,

            Description =
                description
        };
    }
}

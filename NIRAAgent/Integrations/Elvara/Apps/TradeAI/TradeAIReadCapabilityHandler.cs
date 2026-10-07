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


        string summary =
            result.Succeeded
                ? $"TradeAI read-only resource '{result.Resource}' returned authoritative localhost data."
                : $"TradeAI read-only resource '{result.Resource}' could not be read: {result.Error}";


        return new NIRACapabilityHandlerResult
        {
            Succeeded =
                result.Succeeded,

            HttpStatusCode =
                result.StatusCode,

            Summary =
                summary,

            Output =
                result.Succeeded
                    ? result.Body
                    : string.Join(
                        Environment.NewLine,
                        new[]
                        {
                            result.Error,
                            result.Body
                        }.Where(
                            value =>
                                !string.IsNullOrWhiteSpace(
                                    value))),

            ChangedSystemState =
                false
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

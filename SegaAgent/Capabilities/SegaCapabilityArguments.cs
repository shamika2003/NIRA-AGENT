/*
 * filename: SegaCapabilityArguments.cs
 */

using System.Globalization;
using System.Text.Json;

namespace SegaAgent.Capabilities;

internal static class SegaCapabilityArguments
{
    public static string RequireString(
        SegaCapabilityRequest request,
        string name,
        int maximumLength = 32000)
    {
        string? value =
            GetOptionalString(
                request,
                name,
                maximumLength);


        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                $"Capability argument '{name}' is required.");
        }


        return value;
    }


    public static string? GetOptionalString(
        SegaCapabilityRequest request,
        string name,
        int maximumLength = 32000)
    {
        if (!TryGetProperty(
                request,
                name,
                out JsonElement value))
        {
            return null;
        }


        if (value.ValueKind ==
            JsonValueKind.Null)
        {
            return null;
        }


        if (value.ValueKind !=
            JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Capability argument '{name}' must be a string.");
        }


        string clean =
            value.GetString()?.Trim()
            ?? string.Empty;


        if (clean.Length >
            maximumLength)
        {
            throw new InvalidOperationException(
                $"Capability argument '{name}' is too long.");
        }


        return clean;
    }


    public static string RequireRawString(
        SegaCapabilityRequest request,
        string name,
        int maximumLength = 32000)
    {
        string? value =
            GetOptionalRawString(
                request,
                name,
                maximumLength);


        if (value ==
            null)
        {
            throw new InvalidOperationException(
                $"Capability argument '{name}' is required.");
        }


        return value;
    }


    public static string? GetOptionalRawString(
        SegaCapabilityRequest request,
        string name,
        int maximumLength = 32000)
    {
        if (!TryGetProperty(
                request,
                name,
                out JsonElement value))
        {
            return null;
        }


        if (value.ValueKind ==
            JsonValueKind.Null)
        {
            return null;
        }


        if (value.ValueKind !=
            JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Capability argument '{name}' must be a string.");
        }


        string raw =
            value.GetString()
            ?? string.Empty;


        if (raw.Length >
            maximumLength)
        {
            throw new InvalidOperationException(
                $"Capability argument '{name}' is too long.");
        }


        return raw;
    }


    public static bool GetBoolean(
        SegaCapabilityRequest request,
        string name,
        bool defaultValue = false)
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


        if (value.ValueKind ==
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


    public static int RequireInteger(
        SegaCapabilityRequest request,
        string name,
        int minimum,
        int maximum)
    {
        if (!TryGetProperty(
                request,
                name,
                out JsonElement value))
        {
            throw new InvalidOperationException(
                $"Capability argument '{name}' is required.");
        }


        int parsed;


        if (value.ValueKind ==
            JsonValueKind.Number
            &&
            value.TryGetInt32(
                out parsed))
        {
            if (parsed < minimum || parsed > maximum)
            {
                throw new InvalidOperationException(
                    $"Capability argument '{name}' must be between {minimum} and {maximum}.");
            }


            return parsed;
        }


        if (value.ValueKind ==
            JsonValueKind.String
            &&
            int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed))
        {
            if (parsed < minimum || parsed > maximum)
            {
                throw new InvalidOperationException(
                    $"Capability argument '{name}' must be between {minimum} and {maximum}.");
            }


            return parsed;
        }


        throw new InvalidOperationException(
            $"Capability argument '{name}' must be an integer.");
    }


    public static int GetInteger(
        SegaCapabilityRequest request,
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


        if (value.ValueKind ==
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


        if (value.ValueKind ==
            JsonValueKind.String
            &&
            int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
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


    public static JsonElement? GetOptionalObject(
        SegaCapabilityRequest request,
        string name)
    {
        if (!TryGetProperty(
                request,
                name,
                out JsonElement value))
        {
            return null;
        }


        if (value.ValueKind ==
            JsonValueKind.Null)
        {
            return null;
        }


        if (value.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Capability argument '{name}' must be an object.");
        }


        return value.Clone();
    }


    public static string NormalizePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            throw new InvalidOperationException(
                "A filesystem path is required.");
        }


        string fullPath =
            Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(
                    path.Trim()));


        if (fullPath.Length >
            32760)
        {
            throw new InvalidOperationException(
                "Filesystem path is too long.");
        }


        return fullPath;
    }


    public static string Truncate(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return string.Empty;
        }


        if (value.Length <=
            maximumLength)
        {
            return value;
        }


        return value[..maximumLength]
            + "\n...[truncated]";
    }


    private static bool TryGetProperty(
        SegaCapabilityRequest request,
        string name,
        out JsonElement value)
    {
        JsonElement arguments =
            request.Arguments;


        if (arguments.ValueKind !=
            JsonValueKind.Object)
        {
            value =
                default;


            return false;
        }


        foreach (
            JsonProperty property
            in arguments.EnumerateObject())
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
}

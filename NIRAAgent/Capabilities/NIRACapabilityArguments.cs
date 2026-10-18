/*
 * filename: NIRACapabilityArguments.cs
 */

using System.Globalization;
using System.Text.Json;

namespace NIRAAgent.Capabilities;

internal static class NIRACapabilityArguments
{
    public static string RequireString(
        NIRACapabilityRequest request,
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
        NIRACapabilityRequest request,
        string name,
        int maximumLength = 32000)
    {
        if (!TryGetProperty(
                request,
                name,
                out JsonElement value) &&
            !(name.Equals("ref", StringComparison.OrdinalIgnoreCase) &&
              request.CapabilityId.StartsWith("browser.", StringComparison.OrdinalIgnoreCase) &&
              TryGetProperty(request, "elementRef", out value)))
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
        NIRACapabilityRequest request,
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
        NIRACapabilityRequest request,
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
        NIRACapabilityRequest request,
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
        NIRACapabilityRequest request,
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
        NIRACapabilityRequest request,
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


    // Resolve explicit current-user known-folder prefixes against the OS location,
    // including OneDrive/enterprise redirection. Never interpret Documents\... as
    // a child of NIRA's application bin directory. Ordinary relative paths and
    // already absolute paths keep their existing semantics.
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("A filesystem path is required.");

        string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (!Path.IsPathFullyQualified(expanded))
        {
            string slashNormalized = expanded.Replace('\\', '/');
            string first = slashNormalized.Split('/', 2)[0];
            string? resolved = null;
            if (first == "~")
                resolved = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            else if (IsCurrentUserKnownFolderName(first))
                resolved = NIRAFileLocationCapabilityHandler.ResolveConfiguredCurrentUserFolder(first);

            if (!string.IsNullOrWhiteSpace(resolved))
            {
                string remainder = slashNormalized.Length == first.Length
                    ? string.Empty : slashNormalized[(first.Length + 1)..];
                expanded = string.IsNullOrEmpty(remainder)
                    ? resolved : Path.Combine(resolved, remainder.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        string fullPath = Path.GetFullPath(expanded);
        if (fullPath.Length > 32760)
            throw new InvalidOperationException("Filesystem path is too long.");
        return fullPath;
    }

    internal static bool IsCurrentUserKnownFolderName(string name) =>
        name.Equals("Desktop", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Documents", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Downloads", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Pictures", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Music", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Videos", StringComparison.OrdinalIgnoreCase);

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
        NIRACapabilityRequest request,
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
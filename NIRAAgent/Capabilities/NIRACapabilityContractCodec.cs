using System.Globalization;
using System.Text.Json;
using NIRAAgent.Diagnostics;

namespace NIRAAgent.Capabilities;

// R75: a shared, descriptor-owned argument contract for model guidance,
// deterministic normalization, and pre-dispatch validation. No authorization,
// I/O, model calls, tool IDs, source claims, or guessed missing values.
internal static class NIRACapabilityContractCodec
{
    internal static void ValidateDescriptor(NIRACapabilityDescriptor descriptor)
    {
        foreach (NIRACapabilityParameterDescriptor parameter in descriptor.Parameters)
        {
            if (parameter.AllowedValues.Count > 0)
            {
                if (!parameter.Type.Equals("string", StringComparison.OrdinalIgnoreCase) ||
                    parameter.AllowedValues.Any(string.IsNullOrWhiteSpace) ||
                    parameter.AllowedValues.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                        parameter.AllowedValues.Count)
                    throw new InvalidOperationException(
                        $"Invalid allowed values declared for '{descriptor.Id}.{parameter.Name}'.");
                if (parameter.DefaultValue.Length > 0 &&
                    !parameter.AllowedValues.Contains(parameter.DefaultValue, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Invalid default declared for '{descriptor.Id}.{parameter.Name}'.");
                if (parameter.ValueAliases.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                        parameter.ValueAliases.Count ||
                    parameter.ValueAliases.Any(pair =>
                        string.IsNullOrWhiteSpace(pair.Key) ||
                        !parameter.AllowedValues.Contains(pair.Value, StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(
                        $"Invalid aliases declared for '{descriptor.Id}.{parameter.Name}'.");
            }
            else if (parameter.ValueAliases.Count > 0)
                throw new InvalidOperationException(
                    $"Aliases require allowed values for '{descriptor.Id}.{parameter.Name}'.");
            if (parameter.Minimum.HasValue || parameter.Maximum.HasValue)
            {
                if (!parameter.Type.Equals("integer", StringComparison.OrdinalIgnoreCase) ||
                    (parameter.Minimum.HasValue && parameter.Maximum.HasValue &&
                     parameter.Minimum.Value > parameter.Maximum.Value) ||
                    (parameter.DefaultValue.Length > 0 &&
                     (!int.TryParse(parameter.DefaultValue, NumberStyles.Integer,
                         CultureInfo.InvariantCulture, out int parsed) ||
                      (parameter.Minimum.HasValue && parsed < parameter.Minimum.Value) ||
                      (parameter.Maximum.HasValue && parsed > parameter.Maximum.Value))))
                    throw new InvalidOperationException(
                        $"Invalid numeric range declared for '{descriptor.Id}.{parameter.Name}'.");
            }
        }
    }

    internal static string Describe(NIRACapabilityParameterDescriptor parameter)
    {
        string tail = parameter.Description;
        if (parameter.AllowedValues.Count > 0)
            tail = "allowed=" + string.Join(",", parameter.AllowedValues) + " | " + tail;
        if (parameter.Minimum.HasValue || parameter.Maximum.HasValue)
            tail = $"range={parameter.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "*"}..{parameter.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "*"} | " + tail;
        if (!string.IsNullOrEmpty(parameter.DefaultValue))
            tail = "default=" + parameter.DefaultValue + " | " + tail;
        return $"  - {parameter.Name}: {parameter.Type} | required={parameter.Required} | {tail}";
    }

    internal static NIRACapabilityRequest NormalizeAndValidate(
        NIRACapabilityRequest request, NIRACapabilityDescriptor descriptor)
    {
        if (request.Arguments.ValueKind != JsonValueKind.Object) return request;
        var defined = descriptor.Parameters.ToDictionary(p => p.Name,
            StringComparer.OrdinalIgnoreCase);
        var items = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (JsonProperty property in request.Arguments.EnumerateObject())
        {
            JsonElement value = property.Value.Clone();
            if (defined.TryGetValue(property.Name, out var parameter))
            {
                if (parameter.AllowedValues.Count > 0)
                {
                    if (!string.Equals(parameter.Type, "string", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Invalid descriptor enum type for '{parameter.Name}'.");
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        string supplied = value.GetString() ?? string.Empty;
                        string canonical = CanonicalValue(parameter, supplied);
                        if (!string.Equals(supplied, canonical, StringComparison.Ordinal))
                        {
                            value = JsonSerializer.SerializeToElement(canonical);
                            changed = true;
                            NIRADiagnosticTrace.Event("TOOL-CONTRACT-CANONICALIZED",
                                $"Capability={descriptor.Id} Field={parameter.Name} " +
                                "ChangedValue=True AuthorizationUnchanged=True");
                        }
                    }
                }
                if (parameter.Minimum.HasValue || parameter.Maximum.HasValue)
                {
                    if (!string.Equals(parameter.Type, "integer", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Invalid descriptor range type for '{parameter.Name}'.");
                    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) &&
                        ((parameter.Minimum.HasValue && number < parameter.Minimum.Value) ||
                         (parameter.Maximum.HasValue && number > parameter.Maximum.Value)))
                        throw new InvalidOperationException(
                            $"Argument '{parameter.Name}' must be between " +
                            $"{parameter.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "-infinity"} and " +
                            $"{parameter.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "infinity"}.");
                }
            }
            if (!items.TryAdd(property.Name, value))
                throw new InvalidOperationException($"Duplicate capability argument '{property.Name}'.");
        }
        return changed ? request with { Arguments = JsonSerializer.SerializeToElement(items) } : request;
    }

    internal static string CanonicalValue(NIRACapabilityParameterDescriptor parameter, string proposed)
    {
        string trimmed = proposed.Trim();
        string? match = parameter.AllowedValues.FirstOrDefault(value =>
            value.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;
        string? alias = parameter.ValueAliases.FirstOrDefault(pair =>
            pair.Key.Equals(trimmed, StringComparison.OrdinalIgnoreCase)).Value;
        if (alias is not null)
        {
            match = parameter.AllowedValues.FirstOrDefault(value =>
                value.Equals(alias, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
            throw new InvalidOperationException($"Unrecognized descriptor alias for '{parameter.Name}'.");
        }
        throw new InvalidOperationException(
            $"{parameter.Name} must be {string.Join(" or ", parameter.AllowedValues)}.");
    }

    internal static int ReadInteger(NIRACapabilityRequest request,
        NIRACapabilityDescriptor descriptor, string parameterName)
    {
        var parameter = descriptor.Parameters.First(p =>
            p.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase));
        if (!parameter.Type.Equals("integer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Argument '{parameter.Name}' is not an integer descriptor.");
        int fallback = 0;
        if (parameter.DefaultValue.Length != 0 &&
            !int.TryParse(parameter.DefaultValue, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out fallback))
            throw new InvalidOperationException($"Invalid default for '{parameter.Name}'.");
        return NIRACapabilityArguments.GetInteger(request, parameter.Name, fallback,
            parameter.Minimum ?? int.MinValue, parameter.Maximum ?? int.MaxValue);
    }

    internal static string ReadEnum(NIRACapabilityRequest request, NIRACapabilityDescriptor descriptor,
        string parameterName)
    {
        var parameter = descriptor.Parameters.First(p =>
            p.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase));
        string candidate = parameter.DefaultValue;
        if (request.Arguments.ValueKind == JsonValueKind.Object)
        {
            var matching = request.Arguments.EnumerateObject().Where(property =>
                property.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length > 1)
                throw new InvalidOperationException($"Duplicate argument '{parameter.Name}'.");
            if (matching.Length == 1)
            {
                if (matching[0].Value.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException($"Argument '{parameter.Name}' must be string.");
                candidate = matching[0].Value.GetString() ?? string.Empty;
            }
        }
        return CanonicalValue(parameter, candidate);
    }
}

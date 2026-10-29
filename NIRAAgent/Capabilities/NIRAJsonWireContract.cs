using System.Text.Json;

namespace NIRAAgent.Capabilities;

// R58: One canonical JSON contract at the runtime boundary, not an LLM promise.
// This is structural validation only. It NEVER invents missing values, rewrites
// the meaning of a request, authorizes an operation, or executes a capability.
internal static class NIRAJsonWireContract
{
    internal static void EnsureUniqueProperties(JsonElement root)
    {
        var pending = new Stack<(JsonElement Element, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            (JsonElement element, int depth) = pending.Pop();
            if (depth > 64)
                throw new InvalidOperationException("JSON nesting exceeds the maximum contract depth.");
            if (element.ValueKind == JsonValueKind.Object)
            {
                HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new InvalidOperationException(
                            $"Duplicate JSON property '{property.Name}'; refusing ambiguous values.");
                    pending.Push((property.Value, depth + 1));
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    pending.Push((item, depth + 1));
            }
        }
    }

    internal static JsonElement ParseLegacyTypedJson(
        string? source, JsonValueKind expected, string parameter)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > 180000)
            throw new InvalidOperationException(
                $"Argument '{parameter}' must contain valid, bounded JSON.");
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(source,
                new JsonDocumentOptions { MaxDepth = 64 });
            EnsureUniqueProperties(parsed.RootElement);
            if (parsed.RootElement.ValueKind != expected)
                throw new InvalidOperationException(
                    $"Argument '{parameter}' requires a JSON {expected.ToString().ToLowerInvariant()}.");
            return parsed.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Malformed nested JSON in '{parameter}'. Supply a native JSON " +
                "array/object rather than a double-encoded string; no action was executed. " +
                $"Detail: {ex.GetType().Name}", ex);
        }
    }

    internal static void ValidateJsonArguments(
        NIRACapabilityRequest request, NIRACapabilityDescriptor descriptor)
    {
        if (request.Arguments.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Capability arguments must be one JSON object.");
        EnsureUniqueProperties(request.Arguments);
        foreach (NIRACapabilityParameterDescriptor parameter in descriptor.Parameters)
        {
            if (!parameter.Name.EndsWith("Json", StringComparison.OrdinalIgnoreCase) ||
                !request.Arguments.TryGetProperty(parameter.Name, out JsonElement value) ||
                value.ValueKind == JsonValueKind.Null)
                continue;
            if (value.ValueKind == JsonValueKind.String)
            {
                string? source = value.GetString();
                if (string.IsNullOrWhiteSpace(source) || source.Length > 180000)
                    throw new InvalidOperationException(
                        $"Argument '{parameter.Name}' must contain bounded JSON.");
                try
                {
                    using JsonDocument parsed = JsonDocument.Parse(source,
                        new JsonDocumentOptions { MaxDepth = 64 });
                    EnsureUniqueProperties(parsed.RootElement);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"Malformed nested JSON in '{parameter.Name}'. " +
                        "Pass the JSON value directly instead of double-encoding it. " +
                        $"No capability executed ({ex.GetType().Name}).", ex);
                }
            }
            else if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                EnsureUniqueProperties(value);
        }
    }
}

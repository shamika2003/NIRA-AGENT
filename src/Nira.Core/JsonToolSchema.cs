using System.Text.Json;

namespace Nira.Core;

/// <summary>
/// A deliberately narrow JSON Schema validator: type, required, properties,
/// additionalProperties, items, enum, and string lengths. Rejects unknown shapes;
/// this is a preflight guard, not a general-purpose JSON Schema implementation.
/// </summary>
public static class JsonToolSchema
{
    public static bool Validate(JsonElement schema, JsonElement value, out string error)
    {
        error = "";
        return Check(schema, value, "$", out error);
    }

    private static bool Check(JsonElement schema, JsonElement value, string path, out string error)
    {
        error = "";
        if (schema.ValueKind != JsonValueKind.Object)
        {
            error = $"{path}: schema must be an object";
            return false;
        }
        string? type = schema.TryGetProperty("type", out var kind) ? kind.GetString() : null;
        bool validType = type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "null" => value.ValueKind == JsonValueKind.Null,
            null => true,
            _ => false
        };
        if (!validType)
        {
            error = $"{path}: expected {type}, got {value.ValueKind}";
            return false;
        }
        if (schema.TryGetProperty("enum", out var options) && options.ValueKind == JsonValueKind.Array &&
            !options.EnumerateArray().Any(o => o.ToString() == value.ToString()))
        {
            error = $"{path}: value not in enum";
            return false;
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            int length = value.GetString()!.Length;
            if (schema.TryGetProperty("minLength", out var min) && length < min.GetInt32())
            { error = $"{path}: string is too short"; return false; }
            if (schema.TryGetProperty("maxLength", out var max) && length > max.GetInt32())
            { error = $"{path}: string is too long"; return false; }
        }
        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            int n = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (!Check(items, item, path + "[" + n++ + "]", out error)) return false;
            }
        }
        if (value.ValueKind != JsonValueKind.Object) return true;
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var property in required.EnumerateArray())
            {
                string name = property.GetString()!;
                if (!value.TryGetProperty(name, out _))
                { error = $"{path}: missing required property '{name}'"; return false; }
            }
        }
        if (!schema.TryGetProperty("properties", out var props)) return true;
        foreach (var property in value.EnumerateObject())
        {
            if (!props.TryGetProperty(property.Name, out var child))
            {
                if (schema.TryGetProperty("additionalProperties", out var allowed) &&
                    allowed.ValueKind == JsonValueKind.False)
                { error = $"{path}: unsupported property '{property.Name}'"; return false; }
                continue;
            }
            if (!Check(child, property.Value, path + "." + property.Name, out error)) return false;
        }
        return true;
    }

    public static JsonElement Schema(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        return parsed.RootElement.Clone();
    }
}

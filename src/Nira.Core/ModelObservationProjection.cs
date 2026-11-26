using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nira.Core;

/// <summary>Lossless, model-facing projection of known accessibility inventories.
/// Durable messages and operation receipts are never modified or replaced.</summary>
internal static class ModelObservationProjection
{
    private static readonly string[] Columns = ["ControlId", "Name", "Kind", "AutomationId",
        "Enabled", "Offscreen", "CanInvoke", "CanSetText", "CanTypeText"];

    internal static IReadOnlyList<ChatEntry> Compact(IReadOnlyList<ChatEntry> prompt,
        out int savedCharacters)
    {
        savedCharacters = 0;
        if (prompt.Count == 0 || prompt[0].Role != "system") return prompt;
        var callNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in prompt.Where(m => m.Role == "assistant"))
            foreach (var call in entry.ToolCalls ?? [])
                if (!callNames.TryAdd(call.Id, call.Name)) ambiguous.Add(call.Id);
        var projected = prompt.ToArray();
        for (int i = 0; i < projected.Length; i++)
        {
            var entry = projected[i];
            if (entry.Role != "tool" || entry.ToolCallId is not { } id || ambiguous.Contains(id) ||
                !callNames.TryGetValue(id, out var name) || name != "desktop.window_inspect" ||
                entry.Content is not { Length: > 512 and <= 500000 } content) continue;
            try
            {
                if (JsonNode.Parse(content) is not JsonObject root ||
                    root["Success"]?.GetValue<bool>() != true || root["Data"] is not JsonObject data ||
                    data["Controls"] is not JsonArray controls || controls.Count == 0 ||
                    data.ContainsKey("ControlColumns") || data.ContainsKey("ControlRows")) continue;
                // An unknown or extended schema stays untouched: never silently discard new fields.
                if (controls.Any(c => c is not JsonObject row || row.Count != Columns.Length ||
                    Columns.Any(column => !row.ContainsKey(column)))) continue;
                var rows = new JsonArray();
                foreach (var control in controls)
                {
                    var row = new JsonArray();
                    foreach (string column in Columns) row.Add(control![column]?.DeepClone());
                    rows.Add(row);
                }
                data.Remove("Controls");
                data["ControlColumns"] = JsonSerializer.SerializeToNode(Columns);
                data["ControlRows"] = rows;
                string compact = root.ToJsonString();
                if (compact.Length >= content.Length) continue;
                projected[i] = entry with { Content = compact };
                savedCharacters += content.Length - compact.Length;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
            { /* Unrecognized observation remains intact. */ }
        }
        const string explanation = "\n\nCOMPACT OBSERVATIONS: In desktop inspection results, ControlRows are arrays in the exact order of ControlColumns. Each row contains all original fields. Use exact unique observed names for sequence steps or canonical ControlId when ambiguous; individual actions may use name://Name or automation://AutomationId. AutomationId is a different field used for observeAutomationId. Use the latest observation of the target. The host retains full original receipts.";
        if (savedCharacters <= explanation.Length) { savedCharacters = 0; return prompt; }
        projected[0] = projected[0] with { Content = projected[0].Content + explanation };
        savedCharacters -= explanation.Length;
        return projected;
    }
}

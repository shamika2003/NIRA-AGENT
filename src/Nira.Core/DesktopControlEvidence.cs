using System.Text.Json;

namespace Nira.Core;

/// <summary>Bind an ordered desktop action proposal to the latest actual accessibility observation.</summary>
internal static class DesktopControlEvidence
{
    internal static bool Allows(ConversationState state, JsonElement args)
    {
        string? window = args.GetProperty("windowId").GetString();
        var steps = args.GetProperty("steps");
        string? automationId = args.GetProperty("observeAutomationId").GetString();
        string? expected = args.GetProperty("expectedName").GetString();
        if (steps.GetArrayLength() is < 2 or > 12 || string.IsNullOrWhiteSpace(automationId) ||
            string.IsNullOrWhiteSpace(expected) || expected.Any(char.IsControl)) return false;
        foreach (var op in state.Operations.AsEnumerable().Reverse())
        {
            if (op.ToolName != "desktop.window_inspect" || op.Status != OperationStatus.Succeeded || op.ResultJson is null) continue;
            try
            {
                using var doc = JsonDocument.Parse(op.ResultJson);
                var data = doc.RootElement.GetProperty("Data");
                if (data.GetProperty("Window").GetProperty("WindowId").GetString() != window) continue;
                var controls = data.GetProperty("Controls").EnumerateArray().ToArray();
                bool Visible(JsonElement c) => c.GetProperty("Offscreen").ValueKind == JsonValueKind.False;
                if (controls.Count(c => Visible(c) && c.GetProperty("AutomationId").GetString() == automationId) != 1) return false;
                foreach (var step in steps.EnumerateArray())
                {
                    string? id = step.GetProperty("controlId").GetString(), name = step.GetProperty("name").GetString();
                    if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) || controls.Count(c =>
                        c.GetProperty("ControlId").GetString() == id && c.GetProperty("Name").GetString() == name &&
                        Visible(c) && c.GetProperty("Enabled").ValueKind == JsonValueKind.True &&
                        c.GetProperty("CanInvoke").ValueKind == JsonValueKind.True) != 1) return false;
                }
                return true;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
        }
        return false;
    }
}

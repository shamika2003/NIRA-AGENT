using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Nira.Core;

/// <summary>Bind an ordered desktop action proposal to the latest actual accessibility observation.</summary>
internal static class DesktopControlEvidence
{
    // Use observed accessibility contracts to surface supported operations, not
    // application names or a classifier over the user's words. No authority is granted.
    internal static IEnumerable<string> ObservedActionCapabilities(ConversationState state)
    {
        var last = state.Operations.LastOrDefault(op => op.Status == OperationStatus.Succeeded);
        if (last is null || last.ToolName is not ("desktop.window_inspect" or "desktop.control_invoke" or
            "desktop.control_sequence" or "desktop.control_set_text" or "desktop.control_type_text")) return [];
        var inspection = state.Operations.LastOrDefault(op => op.ToolName == "desktop.window_inspect" &&
            op.Status == OperationStatus.Succeeded && op.ResultJson is not null);
        if (inspection is null) return [];
        try
        {
            using var doc = JsonDocument.Parse(inspection.ResultJson!);
            var controls = doc.RootElement.GetProperty("Data").GetProperty("Controls").EnumerateArray()
                .Where(c => c.GetProperty("Enabled").ValueKind == JsonValueKind.True &&
                    c.GetProperty("Offscreen").ValueKind == JsonValueKind.False).ToArray();
            var names = new List<string>();
            bool Supports(string flag) => controls.Any(c => c.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True);
            if (Supports("CanTypeText")) names.Add("desktop.control_type_text");
            if (Supports("CanSetText")) names.Add("desktop.control_set_text");
            if (controls.Count(c => c.TryGetProperty("CanInvoke", out var invoke) && invoke.ValueKind == JsonValueKind.True) >= 2)
                names.Add("desktop.control_sequence");
            if (Supports("CanInvoke")) names.Add("desktop.control_invoke");
            names.Add("desktop.window_capture");
            names.Add("desktop.window_inspect");
            return names;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { return []; }
    }

    internal static string? UnsupportedObservedAction(ConversationState state, string name, JsonElement args)
    {
        string? flag = name switch
        {
            "desktop.control_invoke" => "CanInvoke",
            "desktop.control_set_text" => "CanSetText",
            "desktop.control_type_text" => "CanTypeText",
            _ => null
        };
        if (flag is null) return null;
        string? window = args.GetProperty("windowId").GetString();
        string? id = args.GetProperty("controlId").GetString();
        foreach (var op in state.Operations.AsEnumerable().Reverse())
        {
            if (op.ToolName != "desktop.window_inspect" || op.Status != OperationStatus.Succeeded || op.ResultJson is null) continue;
            try
            {
                using var doc = JsonDocument.Parse(op.ResultJson);
                var data = doc.RootElement.GetProperty("Data");
                if (data.GetProperty("Window").GetProperty("WindowId").GetString() != window) continue;
                var controls = data.GetProperty("Controls").EnumerateArray().Where(c =>
                    c.GetProperty("ControlId").GetString() == id).ToArray();
                if (controls.Length != 1) return "Control is not uniquely present in the latest inspection of the target window. Reinspect if the layout changed; nothing dispatched.";
                var control = controls[0];
                if (control.GetProperty("Offscreen").ValueKind == JsonValueKind.True || control.GetProperty("Enabled").ValueKind != JsonValueKind.True)
                    return "The observed control is hidden or disabled. Nothing dispatched.";
                if (control.TryGetProperty(flag, out var supported) && supported.ValueKind == JsonValueKind.False)
                    return "The observed control has " + flag + "=false. Select a supported capability from its observed flags; nothing dispatched.";
                return null;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
            { return "The target observation is malformed. Reinspect before acting; nothing dispatched."; }
        }
        // The existing backend independently enforces live observation and identity.
        // A missing journal observation does not establish that the backend supports a control.
        return null;
    }

    // Resolve an explicit exact selector against the latest current-task inventory.
    // No fuzzy matching, inferred labels, guessed IDs, or substitution of a supplied ID.
    // Canonical arguments are what the user approves, the journal stores, and the adapter executes.
    internal static string? BindSelectors(ConversationState state, string tool, JsonElement args,
        out JsonElement bound)
    {
        bound = args;
        string? flag = tool switch
        {
            "desktop.control_sequence" or "desktop.control_invoke" => "CanInvoke",
            "desktop.control_set_text" => "CanSetText",
            "desktop.control_type_text" => "CanTypeText",
            _ => null
        };
        if (flag is null) return null;
        bool sequence = tool == "desktop.control_sequence";
        static bool IsSelector(string? value) => value is not null &&
            (value.StartsWith("name://", StringComparison.Ordinal) || value.StartsWith("automation://", StringComparison.Ordinal));
        bool needsBinding = sequence
            ? args.GetProperty("steps").EnumerateArray().Any(step =>
                step.ValueKind == JsonValueKind.String || !step.TryGetProperty("controlId", out var id) || IsSelector(id.GetString()))
            : IsSelector(args.GetProperty("controlId").GetString());
        if (!needsBinding) return null; // Existing exact-ID contracts remain unchanged.
        string? window = args.GetProperty("windowId").GetString();
        foreach (var op in state.Operations.AsEnumerable().Reverse())
        {
            if (op.ToolName != "desktop.window_inspect" || op.Status != OperationStatus.Succeeded || op.ResultJson is null) continue;
            try
            {
                using var doc = JsonDocument.Parse(op.ResultJson);
                var data = doc.RootElement.GetProperty("Data");
                if (data.GetProperty("Window").GetProperty("WindowId").GetString() != window) continue;
                var controls = data.GetProperty("Controls").EnumerateArray().ToArray();
                var eligible = controls.Where(c => c.GetProperty("Enabled").ValueKind == JsonValueKind.True &&
                    c.GetProperty("Offscreen").ValueKind == JsonValueKind.False &&
                    c.GetProperty(flag).ValueKind == JsonValueKind.True).ToArray();
                var canonical = JsonNode.Parse(args.GetRawText())!.AsObject();
                string? Bind(JsonObject step, string? requiredName)
                {
                    string? id = step["controlId"]?.GetValue<string>();
                    if (id is not null && !IsSelector(id)) return null;
                    string field = id?.StartsWith("automation://", StringComparison.Ordinal) == true ? "AutomationId" : "Name";
                    string? selector = id is null ? requiredName : field == "AutomationId" ? id[13..] : id[7..];
                    if (string.IsNullOrWhiteSpace(selector) || selector.Any(char.IsControl))
                        return "Provide a nonempty exact observed control selector.";
                    var matches = eligible.Where(c => c.GetProperty(field).GetString() == selector).ToArray();
                    if (matches.Length != 1)
                        return "The selector " + JsonSerializer.Serialize(selector) + " does not identify exactly one supported, enabled, visible control in the latest inspection. Use its exact ControlId or a unique observed selector; nothing dispatched.";
                    var match = matches[0];
                    if (requiredName is not null && match.GetProperty("Name").GetString() != requiredName)
                        return "The sequence selector and supplied exact name disagree; nothing dispatched.";
                    string? exact = match.GetProperty("ControlId").GetString();
                    if (string.IsNullOrWhiteSpace(exact) || controls.Count(c => c.GetProperty("ControlId").GetString() == exact) != 1)
                        return "The selected live control identity is ambiguous; nothing dispatched.";
                    step["controlId"] = exact;
                    return null;
                }
                if (sequence)
                {
                    var steps = canonical["steps"]!.AsArray();
                    for (int i = 0; i < steps.Count; i++)
                    {
                        if (steps[i] is JsonValue scalar && scalar.TryGetValue<string>(out string? label))
                            steps[i] = new JsonObject { ["name"] = label };
                        var step = steps[i]!.AsObject();
                        if (Bind(step, step["name"]!.GetValue<string>()) is { } error)
                            return $"steps[{i}]: {error}";
                    }
                }
                else if (Bind(canonical, null) is { } error) return error;
                bound = JsonSerializer.SerializeToElement(canonical);
                return null;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
            { return "The latest target observation cannot resolve this selector. Reinspect the exact window; nothing dispatched."; }
        }
        return "Inspect the exact target window in this task before selecting controls; nothing dispatched.";
    }

    internal static string? MissingEvidence(ConversationState state, JsonElement args, out JsonElement canonical)
    {
        canonical = args;
        string? window = args.GetProperty("windowId").GetString();
        var steps = args.GetProperty("steps");
        string? automationId = args.GetProperty("observeAutomationId").GetString();
        if (steps.GetArrayLength() is < 2 or > 12 || string.IsNullOrWhiteSpace(automationId))
            return "Provide 2–12 ordered observed controls and a unique display; nothing dispatched.";
        if (!DesktopDisplayExpectation.TryRead(args, out string expected, out string mode, out string expectationError))
            return expectationError + " Nothing dispatched.";
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
                var problems = new List<string>();
                var display = controls.Where(c => Visible(c) && c.GetProperty("AutomationId").GetString() == automationId).ToArray();
                if (display.Length == 1)
                {
                    string initialName = display[0].GetProperty("Name").GetString() ?? "";
                    if (DesktopDisplayExpectation.BindImplicitName(args, initialName, out canonical) is { } bindError)
                        problems.Add(bindError);
                    else if (!DesktopDisplayExpectation.TryRead(canonical, out expected, out mode, out expectationError))
                        problems.Add(expectationError);
                    else if (DesktopDisplayExpectation.ValidateInitial(canonical, initialName, expected, mode) is { } initialError)
                        problems.Add(initialError);
                }
                if (display.Length != 1)
                {
                    var byControlId = controls.Where(c => Visible(c) && c.GetProperty("ControlId").GetString() == automationId).ToArray();
                    if (byControlId.Length == 1 && byControlId[0].GetProperty("AutomationId").GetString() is { Length: > 0 } exact &&
                        controls.Count(c => Visible(c) && c.GetProperty("AutomationId").GetString() == exact) == 1)
                        problems.Add("observeAutomationId is not ControlId. The matching observed AutomationId is " + JsonSerializer.Serialize(exact) + ".");
                    else problems.Add("observeAutomationId must equal one unique visible AutomationId from the latest inspection, not ControlId or Name.");
                }
                int index = 0;
                foreach (var step in steps.EnumerateArray())
                {
                    string? id = step.GetProperty("controlId").GetString(), name = step.GetProperty("name").GetString();
                    var matching = controls.Where(c => c.GetProperty("ControlId").GetString() == id &&
                        Visible(c) && c.GetProperty("Enabled").ValueKind == JsonValueKind.True &&
                        c.GetProperty("CanInvoke").ValueKind == JsonValueKind.True).ToArray();
                    if (matching.Length != 1)
                        problems.Add($"steps[{index}].controlId is not one unique enabled visible invokable control in the latest inspection. Use its exact observed ControlId.");
                    else
                    {
                        string? observedName = matching[0].GetProperty("Name").GetString();
                        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) || observedName != name)
                            problems.Add($"steps[{index}].name does not match its observed ControlId. Exact observed Name: " + JsonSerializer.Serialize(observedName) + ".");
                    }
                    index++;
                }
                return problems.Count == 0 ? null : string.Join(" ", problems) + " Correct all listed fields using the latest inspection; nothing dispatched.";
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { return "The latest target observation is malformed. Reinspect the target window; nothing dispatched."; }
        }
        return "Inspect the exact target window in this task before proposing a sequence; nothing dispatched.";
    }
}

/// <summary>Shared preflight and live read-back contract for ordered controls. Explicit text is exact;
/// legacy implicit names may ignore grouping only when bound to the same observed numeric label.</summary>
public static class DesktopDisplayExpectation
{
    public static bool TryRead(JsonElement args, out string expected, out string mode, out string error)
    {
        expected = ""; mode = "exact"; error = "";
        bool hasNumber = args.TryGetProperty("expectedNumber", out var number);
        bool hasName = args.TryGetProperty("expectedName", out var name);
        bool hasText = args.TryGetProperty("expectedText", out var text);
        bool hasMode = args.TryGetProperty("comparisonMode", out var comparison);
        if ((hasNumber ? 1 : 0) + (hasName ? 1 : 0) + (hasText ? 1 : 0) != 1 || (hasMode && !hasName))
        { error = "Supply exactly one expectation: expectedNumber for a numeric value or expectedText for exact literal text (no comparisonMode). expectedName is the legacy field."; return false; }
        if (hasNumber)
        {
            expected = number.ValueKind == JsonValueKind.Number ? number.GetRawText() :
                number.ValueKind == JsonValueKind.String ? number.GetString() ?? "" : "";
            mode = "numeric_value";
            if (!IsBareNumber(expected))
            { error = "expectedNumber must be a plain signed or unsigned decimal value, without labels or exponents."; return false; }
        }
        else
        {
            var label = hasText ? text : name;
            expected = label.ValueKind == JsonValueKind.String ? label.GetString() ?? "" : "";
            mode = hasMode && comparison.ValueKind == JsonValueKind.String ? comparison.GetString() ?? "" : "exact";
        }
        if (string.IsNullOrWhiteSpace(expected) || expected.Length > 150 || expected.Any(char.IsControl) ||
            mode is not ("exact" or "numeric_grouping" or "numeric_value"))
        { error = "Provide a valid nonempty display expectation."; return false; }
        return true;
    }

    private static bool IsBareNumber(string value)
    {
        if (value.Length is < 1 or > 150) return false;
        try
        {
            var matches = NumericToken.Matches(value);
            return matches.Count == 1 && matches[0].Index == 0 && matches[0].Length == value.Length;
        }
        catch (RegexMatchTimeoutException) { return false; }
    }

    // Compatibility normalization occurs BEFORE approval, on the exact observed display.
    // It never changes the requested digits, signs, decimal places, controls, or target.
    // The canonical full label and comparison mode are recorded and approved, not inferred after dispatch.
    public static string? BindImplicitName(JsonElement args, string initialName, out JsonElement canonical)
    {
        canonical = args;
        if (!TryRead(args, out string expected, out string mode, out string error)) return error;
        if (mode != "exact" || !args.TryGetProperty("expectedName", out _) ||
            args.TryGetProperty("comparisonMode", out _) || initialName.Length > 512) return null;
        try
        {
            var initial = NumericToken.Matches(initialName);
            var final = NumericToken.Matches(expected);
            if (initial.Count != 1 || final.Count != 1) return null;
            string label;
            if (SameContext(initialName, initial[0], expected, final[0])) label = expected;
            else if (final[0].Index == 0 && final[0].Length == expected.Length)
                label = initialName[..initial[0].Index] + expected + initialName[(initial[0].Index + initial[0].Length)..];
            else return null; // Different or ambiguous labels stay literal; never strip arbitrary text.
            if (label.Length > 150 || label.Any(char.IsControl))
                return "The bound numeric display label is invalid; supply expectedText or expectedNumber explicitly.";
            var normalized = JsonNode.Parse(args.GetRawText())!.AsObject();
            normalized["expectedName"] = label;
            normalized["comparisonMode"] = "numeric_grouping";
            canonical = JsonSerializer.SerializeToElement(normalized);
            return null;
        }
        catch (RegexMatchTimeoutException) { return "The display expectation could not be checked safely."; }
    }

    public static string? ValidateInitial(JsonElement args, string initialName, string expected, string mode)
    {
        // The adapter also checks the contract against live state before invoking a control.
        // Never silently choose a different comparison rule after the action was approved.
        if (BindImplicitName(args, initialName, out var bound) is { } bindError) return bindError;
        if (!bound.Equals(args)) return "Bind the legacy numeric display expectation before approval; use expectedNumber or expectedText explicitly.";
        if (mode != "numeric_value" && (mode != "exact" || !args.TryGetProperty("comparisonMode", out _))) return null;
        if (initialName.Length > 512) return mode == "numeric_value" ? "Numeric display label is too long to bind safely." : null;
        try
        {
            var initial = NumericToken.Matches(initialName);
            if (mode == "numeric_value" && initial.Count != 1)
                return "expectedNumber requires one numeric token in the currently observed display so its label can be bound before dispatch. Use expectedText for a literal final label.";
            // The deprecated explicit exact mode must not start a mutation with a
            // changing numeric value in the observed field without a clear literal intent.
            // No silent mode switch. Literal exact matching remains available with expectedText alone.
            if (mode == "exact" && initial.Count == 1)
            {
                var final = NumericToken.Matches(expected);
                if (final.Count == 1 && (IsBareNumber(expected) && !IsBareNumber(initialName) ||
                    SameContext(initialName, initial[0], expected, final[0]) && initialName != expected))
                    return "The legacy exact mode is fragile for a changing numeric display. Supply expectedNumber without comparisonMode for numeric verification; for a deliberately literal final name supply expectedText alone.";
            }
            return null;
        }
        catch (RegexMatchTimeoutException) { return "The display expectation could not be checked safely."; }
    }

    // Numeric grouping applies only to one isolated numeric token. A bare value may
    // match a labelled field only if the surrounding label was already observed on
    // that exact field before dispatch. Never strip arbitrary text or change signs.
    private static readonly Regex NumericToken = new(@"(?<![\p{L}\p{N}.,+−-])[-+]?(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?:\.[0-9]+)?(?![\p{L}\p{N}.,+−-])",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static bool SameContext(string left, Match l, string right, Match r) =>
        left[..l.Index] == right[..r.Index] && left[(l.Index + l.Length)..] == right[(r.Index + r.Length)..];

    // Compare finite decimal values without floating-point rounding or overflow.
    private static string NormalizeNumber(string token)
    {
        string digits = token.Replace(",", "", StringComparison.Ordinal);
        bool negative = digits.StartsWith('-');
        if (negative || digits.StartsWith('+')) digits = digits[1..];
        int dot = digits.IndexOf('.');
        string whole = (dot < 0 ? digits : digits[..dot]).TrimStart('0');
        if (whole.Length == 0) whole = "0";
        string fraction = dot < 0 ? "" : digits[(dot + 1)..].TrimEnd('0');
        bool zero = whole == "0" && fraction.Length == 0;
        return (negative && !zero ? "-" : "") + whole + (fraction.Length == 0 ? "" : "." + fraction);
    }

    public static bool Matches(string? observed, string expected, string mode, string? initialName = null)
    {
        if (mode == "exact") return string.Equals(observed, expected, StringComparison.Ordinal);
        if (mode is not ("numeric_grouping" or "numeric_value") || observed is null || observed.Length > 512 || expected.Length > 150) return false;
        try
        {
            var a = NumericToken.Matches(observed); var b = NumericToken.Matches(expected);
            if (a.Count != 1 || b.Count != 1) return false;
            var x = a[0]; var y = b[0];
            if (mode == "numeric_value" ? NormalizeNumber(x.Value) != NormalizeNumber(y.Value) :
                x.Value.Replace(",", "", StringComparison.Ordinal) != y.Value.Replace(",", "", StringComparison.Ordinal)) return false;
            if (mode == "numeric_grouping" && SameContext(observed, x, expected, y)) return true;
            // A numeric-only expectation deliberately omits a stable accessibility label.
            // Bind that label to the actual pre-action observation, not a guessed prefix.
            if (y.Index != 0 || y.Length != expected.Length || initialName is null || initialName.Length > 512) return false;
            var initial = NumericToken.Matches(initialName);
            return initial.Count == 1 && SameContext(observed, x, initialName, initial[0]);
        }
        catch (RegexMatchTimeoutException) { return false; }
    }

}

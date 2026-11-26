using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>One explicitly approved, finite series of observed UIA button invocations and an independent read-back.</summary>
public sealed class DesktopControlSequenceTool(IDesktopAutomationBackend desktop) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("desktop.control_sequence",
        "Invoke 2–12 already-observed visible buttons in one stable window IN ORDER, with one exact approval. " +
        "Use steps as exact observed name strings when unique, or objects with name and exact controlId when ambiguous. The host resolves them before approval. Select a unique observed display AutomationId. Supply expectedNumber for a numeric outcome, without a label or comparisonMode; supply expectedText alone for literal text or identifiers. " +
        "Stops on stale target or failed dispatch; no automatic replay. Use only for known ordered interactions, not unknown dialogs or changing windows.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "windowId":{"type":"string","minLength":8,"maxLength":100},
          "steps":{"type":"array","items":{"anyOf":[{"type":"string","minLength":1,"maxLength":150},{"type":"object","properties":{
            "controlId":{"type":"string","minLength":8,"maxLength":100},
            "name":{"type":"string","minLength":1,"maxLength":150}},"required":["name"],"additionalProperties":false}]}},
          "observeAutomationId":{"type":"string","minLength":1,"maxLength":120},
          "expectedNumber":{"description":"For numeric outcomes only: the number, without a label. Do not provide expectedText, expectedName or comparisonMode.","anyOf":[{"type":"number"},{"type":"string","minLength":1,"maxLength":150}]},
          "expectedText":{"type":"string","minLength":1,"maxLength":150,"description":"Exact literal accessible name, including formatting and identifiers. Do not provide other expectation fields."},
          "expectedName":{"type":"string","minLength":1,"maxLength":150,"description":"Legacy field. When no comparisonMode is supplied, a single number matching the observed label is bound to numeric grouping before approval. Prefer expectedNumber or expectedText."},
          "comparisonMode":{"type":"string","enum":["exact","numeric_grouping"],"description":"Legacy compatibility only. Omit in new proposals; use expectedNumber or expectedText alone."}},
          "required":["windowId","steps","observeAutomationId"],"additionalProperties":false}
        """), HasSideEffects: true);

    public string GetTarget(JsonElement args) => DesktopToolInput.Target(args, "sequence") + "?steps=" + args.GetProperty("steps").GetArrayLength();

    public static bool DisplayMatches(string? observed, string expected, string mode, string? initialName = null) =>
        DesktopDisplayExpectation.Matches(observed, expected, mode, initialName);

    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) => Task.Run(() => Execute(args, ct), ct);

    private ToolReceipt Execute(JsonElement args, CancellationToken ct)
    {
        if (!JsonToolSchema.Validate(Descriptor.Parameters, args, out string error)) return ToolReceipt.Fail("ARGUMENT_SCHEMA", error);
        string window = DesktopToolInput.Window(args);
        // The executor accepts canonical IDs only. Name-only proposals are bound
        // against the current task receipt by the host, before authorization.
        if (args.GetProperty("steps").EnumerateArray().Any(s => s.ValueKind != JsonValueKind.Object || !s.TryGetProperty("controlId", out _)))
            return ToolReceipt.Fail("DESKTOP_SEQUENCE_UNBOUND", "Resolve exact control identities before approval and execution.");
        var steps = args.GetProperty("steps").EnumerateArray().Select(s => (Id: s.GetProperty("controlId").GetString()!, Name: s.GetProperty("name").GetString()!)).ToArray();
        string automationId = args.GetProperty("observeAutomationId").GetString()!;
        if (!DesktopDisplayExpectation.TryRead(args, out string expected, out string mode, out string expectationError))
            return ToolReceipt.Fail("DESKTOP_SEQUENCE_EXPECTATION", expectationError);
        if (steps.Length is < 2 or > 12 || steps.Any(s => string.IsNullOrWhiteSpace(s.Name) || s.Name.Any(char.IsControl)) ||
            string.IsNullOrWhiteSpace(automationId) || string.IsNullOrWhiteSpace(expected) || expected.Any(char.IsControl))
            return ToolReceipt.Fail("DESKTOP_SEQUENCE_INVALID", "Provide 2–12 named observed buttons and an exact display expectation.");
        int dispatched = 0;
        string? observed = null;
        var completed = new List<string>();
        bool Matches(DesktopInspection inspection, (string Id, string Name) step) =>
            inspection.Window.WindowId == window && inspection.Controls.Count(c => c.ControlId == step.Id && c.Name == step.Name && c.Enabled && !c.Offscreen && c.CanInvoke) == 1;
        DesktopControl[] Displays(DesktopInspection inspection) => inspection.Controls.Where(c => c.AutomationId == automationId && !c.Offscreen).ToArray();
        ToolReceipt Stop(string message, string code) => dispatched == 0 ? ToolReceipt.Fail(code, message, recovery: ToolRecovery.Reobserve) :
            ToolReceipt.Ok(message, new { Verified = false, DispatchedCount = dispatched, RequestedCount = steps.Length,
                CompletedNames = completed.ToArray(), ObservedName = observed, ExpectedName = expected, ComparisonMode = mode, StopCode = code }, changedState: true, recovery: ToolRecovery.UnverifiedEffect);
        try
        {
            ct.ThrowIfCancellationRequested();
            var initial = desktop.Inspect(window);
            if (!steps.All(s => Matches(initial, s)) || Displays(initial).Length != 1)
                return Stop("The approved sequence or display is no longer uniquely observable; nothing invoked.", "DESKTOP_SEQUENCE_STALE");
            string initialName = Displays(initial)[0].Name;
            if (DesktopDisplayExpectation.ValidateInitial(args, initialName, expected, mode) is { } initialError)
                return ToolReceipt.Fail("DESKTOP_SEQUENCE_EXPECTATION", initialError + " Nothing invoked.");
            foreach (var step in steps)
            {
                ct.ThrowIfCancellationRequested();
                if (!Matches(desktop.Inspect(window), step))
                    return Stop("A remaining approved control changed; the sequence stopped.", "DESKTOP_SEQUENCE_STALE");
                // The backend re-resolves the exact control and window immediately before invoking it.
                var action = desktop.Invoke(window, step.Id);
                if (action.Dispatched) dispatched++;
                if (!action.Success || !action.Dispatched)
                    return Stop("An approved control did not dispatch successfully; the sequence stopped.", action.ErrorCode ?? "DESKTOP_SEQUENCE_DISPATCH_FAILED");
                completed.Add(step.Name);
                if (ct.WaitHandle.WaitOne(120)) ct.ThrowIfCancellationRequested();
            }
            for (int i = 0; i < 12; i++)
            {
                ct.ThrowIfCancellationRequested();
                var displays = Displays(desktop.Inspect(window));
                observed = displays.Length == 1 ? displays[0].Name : null;
                if (DisplayMatches(observed, expected, mode, initialName))
                    return ToolReceipt.Ok("Approved ordered controls dispatched and final accessible display independently matched under the selected comparison mode.",
                        new { Verified = true, DispatchedCount = dispatched, RequestedCount = steps.Length, CompletedNames = completed.ToArray(), ObservedName = observed, ExpectedName = expected, ComparisonMode = mode }, changedState: true);
                if (ct.WaitHandle.WaitOne(100)) ct.ThrowIfCancellationRequested();
            }
            return Stop("Approved sequence dispatched, but the observed display did not satisfy the requested verification.", "DESKTOP_SEQUENCE_MISMATCH");
        }
        catch (DesktopAutomationException e) { return Stop("The approved desktop target became unavailable: " + e.Message, e.Code); }
    }
}

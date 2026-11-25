using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>One explicitly approved, finite series of observed UIA button invocations and an independent read-back.</summary>
public sealed class DesktopControlSequenceTool(IDesktopAutomationBackend desktop) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("desktop.control_sequence",
        "Invoke 2–12 already-observed visible buttons in one stable window IN ORDER, with one exact approval. " +
        "Copy each controlId and name from the latest window inspection. Read back one uniquely observed display AutomationId and compare its exact final accessible Name. " +
        "Stops on stale target or failed dispatch; no automatic replay. Use only for known ordered interactions, not unknown dialogs or changing windows.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "windowId":{"type":"string","minLength":8,"maxLength":100},
          "steps":{"type":"array","items":{"type":"object","properties":{
            "controlId":{"type":"string","minLength":8,"maxLength":100},
            "name":{"type":"string","minLength":1,"maxLength":150}},"required":["controlId","name"],"additionalProperties":false}},
          "observeAutomationId":{"type":"string","minLength":1,"maxLength":120},
          "expectedName":{"type":"string","minLength":1,"maxLength":150}},
          "required":["windowId","steps","observeAutomationId","expectedName"],"additionalProperties":false}
        """), HasSideEffects: true);

    public string GetTarget(JsonElement args) => DesktopToolInput.Target(args, "sequence") + "?steps=" + args.GetProperty("steps").GetArrayLength();

    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) => Task.Run(() => Execute(args, ct), ct);

    private ToolReceipt Execute(JsonElement args, CancellationToken ct)
    {
        if (!JsonToolSchema.Validate(Descriptor.Parameters, args, out string error)) return ToolReceipt.Fail("ARGUMENT_SCHEMA", error);
        string window = DesktopToolInput.Window(args);
        var steps = args.GetProperty("steps").EnumerateArray().Select(s => (Id: s.GetProperty("controlId").GetString()!, Name: s.GetProperty("name").GetString()!)).ToArray();
        string automationId = args.GetProperty("observeAutomationId").GetString()!;
        string expected = args.GetProperty("expectedName").GetString()!;
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
                CompletedNames = completed.ToArray(), ObservedName = observed, ExpectedName = expected, StopCode = code }, changedState: true, recovery: ToolRecovery.UnverifiedEffect);
        try
        {
            ct.ThrowIfCancellationRequested();
            var initial = desktop.Inspect(window);
            if (!steps.All(s => Matches(initial, s)) || Displays(initial).Length != 1)
                return Stop("The approved sequence or display is no longer uniquely observable; nothing invoked.", "DESKTOP_SEQUENCE_STALE");
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
                if (string.Equals(observed, expected, StringComparison.Ordinal))
                    return ToolReceipt.Ok("Approved ordered controls dispatched and final accessible display name independently matched.",
                        new { Verified = true, DispatchedCount = dispatched, RequestedCount = steps.Length, CompletedNames = completed.ToArray(), ObservedName = observed, ExpectedName = expected }, changedState: true);
                if (ct.WaitHandle.WaitOne(100)) ct.ThrowIfCancellationRequested();
            }
            return Stop("Approved sequence dispatched, but the final accessible display did not match the expected name.", "DESKTOP_SEQUENCE_MISMATCH");
        }
        catch (DesktopAutomationException e) { return Stop("The approved desktop target became unavailable: " + e.Message, e.Code); }
    }
}

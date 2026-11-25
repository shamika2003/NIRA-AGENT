using System.Text.Json;

namespace Nira.Core;

/// <summary>Deterministic recovery boundaries. The model chooses a new plan; the host
/// only prevents unsafe replay, requires fresh observations, and preserves failures.</summary>
public static class ToolRecoveryPolicy
{
    public const string UnverifiedEffectCode = "UNVERIFIED_EFFECT";

    public static bool HasUnverifiedEffect(ConversationState state) =>
        state.Operations.Any(op => op.Recovery == ToolRecovery.UnverifiedEffect) ||
        // Compatibility for checkpoints created before typed recovery metadata.
        state.RetryBlockCode is UnverifiedEffectCode or "DESKTOP_TEXT_NOT_VERIFIED" or "DESKTOP_SEQUENCE_NOT_VERIFIED";

    internal static AgentOperation? UnresolvedRejection(ConversationState state)
    {
        // A later successful mutation is evidence of an alternative action, not
        // proof that it satisfied the user's goal; the model must assess that.
        int rejected = state.Operations.FindLastIndex(op => op.Recovery == ToolRecovery.Reobserve);
        if (rejected < 0) return null;
        foreach (var op in state.Operations.Skip(rejected + 1))
        {
            if (op.Status != OperationStatus.Succeeded || op.ResultJson is null) continue;
            try
            {
                using var receipt = JsonDocument.Parse(op.ResultJson);
                var root = receipt.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ChangedState", out var changed) && changed.ValueKind == JsonValueKind.True &&
                    (!root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object ||
                     !data.TryGetProperty("Verified", out var verified) || verified.ValueKind != JsonValueKind.False)) return null;
            }
            catch (JsonException) { /* No usable evidence. */ }
        }
        return state.Operations[rejected];
    }

    internal static string? BeforeAction(ConversationState state, ToolRegistry registry,
        ToolDescriptor descriptor, JsonElement arguments)
    {
        if (!descriptor.HasSideEffects) return null;
        int lastRejected = state.Operations.FindLastIndex(op => op.Recovery == ToolRecovery.Reobserve);
        if (lastRejected < 0) return null;
        if (!state.Operations.Skip(lastRejected + 1).Any(op => op.Status == OperationStatus.Succeeded &&
            registry.TryGet(op.ToolName, out var observed) && observed is not null && !observed.Descriptor.HasSideEffects))
            return "A previous action did not dispatch. Obtain a fresh relevant read-only observation before proposing another action.";
        int attempts = 0;
        foreach (var op in state.Operations.Where(op => op.Recovery == ToolRecovery.Reobserve && op.ToolName == descriptor.Name))
        {
            using var previous = JsonDocument.Parse(op.ArgumentsJson);
            if (JsonElement.DeepEquals(previous.RootElement, arguments)) attempts++;
        }
        return attempts >= 2 ? "This exact action has already been rejected twice. Choose a materially different authorized approach or explain the blocker." : null;
    }
}

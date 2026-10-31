namespace NIRAAgent.AI.Cognition;

// Compatibility shim: the single Response Presentation mini-block owns both
// character expression and native UI policy. No independent personality here.
internal static class NIRACharacterPromptModule
{
    internal static string ForEntry(string? yaml) =>
        NIRAResponsePresentationModule.ForPrimary(yaml);

    internal static string ForFinalDelivery(string? yaml) =>
        NIRAResponsePresentationModule.ForFinal(yaml);
}

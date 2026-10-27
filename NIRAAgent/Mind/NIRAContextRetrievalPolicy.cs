namespace NIRAAgent.Mind;

// A history lookup can resolve what the user meant, but cannot validate
// that a file is still present on this computer. This policy is deliberately
// limited to filesystem observations: it does not alter browser research,
// unrelated memory work, or model-selected tool execution.
internal static class NIRAContextRetrievalPolicy
{
    // Context expansion cannot preempt an executable capability in a mixed
    // model decision. A call to a real tool is checked by the standard
    // authorizer/schema preflight, and further context may follow its receipt.
    internal static bool ShouldExpandContextBeforeWork(
        int contextCount, int capabilityIdCount, int conversationSearchCount,
        int proposedCapabilityCount)
    {
        return proposedCapabilityCount == 0 &&
            (contextCount > 0 || capabilityIdCount > 0 || conversationSearchCount > 0);
    }

    internal static bool IsHistoryOnlyAfterFileObservation(
        bool observedFileSource,
        bool expandedNewSection,
        int requestedMemorySearchCount,
        int requestedConversationSearchCount,
        bool hasOtherWork)
    {
        return observedFileSource && !expandedNewSection &&
            requestedMemorySearchCount == 0 &&
            requestedConversationSearchCount > 0 && !hasOtherWork;
    }
}

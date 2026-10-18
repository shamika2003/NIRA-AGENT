namespace NIRAAgent.Mind;

// A history lookup can resolve what the user meant, but cannot validate
// that a file is still present on this computer. This policy is deliberately
// limited to filesystem observations: it does not alter browser research,
// unrelated memory work, or model-selected tool execution.
internal static class NIRAContextRetrievalPolicy
{
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

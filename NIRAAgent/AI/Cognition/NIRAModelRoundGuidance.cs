namespace NIRAAgent.AI.Cognition;

// Advisory model-cost guidance. Executive Cycle is an observed orchestration
// counter, NOT a promise of the total LLM calls (repairs, reviewers and other
// specialist model calls may occur within a cycle). No maximum-cycle policy,
// completion certificate, tool permission or bypass is created here.
internal static class NIRAModelRoundGuidance
{
    internal static string Describe(int cycle, bool internalWork = false)
    {
        int current = Math.Max(cycle, 1);
        string stageGuidance = internalWork
            ? "This is BLOCK 02 internal work. If the objective is now satisfied, " +
              "return State=Complete with the full grounded findings, " +
              "emitReply=true, replyReady=false, characterReady=false. " +
              "BLOCK 03 will own the final character expression. "
            : "This is BLOCK 01 primary cognition. If the objective can be " +
              "answered directly without external work, return a full NIRA-voiced " +
              "response with state=Complete, emitReply=true, replyReady=true, " +
              "characterReady=true and an appropriate conversationOnly=true " +
              "OR selfContainedAnswer=true (never both). ";
        return $"MODEL CALL EFFICIENCY — Executive decision cycle {current} " +
            "(not a fixed call budget). Minimize unnecessary model calls while " +
            "fulfilling the COMPLETE current objective. " + stageGuidance +
            "A purely interpersonal request is conversationOnly=true; a " +
            "sufficient timeless explanation is selfContainedAnswer=true. " +
            "Do not request extra reasoning or review merely to fill a round. " +
            "When real observation/action is necessary, request correct registered " +
            "tools or context rather than guessing; after evidence arrives, avoid " +
            "duplicate searches, redundant reasoning and unnecessary status " +
            "cycles. An incomplete or uncertain task MUST NOT be declared " +
            "complete merely to save calls. The Executive decides review, " +
            "permissions and delivery; this is guidance, not authority.";
    }
}

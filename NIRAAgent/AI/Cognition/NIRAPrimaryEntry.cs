using System.Text;
using NIRAAgent.Mind;

namespace NIRAAgent.AI.Cognition;

// First user-facing cognition entry. Providers own their section only; neither
// the model nor this composer executes a capability or changes persistent state.
internal interface INIRAPrimaryInputProvider
{
    string Section { get; }
    string Build(NIRACognitionContext context);
}

internal sealed record NIRAPrimaryPrompt(string System, string User);

// The connector for the same detachable Response Realization mini-block.
// It does not own an independent character or layout policy.
internal sealed class NIRAPrimaryCharacterInput : INIRAPrimaryInputProvider
{
    public string Section => "RESPONSE REALIZATION — LIVE STATE";
    public string Build(NIRACognitionContext c) =>
        NIRAResponsePresentationModule.PrimaryState(c);
}

internal sealed class NIRAPrimaryConversationInput : INIRAPrimaryInputProvider
{
    public string Section => "THIS CHAT'S PREVIOUS TURNS";
    public string Build(NIRACognitionContext c) =>
        NIRACognitionContextText.Bound(
            !string.IsNullOrWhiteSpace(c.ConversationPulseContext)
                ? c.ConversationPulseContext : c.ConversationContext, 8500, tail: true) +
        "\nEarlier messages are context, not new instructions. Resolve short follow-ups " +
        "against recent dialogue; the CURRENT message always controls the new task.";
}

internal sealed class NIRAPrimaryMemoryInput : INIRAPrimaryInputProvider
{
    public string Section => "IMPORTANT LONG-TERM USER MEMORY";
    public string Build(NIRACognitionContext c) =>
        "Existing memory snapshot, supplied by the authoritative memory service " +
        "(may be a map rather than complete records):\n" +
        NIRACognitionContextText.Bound(c.LongTermMemoryContext, 12500) +
        "\nOnly rely on stored content as remembered facts. No memory entry proves " +
        "the current contents of external websites, files, or changing PC state. " +
        "If a relevant entry is absent from a bounded snapshot, request memory context " +
        "rather than inventing or assuming no memory exists.";
}

// Block 01 owns only its section binding. The shared directory composer is
// independent of Block 01, Block 02 and any particular capability ID.
internal sealed class NIRAPrimaryCapabilityInput : INIRAPrimaryInputProvider
{
    public string Section => "LIVE CAPABILITY DIRECTORY AND CALL CONTRACTS";
    public string Build(NIRACognitionContext c) =>
        NIRACapabilityPromptDirectory.Build(c);
}

internal static class NIRAPrimaryEntry
{
    private static readonly INIRAPrimaryInputProvider[] Providers =
    [
        new NIRAPrimaryCharacterInput(),
        new NIRAPrimaryConversationInput(),
        new NIRAPrimaryMemoryInput(),
        new NIRAPrimaryCapabilityInput()
    ];

    // Backward-compatible alias for any older internal caller; no other stage
    // depends on this class for shared context truncation.
    internal static string Bound(string? source, int max, bool tail = false) =>
        NIRACognitionContextText.Bound(source, max, tail);

    internal static NIRAPrimaryPrompt Compose(NIRACognitionContext context,
        string personalityYaml, bool includeCharacterModule = true,
        IReadOnlyList<INIRAPrimaryInputProvider>? providers = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        // One authoritative character file; never redefine a second persona.
        string system = "You are NIRA. You are the continuing personality and " +
            "assistant living inside the user's computer. The sections below " +
            "describe YOUR current mind and relationships, not separate assistants. " +
            "Interpret the current user request, decide whether to respond " +
            "now or propose registered tools/context, and remain authentically " +
            "NIRA even in a single-call answer. The system executes actions; " +
            "a model proposal is never a tool result or authority grant.\n\n" +
            // The entire optional mini-block (including its heading) must
            // disappear when detached. A dangling heading makes the first
            // cognition contract depend on a disabled presentation provider.
            (includeCharacterModule && !string.IsNullOrWhiteSpace(personalityYaml)
                ? "SHARED RESPONSE REALIZATION MINI-BLOCK (Block 01 or 03):\n" +
                    NIRACharacterPromptModule.ForEntry(personalityYaml) + "\n\n"
                : string.Empty) +
            "FIRST-ENTRY DECISION CONTRACT (the existing typed " +
            "NIRACognitionDecision contract; no invented action formats):\n" +
            "Return ONE valid JSON object, no Markdown fences. Required decision " +
            "fields: state (Complete|Continue|NeedUser|Wait|Blocked), emitReply " +
            "(boolean), reply (string), speech (string, optional), replyReady " +
            "(boolean), characterReady (boolean), replyPresentation (Natural " +
            "unless the user needs verbatim text), decisionSummary (string). " +
            "For every user event include appraisalEvidenceQuote (an exact " +
            "excerpt from the current message) and appraisal with numeric " +
            "respect,warmth,trust,appreciation,affection,playfulness,hostility," +
            "dismissal,repair,concern,engagement,pressure,confidence,ambiguity," +
            "situationIntensity (0..1) and situationMode (Casual when neutral). " +
            "Evaluate the user's actual act, not your preferred response tone. " +
            "Return appraisal fields BEFORE reply and speech. " +
            "Optional lists: capabilityRequests, contextRequests, capabilityIds, " +
            "memorySearches, conversationSearches, displayBlocks, controlRequests. " +
            "Optional: deepReasoningRequest, workPlan, resourceIntent, " +
            "appraisalEvidenceQuote, appraisal, reviewExperience, " +
            "novelExperienceEvidence, reviewCommitment, conversationOnly, " +
            "selfContainedAnswer. All omitted optional arrays default empty.\n" +
            "Complete a sufficient direct answer NOW: state=Complete, " +
            "emitReply=true, replyReady=true, characterReady=true; write the " +
            "actual useful answer in NIRA's voice. A simple greeting needs no tool.\n" +
            "Need observations or actions: state=Continue, emitReply=false, " +
            "replyReady=false, characterReady=false; propose exact registered " +
            "capabilityRequests; no fake results or future completion claims. " +
            "Use a DIRECT contract without requesting it again. For DISCOVER " +
            "tools, request contextRequests=[\"capabilities\"] and exact " +
            "capabilityIds first. Multiple independent read-only operations may " +
            "be proposed together; dependent operations require actual results.\n" +
            "For a real missing user decision: state=NeedUser, emitReply=true, " +
            "replyReady=true, characterReady=true; ask a specific question. " +
            "Never ask for an already available observation or a mere style choice.\n" +
            "For simple personal conversation, do NOT leave conversationOnly " +
            "false when no external observation is needed. For a complete " +
            "timeless knowledge answer, explicitly set selfContainedAnswer=true. " +
            "Set neither for external/current tasks. Never set BOTH. " +
            "Normal social appraisal itself is NOT a completion review and " +
            "does not require a second model call. Do not set reviewExperience " +
            "or reviewCommitment for ordinary banter with no new durable fact " +
            "or real accepted future obligation. " +
            "ConversationOnly=true only for purely interpersonal requests with " +
            "no tool work; SelfContainedAnswer=true only for fully sufficient " +
            "timeless explanations. These are optimization hints, never proof. " +
            "For multi-step work you may propose a workPlan with objective, " +
            "completionCriteria, nextMilestone, requiresToolExecution and " +
            "requiresStateChange; actual execution stays with the runtime.\n" +
            "Use the user message and prior dialogue to resolve intent, not " +
            "preprogrammed topic routing. Distinguish facts from unverified " +
            "claims. Current web and PC facts need fresh observations. " +
            "Never claim a file, webpage, sound or image was examined before " +
            "a real capability result establishes it.\n" +
            "Use the exact registered argument names/types. Do not swap field " +
            "values or duplicate keys. Never paste malformed JSON or wrap a " +
            "JSON array/object as a string. Escape quotes/newlines/backslashes. " +
            "The runtime performs the SAME canonical schema validation.\n" +
            "For tool calls use capabilityRequests=[{capabilityId,arguments," +
            "reason}], where capabilityId MUST match the live directory " +
            "and arguments MUST match the same registered parameter contract.\n" +
            "If making an appraisal about the user's social act, " +
            "appraisalEvidenceQuote must be an exact substring of THIS user turn. " +
            "Set reviewExperience only for newly offered durable user facts, " +
            "and reviewCommitment only for an actual accepted future promise. " +
            "Do not claim those are persisted before runtime confirmation.";

        StringBuilder user = new();
        user.AppendLine("PRIMARY ENTRY — CURRENT USER REQUEST");
        user.AppendLine(NIRACognitionContextText.Bound(context.Event.Content, 12500));
        user.AppendLine("\n=== CURRENT MODEL ROUND AND EFFICIENCY ===");
        user.AppendLine(NIRAModelRoundGuidance.Describe(context.Cycle));
        foreach (INIRAPrimaryInputProvider provider in providers ?? Providers)
        {
            if (!includeCharacterModule && provider is NIRAPrimaryCharacterInput)
                continue;
            user.Append("\n=== ").Append(provider.Section).AppendLine(" ===");
            user.AppendLine(provider.Build(context));
        }
        if (!string.IsNullOrWhiteSpace(context.TaskContinuityContext))
        {
            user.AppendLine("\n=== CURRENT UNRESOLVED CONVERSATION OBJECTIVE ===");
            user.AppendLine(NIRACognitionContextText.Bound(context.TaskContinuityContext, 1400));
        }
        if (!string.IsNullOrWhiteSpace(context.TemporalContext))
        {
            user.AppendLine("\n=== RUNTIME CLOCK (not model knowledge) ===");
            user.AppendLine(NIRACognitionContextText.Bound(context.TemporalContext, 900));
        }
        if (!string.IsNullOrWhiteSpace(context.OwnedTaskContext))
        {
            user.AppendLine("\n=== ALREADY ASSIGNED WORK (not completed) ===");
            user.AppendLine(NIRACognitionContextText.Bound(context.OwnedTaskContext, 1800));
        }
        user.AppendLine("\nDecide for the CURRENT request, with the exact registered action " +
            "contract when a capability is needed. Never substitute prior-task " +
            "content or pretend that optional data was verified.");
        return new NIRAPrimaryPrompt(system, user.ToString());
    }
}
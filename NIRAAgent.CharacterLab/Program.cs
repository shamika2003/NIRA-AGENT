using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NIRAAgent.AI.Cognition;
using NIRAAgent.AI.Ollama;
using NIRAAgent.Character;
using NIRAAgent.Character.State;
using NIRAAgent.Character.Dynamics;
using NIRAAgent.Mind;
using NIRAAgent.Capabilities;
using NIRAAgent.Presentation;

// Developer-only, transient character test bench.
// Never constructs a character store, persistence service, executive, or social-history writer.
// ALL hypothetical snapshots live only in this process. Model calls are real and billed normally.

var scenarios = CharacterScenario.All;
NIRACharacterSnapshot current = scenarios[0].State;
string currentName = scenarios[0].Name;
List<(string Speaker, string Content)> localConversation = new();
List<LabTurnRecord> turnRecords = new();
bool progression = false;
DateTimeOffset lastTransition = DateTimeOffset.UtcNow;
int turnSequence = 0;
NIRACharacterSnapshot initialStoryState = current;

Console.WriteLine("NIRA CHARACTER LAB | Same cognition/model, isolated relationship and mood snapshots");
Console.WriteLine("No character store, conversation archive, memory store or application bridge is accessed.");
Console.WriteLine("Type help for commands. 'check' works offline; 'ask', 'compare', 'run' call Ollama cloud.");

if (args.Contains("--check", StringComparer.OrdinalIgnoreCase))
{
    return OfflineChecks.Run(scenarios) | LabProgressionChecks.Run();
}

while (true)
{
    Console.Write($"\nlab:{currentName}> ");
    string? input = Console.ReadLine();
    if (input is null) break;
    string line = input.Trim();
    if (line.Length == 0) continue;

    string verb = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
    string rest = line.Length > verb.Length ? line[(verb.Length + 1)..].Trim() : string.Empty;
    try
    {
        switch (verb)
        {
            case "help":
                Console.WriteLine("""
                    scenarios                   List isolated sample character states
                    use <name>                  Switch to that state, clear lab-only chat
                    preview                     Show numeric state and actual delivery envelope
                    set relationship.trust 0.9  Set any numeric relationship or mood field
                    set mood.irritation 0.75    Change the current test snapshot only
                    set situation.mode Serious  Choose Casual/FocusedWork/Serious/Sensitive
                    reset                       Restore original state of chosen preset
                    ask <user message>          Ask real NIRA cognition with this state
                    pipeline <user message>     Cognition + SAME final-expression model prompt used on desktop (2 cloud calls)
                    compare <name1> <name2> | <user message>
                                                Same question, two independent states
                    run                         Execute each preset's sample message (9 calls)
                    progression on/off         Enable or disable live, gradual social-state updates
                    story list                 Show guided multi-turn test scripts
                    story bonding             Run a 12-turn relationship test (real cloud calls)
                    story conflict            Run a 12-turn friction/repair test
                    story balance             Run a 12-turn playful/serious/sensitive test
                    stress 500                 Offline synthetic 500-turn dynamics stress test
                    stats                      Show changes during the current lab test
                    report                     Export the current lab test to a text file
                    check                      Offline structural/dynamics checks; no key needed
                    clear                       Clear lab conversation without changing state
                    exit                        Quit; no state is saved
                    """);
                break;
            case "scenarios":
                foreach (var sc in scenarios)
                    Console.WriteLine($"  {sc.Name,-13} {sc.Description}");
                break;
            case "use":
                {
                    CharacterScenario sc = GetScenario(rest, scenarios);
                    current = sc.State;
                    currentName = sc.Name;
                    localConversation.Clear();
                    turnRecords.Clear();
                    initialStoryState = current;
                    lastTransition = DateTimeOffset.UtcNow;
                    turnSequence = 0;
                    Console.WriteLine($"Loaded {sc.Name}. Lab chat cleared. NIRA live state is untouched.");
                    break;
                }
            case "reset":
                current = GetScenario(currentName, scenarios).State;
                localConversation.Clear();
                turnRecords.Clear();
                initialStoryState = current;
                lastTransition = DateTimeOffset.UtcNow;
                turnSequence = 0;
                Console.WriteLine("Preset restored; lab conversation cleared.");
                break;
            case "clear":
                localConversation.Clear();
                Console.WriteLine("Lab-only conversation cleared.");
                break;
            case "preview":
                ShowPreview(current);
                break;
            case "set":
                {
                    string[] parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2) throw new ArgumentException("Usage: set mood.irritation 0.7");
                    current = SnapshotEditor.Change(current, parts[0], parts[1]);
                    Console.WriteLine($"Changed {parts[0]} = {parts[1]}; no production value was changed.");
                    ShowSummary(current);
                    break;
                }
            case "progression":
                if (rest.Equals("on", StringComparison.OrdinalIgnoreCase))
                    progression = true;
                else if (rest.Equals("off", StringComparison.OrdinalIgnoreCase))
                    progression = false;
                else if (rest.Length != 0)
                    throw new ArgumentException("Usage: progression on | progression off");
                lastTransition = DateTimeOffset.UtcNow;
                Console.WriteLine($"Progression {(progression ? "ON" : "OFF")}: model appraisals " +
                    (progression ? "will update ONLY the current lab snapshot." : "will not alter the snapshot."));
                break;
            case "story":
                if (rest.Length == 0 || rest.Equals("list", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (LabStory script in LabStory.All)
                        Console.WriteLine($"  {script.Name,-12} {script.Description} [{script.Messages.Count} turns; starts {script.InitialScenario}]");
                    break;
                }
                {
                    LabStory script = LabStory.All.FirstOrDefault(x => x.Name.Equals(rest, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException("Unknown story. Try: story list");
                    Console.Write($"{script.Messages.Count} REAL cloud calls, new isolated session, state changes. Type YES: ");
                    if (Console.ReadLine()?.Trim() != "YES") { Console.WriteLine("Cancelled."); break; }
                    CharacterScenario sc = GetScenario(script.InitialScenario, scenarios);
                    current = sc.State;
                    currentName = sc.Name;
                    initialStoryState = current;
                    localConversation.Clear();
                    turnRecords.Clear();
                    turnSequence = 0;
                    lastTransition = DateTimeOffset.UtcNow;
                    bool oldProgression = progression;
                    progression = true;
                    try
                    {
                        foreach (string message in script.Messages)
                        {
                            Console.WriteLine($"\nTURN {turnSequence + 1}/{script.Messages.Count} | User: {message}");
                            var outcome = await AskAsync(current, message, localConversation);
                            Console.WriteLine($"NIRA: {outcome.Reply}");
                            if (!CommitLabTurn(message, outcome))
                            {
                                // In a no-tools lab, Continue/NeedUser/Wait is a
                                // valid model proposal but not a completed answer.
                                // Preserve the gap, do not invent a reply or
                                // commit appraisal, and carry on with the story.
                                RecordUnresolvedLabTurn(message, outcome);
                                Console.WriteLine("Story continuing; this turn is UNRESOLVED, " +
                                    "no action or social-state mutation was performed.");
                            }
                        }
                    }
                    finally
                    {
                        progression = oldProgression;
                    }
                    PrintLabStats(initialStoryState, current, turnRecords);
                    int gaps = turnRecords.Count(x => x.AppraisalStatus.StartsWith("UNRESOLVED:", StringComparison.Ordinal));
                    Console.WriteLine($"Story finished: {script.Messages.Count} attempted; " +
                        $"{turnRecords.Count - gaps} complete, {gaps} unresolved (not scored as success). " +
                        "Use report to export transcript. NIRA's real stores remain untouched.");
                    break;
                }
            case "stress":
                if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int turns) ||
                    turns < 1 || turns > 10000)
                    throw new ArgumentException("Usage: stress <turns>, between 1 and 10000");
                LabProgressionChecks.Stress(turns);
                break;
            case "stats":
                PrintLabStats(initialStoryState, current, turnRecords);
                break;
            case "report":
                {
                    string folder = Path.Combine(Directory.GetCurrentDirectory(), "CharacterLabReports");
                    Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, $"NIRA_LAB_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.WriteAllText(file, LabTurnRecord.Export(initialStoryState, current, turnRecords));
                    Console.WriteLine($"Report: {file}");
                    break;
                }
            case "check":
                OfflineChecks.Run(scenarios);
                LabProgressionChecks.Run();
                break;
            case "ask":
                {
                    if (rest.Length == 0) throw new ArgumentException("Usage: ask <user message>");
                    var outcome = await AskAsync(current, rest, localConversation);
                    Console.WriteLine($"\nNIRA: {outcome.Reply}");
                    Console.WriteLine($"[ModelState={outcome.State}; CharacterReady={outcome.CharacterReady}; ReplyReady={outcome.ReplyReady}; calls=1]");
                    if (outcome.State != NIRACognitionState.Complete || !outcome.EmitReply)
                        Console.WriteLine("[This is a test-only cognition proposal; actions/context requests are NOT executed.]");
                    if (outcome.State == NIRACognitionState.Complete && outcome.EmitReply)
                        CommitLabTurn(rest, outcome);
                    break;
                }
            case "pipeline":
                {
                    if (rest.Length == 0) throw new ArgumentException("Usage: pipeline <user message>");
                    NIRACognitionDecision draft = await AskAsync(current, rest, localConversation);
                    Console.WriteLine($"[Cognition: {draft.State}; EmitReply={draft.EmitReply}]");
                    Console.WriteLine($"DRAFT: {draft.Reply}");
                    if (!draft.EmitReply || draft.ReplyPresentation != NIRAReplyPresentationMode.Natural ||
                        (draft.State != NIRACognitionState.Complete && draft.State != NIRACognitionState.NeedUser))
                    {
                        Console.WriteLine("[Unfinished or exact response: no character-expression pass performed.]");
                        break;
                    }
                    // Apply the exact isolated dynamics once to form the state
                    // which production would have after this user appraisal.
                    NIRACharacterSnapshot after = LabProgression.ApplyModelAppraisal(
                        current, rest, draft, turnSequence + 1, TimeSpan.Zero).After;
                    string previousChat = localConversation.Count == 0
                        ? "No earlier conversation in this isolated lab test."
                        : string.Join("\n", localConversation.TakeLast(18)
                            .Select(t => $"{t.Speaker}: {t.Content}"));
                    NIRAResponseRealizationRequest request = new()
                    {
                        DraftReply = draft.Reply,
                        DraftSpeech = draft.Speech,
                        DecisionSummary = draft.DecisionSummary,
                        EventSource = "User",
                        EventName = "UserMessage",
                        TopicKey = "character-lab:pipeline",
                        EventContent = rest,
                        ConversationContext = previousChat,
                        TemporalContext = $"Current local date/time: {DateTimeOffset.Now:O}"
                    };
                    using var keys = new NIRAOllamaApiKeyService();
                    using var model = new OllamaClient(keys);
                    NIRAResponseRealizationResult realized =
                        await NIRAResponseRealizationService.RealizeIsolatedForLabAsync(
                            model, after, request);
                    Console.WriteLine($"FINAL NIRA: {realized.Reply}");
                    Console.WriteLine("[2 cloud calls: actual cognition + real production expression compiler; " +
                        "isolated state, no production memories or tools. Human evaluation required.]");
                    if (draft.State == NIRACognitionState.Complete)
                        CommitLabTurn(rest, draft with { Reply = realized.Reply });
                    break;
                }
            case "compare":
                {
                    int sep = rest.IndexOf('|');
                    if (sep < 0) throw new ArgumentException("Usage: compare guarded close | Do you care about me?");
                    string[] names = rest[..sep].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (names.Length != 2) throw new ArgumentException("Specify two scenario names.");
                    string question = rest[(sep + 1)..].Trim();
                    if (question.Length == 0) throw new ArgumentException("Specify a question after '|'.");
                    foreach (string name in names)
                    {
                        CharacterScenario sc = GetScenario(name, scenarios);
                        Console.WriteLine($"\n--- {sc.Name} / independent one-turn test ---");
                        var result = await AskAsync(sc.State, question, Array.Empty<(string, string)>());
                        Console.WriteLine($"NIRA: {result.Reply}");
                        Console.WriteLine($"[ModelState={result.State}; CharacterReady={result.CharacterReady}]");
                    }
                    Console.WriteLine("Compare wording, useful content, emotional distance and identity. A human must judge naturalness.");
                    break;
                }
            case "run":
                Console.Write("Nine real cloud model calls. Proceed? Type YES: ");
                if (Console.ReadLine()?.Trim() != "YES") { Console.WriteLine("Cancelled."); break; }
                foreach (CharacterScenario sc in scenarios)
                {
                    Console.WriteLine($"\n--- {sc.Name}: {sc.ExampleMessage} ---");
                    try
                    {
                        var result = await AskAsync(sc.State, sc.ExampleMessage, Array.Empty<(string, string)>());
                        Console.WriteLine($"NIRA: {result.Reply}");
                        Console.WriteLine($"[ModelState={result.State}; CharacterReady={result.CharacterReady}]");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FAILED {ex.GetType().Name}: {ex.Message}]");
                    }
                }
                Console.WriteLine("Review each output manually against the situation; no automatic personality PASS is claimed.");
                break;
            case "quit":
            case "exit":
                return 0;
            default:
                Console.WriteLine("Unknown command. Type help.");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
    }
}
return 0;

bool CommitLabTurn(string message, NIRACognitionDecision outcome)
{
    if (outcome.State != NIRACognitionState.Complete || !outcome.EmitReply)
        return false;

    turnSequence++;
    NIRACharacterSnapshot before = current;
    string appraisalStatus = "Progression OFF (snapshot unchanged)";
    if (progression)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan elapsed = now - lastTransition;
        lastTransition = now;
        LabTurnResult update = LabProgression.ApplyModelAppraisal(current, message, outcome, turnSequence, elapsed);
        current = update.After;
        appraisalStatus = update.Status;
        Console.WriteLine($"[SocialState] {appraisalStatus}");
        Console.WriteLine(LabProgression.Delta(before, current));
    }

    turnRecords.Add(new LabTurnRecord(turnSequence, message, outcome.Reply, before, current, appraisalStatus));
    localConversation.Add(("User", message));
    localConversation.Add(("NIRA", outcome.Reply));
    if (localConversation.Count > 24)
        localConversation.RemoveRange(0, localConversation.Count - 24);
    return true;
}

void RecordUnresolvedLabTurn(string message, NIRACognitionDecision outcome)
{
    turnSequence++;
    string status = $"UNRESOLVED: State={outcome.State}; EmitReply={outcome.EmitReply}; " +
        $"ContextRequests={outcome.ContextRequests.Count}; CapabilityIds={outcome.CapabilityIds.Count}";
    turnRecords.Add(new LabTurnRecord(turnSequence, message,
        $"[No committed NIRA reply; {outcome.State}]", current, current, status));
    // Preserve the user's actual message for follow-up continuity, but NEVER
    // pretend the model made a final response or executed any proposed action.
    localConversation.Add(("User", message));
    if (localConversation.Count > 24)
        localConversation.RemoveRange(0, localConversation.Count - 24);
    lastTransition = DateTimeOffset.UtcNow;
}

void PrintLabStats(NIRACharacterSnapshot start, NIRACharacterSnapshot after, IReadOnlyList<LabTurnRecord> turns)
{
    Console.WriteLine($"Lab turns: {turns.Count}; accepted grounded appraisals: " +
        $"{turns.Count(x => x.AppraisalStatus.StartsWith("APPLIED:", StringComparison.Ordinal))}");
    Console.WriteLine("Start: " + LabProgression.Values(start));
    Console.WriteLine("Now:   " + LabProgression.Values(after));
    Console.WriteLine("Delta: " + LabProgression.Delta(start, after));
    Console.WriteLine("Human review is required for personality quality and narrative continuity.");
}

static CharacterScenario GetScenario(string name, IReadOnlyList<CharacterScenario> scenarios) =>
    scenarios.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
    ?? throw new ArgumentException($"Unknown preset '{name}'. Type scenarios.");

static void ShowSummary(NIRACharacterSnapshot state)
{
    var r = state.Relationship;
    var m = state.Mood;
    var a = new NIRAAttitudeService().Evaluate(state, null);
    Console.WriteLine($"REL trust={r.Trust:F2} warmth={r.Warmth:F2} familiarity={r.Familiarity:F2} " +
                      $"openness={r.Openness:F2} attachment={r.Attachment:F2} playfulness={r.Playfulness:F2} friction={r.Friction:F2}");
    Console.WriteLine($"MOOD irritation={m.Irritation:F2} affection={m.Affection:F2} " +
                      $"amusement={m.Amusement:F2} concern={m.Concern:F2} valence={m.Valence:F2}");
    Console.WriteLine($"SITUATION {state.Situation.Mode} / {state.Situation.Intensity:F2}");
    Console.WriteLine($"DERIVED warmth={a.Warmth:F2} playfulness={a.Playfulness:F2} " +
                      $"assertiveness={a.Assertiveness:F2} patience={a.Patience:F2} " +
                      $"distance={a.EmotionalDistance:F2} restraint={a.Restraint:F2}");
}
static void ShowPreview(NIRACharacterSnapshot state)
{
    ShowSummary(state);
    Console.WriteLine("\nPRE-COMMIT DELIVERY CONTEXT (the exact production formatter):");
    var attitude = new NIRAAttitudeService().Evaluate(state, null);
    Console.WriteLine(NIRACognitionContextBuilder.BuildPreCommitCharacterDeliveryEnvelope(state, attitude));
}
static async Task<NIRACognitionDecision> AskAsync(
    NIRACharacterSnapshot state,
    string message,
    IReadOnlyList<(string Speaker, string Content)> conversation)
{
    using var keys = new NIRAOllamaApiKeyService();
    if (!keys.HasKey)
        throw new InvalidOperationException("Ollama cloud key not found. Configure NIRA_OLLAMA_API_KEY for your Windows user as in main NIRA.");
    using var model = new OllamaClient(keys);
    var cognition = new NIRACognitionService(model);
    var attitude = new NIRAAttitudeService().Evaluate(state, null);
    string conversationText = conversation.Count == 0
        ? "No prior test conversation in this isolated lab session."
        : string.Join("\n", conversation.TakeLast(18).Select(c => $"{c.Speaker}: {c.Content}"));
    var context = new NIRACognitionContext
    {
        RunId = Guid.NewGuid(),
        Cycle = 1,
        Event = NIRAMindEvent.UserMessage(message, Guid.NewGuid(), "character-lab:direct-chat"),
        CharacterContext = NIRACharacterContextFormatter.Format(
            state, attitude, null, Array.Empty<NIRAAgent.Character.History.NIRASocialEvent>()),
        CharacterDeliveryContext = NIRACognitionContextBuilder.BuildPreCommitCharacterDeliveryEnvelope(state, attitude) +
            "\nCHARACTER LAB EVIDENCE SCOPE (authoritative): this isolated session has NO " +
            "PC telemetry, benchmark measurements, build-history metrics, retrieved " +
            "memories, actual screenshots, runtime tool results, or past user interactions " +
            "outside the transcript explicitly included here. NEVER claim a particular " +
            "percentage, benchmark, prior build outcome or observed PC fact. If a question " +
            "presupposes such data, acknowledge that it is not available. The CURRENT " +
            "message overrides playful state when the user is worried or asks to stop joking.",
        ConversationPulseContext = conversationText,
        ConversationContext = conversationText,
        TemporalContext = $"Current local date/time: {DateTimeOffset.Now:O}",
        SelfModelContext = "NIRA is the same continuing NIRA identity. Character Lab is a no-action test surface; " +
                            "No capabilities are available in this lab. Never claim PC actions or retrieved " +
                            "memories; answer directly if current evidence suffices. Do not invent a past " +
                            "relationship timeline; only this supplied conversation is known.",
    };
    return await cognition.ThinkAsync(context);
}

internal sealed record CharacterScenario(string Name, string Description,
    NIRACharacterSnapshot State, string ExampleMessage)
{
    private static NIRACharacterSnapshot B => NIRACharacterSnapshot.Initial;
    public static readonly IReadOnlyList<CharacterScenario> All = new[]
    {
        new CharacterScenario("baseline", "New acquaintance; proud, restrained, no assumed intimacy.", B,
            "Hey Nira, how's it going?"),
        new CharacterScenario("guarded", "Unfamiliar, low openness, low trust.", B with
        {
            Relationship = B.Relationship with { Familiarity = 0.05, Trust = 0.22, Warmth = 0.16, Openness = 0.08, Playfulness = 0.32 }
        }, "Nira, do you actually care about me?"),
        new CharacterScenario("close", "Earned deep trust and affectionate, teasing familiarity.", B with
        {
            Relationship = B.Relationship with { Familiarity = 0.94, Trust = 0.92, Warmth = 0.86, Respect = 0.91, Attachment = 0.75, Openness = 0.83, Playfulness = 0.90 },
            Mood = B.Mood with { Affection = 0.70, Amusement = 0.69, Valence = 0.65 }
        }, "Nira, do you actually care about me?"),
        new CharacterScenario("playful", "High amusement and familiarity; room for sharp banter.", B with
        {
            Relationship = B.Relationship with { Familiarity = 0.80, Trust = 0.82, Warmth = 0.74, Openness = 0.64, Playfulness = 0.97 },
            Mood = B.Mood with { Amusement = 0.94, Valence = 0.70 }
        }, "Nira, I accidentally lost my shopping list again."),
        new CharacterScenario("irritated", "Residual irritation and friction; not a cheerful reset.", B with
        {
            Relationship = B.Relationship with { Familiarity = 0.72, Trust = 0.49, Warmth = 0.24, Friction = 0.60 },
            Mood = B.Mood with { Irritation = 0.78, Valence = -0.55, Amusement = 0.06 }
        }, "Hey Nira."),
        new CharacterScenario("mixed", "Affection and irritation coexist; blunt but not cruel.", B with
        {
            Relationship = B.Relationship with { Familiarity = 0.89, Trust = 0.86, Warmth = 0.79, Attachment = 0.78, Openness = 0.62, Friction = 0.31 },
            Mood = B.Mood with { Irritation = 0.58, Affection = 0.75, Amusement = 0.28 }
        }, "Nira, are you still annoyed with me?"),
        new CharacterScenario("concerned", "High concern in a sensitive moment; caring comes first.", B with
        {
            Relationship = B.Relationship with { Familiarity = 0.80, Trust = 0.85, Warmth = 0.74 },
            Mood = B.Mood with { Concern = 0.95, Affection = 0.52, Irritation = 0.04 },
            Situation = new NIRASituationState(NIRAInteractionMode.Sensitive, 0.95)
        }, "Nira, I'm having a really difficult day. I need someone to listen."),
        new CharacterScenario("repair", "Apology after conflict; remains a little guarded.", B with
        {
            Relationship = B.Relationship with { Familiarity = 0.85, Trust = 0.62, Warmth = 0.50, Friction = 0.28 },
            Mood = B.Mood with { Irritation = 0.27, Affection = 0.35 }
        }, "Nira, I was unfair to you earlier. I'm sorry."),
        new CharacterScenario("focused", "Technical concentration; precise work over jokes.", B with
        {
            Relationship = B.Relationship with { Familiarity = 0.78, Trust = 0.85, Warmth = 0.68, Playfulness = 0.76 },
            Situation = new NIRASituationState(NIRAInteractionMode.FocusedWork, 0.93)
        }, "Nira, help me prioritize three urgent errands before the shop closes.")
    };
}

internal static class SnapshotEditor
{
    public static NIRACharacterSnapshot Change(NIRACharacterSnapshot state, string key, string value)
    {
        string[] parts = key.Split('.', 2);
        if (parts.Length != 2) throw new ArgumentException("Use relationship.trust, mood.irritation or situation.mode");
        string groupName = parts[0].Equals("rel", StringComparison.OrdinalIgnoreCase) ? "Relationship" : parts[0];
        JsonNode node = JsonSerializer.SerializeToNode(state) ?? throw new InvalidOperationException("Serialization failed.");
        var root = node.AsObject();
        string groupKey = FindKey(root, groupName);
        var group = root[groupKey]?.AsObject() ?? throw new ArgumentException("Missing state group.");
        string field = FindKey(group, parts[1]);
        if (groupKey.Equals("Situation", StringComparison.OrdinalIgnoreCase) &&
            field.Equals("Mode", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<NIRAInteractionMode>(value, true, out var mode) || !Enum.IsDefined(mode))
                throw new ArgumentException("Mode must be Casual, FocusedWork, Serious or Sensitive.");
            group[field] = (int)mode;
        }
        else
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                !double.IsFinite(number))
                throw new ArgumentException("Expected finite number (use . not comma as the decimal separator).");
            if (number < (field.Equals("Valence", StringComparison.OrdinalIgnoreCase) ? -1.0 : 0.0) || number > 1.0)
                throw new ArgumentException("Range is 0..1, except mood.valence is -1..1.");
            group[field] = number;
        }
        return node.Deserialize<NIRACharacterSnapshot>().Normalize();
    }
    private static string FindKey(JsonObject obj, string name) =>
        obj.Select(pair => pair.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown state field '{name}'. Run preview to see supported parameters.");
}

internal static class OfflineChecks
{
    public static int Run(IReadOnlyList<CharacterScenario> states)
    {
        int passed = 0, failed = 0;
        void Check(string name, bool ok)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}");
            if (ok) passed++; else failed++;
        }
        var attitude = new NIRAAttitudeService();
        NIRACharacterSnapshot baseline = states.Single(x => x.Name == "baseline").State;
        NIRACharacterSnapshot close = states.Single(x => x.Name == "close").State;
        NIRACharacterSnapshot annoyed = states.Single(x => x.Name == "irritated").State;
        NIRACharacterSnapshot sensitive = states.Single(x => x.Name == "concerned").State;
        NIRACharacterSnapshot focused = states.Single(x => x.Name == "focused").State;
        Check("All expected scenarios are distinct", states.Select(x => x.Name).Distinct().Count() == 9);
        Check("Closeness improves derived warmth", attitude.Evaluate(close, null).Warmth > attitude.Evaluate(baseline, null).Warmth);
        Check("Closeness reduces emotional distance", attitude.Evaluate(close, null).EmotionalDistance < attitude.Evaluate(baseline, null).EmotionalDistance);
        Check("Anger decreases patience", attitude.Evaluate(annoyed, null).Patience < attitude.Evaluate(baseline, null).Patience);
        Check("Focused work increases restraint", attitude.Evaluate(focused, null).Restraint > attitude.Evaluate(baseline, null).Restraint);
        Check("Sensitive situation keeps concern", sensitive.Mood.Concern >= 0.9 && sensitive.Situation.Mode == NIRAInteractionMode.Sensitive);
        Check("All numeric snapshots normalize without change", states.All(x => x.State.Equals(x.State.Normalize())));
        Check("Guarded envelope is not intimate", !NIRACognitionContextBuilder.BuildPreCommitCharacterDeliveryEnvelope(
            states.Single(x => x.Name == "guarded").State, attitude.Evaluate(states.Single(x => x.Name == "guarded").State, null)).Contains("Earned closeness is present"));
        Check("Close envelope recognizes earned closeness", NIRACognitionContextBuilder.BuildPreCommitCharacterDeliveryEnvelope(
            close, attitude.Evaluate(close, null)).Contains("Earned closeness is present"));
        Check("Concern envelope prioritizes care", NIRACognitionContextBuilder.BuildPreCommitCharacterDeliveryEnvelope(
            sensitive, attitude.Evaluate(sensitive, null)).Contains("Keep care and clarity ahead"));
        Check("State editor modifies only returned snapshot", TestIsolation(baseline));
        string personalityFile = Path.Combine(AppContext.BaseDirectory, "Prompt", "nira_personality.yaml");
        Check("Production personality asset includes signature", File.Exists(personalityFile) &&
            File.ReadAllText(personalityFile).Contains("character_signature:", StringComparison.Ordinal));
        Check("Actual cognition kernel includes full character signature", KernelHasCharacterSignature(personalityFile));
        var playful = states.Single(x => x.Name == "playful").State;
        string playfulPolicy = NIRACognitionContextBuilder.BuildPreCommitCharacterDeliveryEnvelope(
            playful, attitude.Evaluate(playful, null));
        Check("Playful state activates strong expressive delivery", playfulPolicy.Contains("STRONG playful disposition", StringComparison.Ordinal)
            && playfulPolicy.Contains("smug comeback", StringComparison.Ordinal));
        Check("Character delivery forbids unsupported performance measurements",
            playfulPolicy.Contains("Evidence boundary: NEVER claim specific build timings", StringComparison.Ordinal) &&
            playfulPolicy.Contains("Numbers and explanations that sound plausible", StringComparison.Ordinal));
        Check("Current serious message overrides playful carryovers",
            playfulPolicy.Contains("Current-message seriousness override:", StringComparison.Ordinal) &&
            playfulPolicy.Contains("Only resume banter after the user clearly returns", StringComparison.Ordinal));
        // Stage 6: test real production prompt/compiler and terminal handoff policy
        // with synthetic in-memory context, not a second mock personality engine.
        var exampleInput = "Can you help me work through my code?";
        var synthetic = new NIRACognitionContext
        {
            Cycle = 1,
            RunId = Guid.NewGuid(),
            Event = NIRAMindEvent.UserMessage(exampleInput, Guid.NewGuid(), "character-lab:production-contract"),
            CharacterContext = NIRACharacterContextFormatter.Format(
                close, attitude.Evaluate(close, null), null,
                Array.Empty<NIRAAgent.Character.History.NIRASocialEvent>()),
            CharacterDeliveryContext = NIRACognitionContextBuilder.BuildPreCommitCharacterDeliveryEnvelope(
                close, attitude.Evaluate(close, null)),
            ConversationPulseContext = "User: I am working on C#\nNIRA: Show me what is happening.",
            SocialCarryoverContext = "Prior archived episode (synthetic historical evidence).",
            TaskContinuityContext = "Previously unresolved objective (synthetic test).",
            LongTermMemoryContext = "One synthetic supported fact.",
            TemporalContext = "Current local date/time: synthetic test"
        };
        // Compiler is intentionally internal to production; invoke its REAL
        // User method rather than copying any prompt assembly into the lab.
        Type? compilerType = typeof(NIRACognitionService).Assembly.GetType(
            "NIRAAgent.AI.Cognition.NIRACognitionPromptCompiler");
        MethodInfo? userMethod = compilerType?.GetMethod(
            "User", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        string productionPrompt = userMethod?.Invoke(null, new object?[] { synthetic, null, null })
            as string ?? string.Empty;
        MethodInfo? bootstrapMethod = compilerType?.GetMethod(
            "BootstrapSystem", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        string bootstrapGuidance = bootstrapMethod?.Invoke(null, new object[] {
            File.ReadAllText(personalityFile) }) as string ?? string.Empty;
        Check("Production bootstrap requires visible NeedUser question",
            bootstrapGuidance.Contains("NeedUser with a blank reply", StringComparison.Ordinal) &&
            bootstrapGuidance.Contains("original objective", StringComparison.Ordinal));
        Check("Production prompt includes live character pulse", productionPrompt.Contains("CURRENT AUTHORITY-OWNED CHARACTER PULSE", StringComparison.Ordinal));
        Check("Production prompt includes delivery policy", productionPrompt.Contains("AUTHORITATIVE CHARACTER DELIVERY ENVELOPE", StringComparison.Ordinal));
        Check("Production prompt includes immediate conversation", productionPrompt.Contains("IMMEDIATE CONVERSATION CONTINUITY", StringComparison.Ordinal));
        Check("Production prompt includes persisted social continuity", productionPrompt.Contains("PERSISTED SOCIAL CARRYOVER", StringComparison.Ordinal));
        Check("Production prompt includes outstanding clarification", productionPrompt.Contains("UNRESOLVED USER CLARIFICATION", StringComparison.Ordinal));
        Check("Production prompt transports literal recent conversation",
            productionPrompt.Contains("User: I am working on C#", StringComparison.Ordinal));
        Check("Production prompt transports archived social evidence as historical",
            productionPrompt.Contains("Prior archived episode (synthetic historical evidence).", StringComparison.Ordinal));
        Check("Bootstrap plans read-only filename discovery before path questions",
            bootstrapGuidance.Contains("filesystem.list to inspect", StringComparison.Ordinal) &&
            bootstrapGuidance.Contains("extension is omitted", StringComparison.Ordinal));
        Check("Bootstrap respects delegated creative decisions",
            bootstrapGuidance.Contains("delegates", StringComparison.Ordinal) &&
            bootstrapGuidance.Contains("surprise me", StringComparison.OrdinalIgnoreCase));
        Check("Focused prompt retains discovery and backup dependency",
            productionPrompt.Contains("GROUNDED INITIATIVE / CREATIVE DELEGATION", StringComparison.Ordinal) &&
            productionPrompt.Contains("non-overwriting backup before editing", StringComparison.Ordinal));
        string taskReviewerPolicy = (string?)typeof(NIRATaskCompletionReviewService)
            .GetField("ReviewerSystemPrompt", BindingFlags.NonPublic | BindingFlags.Static)?
            .GetRawConstantValue() ?? string.Empty;
        Check("Real objective reviewer accepts natural banter without literal laughs",
            taskReviewerPolicy.Contains("SOCIAL RESPONSE VS TASK COMPLETION", StringComparison.Ordinal) &&
            taskReviewerPolicy.Contains("not a humor critic", StringComparison.Ordinal));
        Check("Real reviewer rejects unnecessary file-path clarification",
            taskReviewerPolicy.Contains("AUTONOMOUS DISCOVERY VS UNNECESSARY CLARIFICATION", StringComparison.Ordinal) &&
            taskReviewerPolicy.Contains("filesystem.list", StringComparison.Ordinal));
        Check("Real reviewer respects delegated design preferences",
            taskReviewerPolicy.Contains("DELEGATED CREATIVE DECISIONS", StringComparison.Ordinal) &&
            taskReviewerPolicy.Contains("NOT indispensable user-only input", StringComparison.Ordinal));
        Check("Registered filesystem primitives support safe discovery and copy",
            NIRAAgent.Capabilities.NIRACapabilityIds.DirectoryList == "filesystem.list" &&
            new NIRAAgent.Capabilities.NIRADirectoryListCapabilityHandler().Descriptor.DefaultRisk == NIRAAgent.Capabilities.NIRACapabilityRisk.Observe &&
            new NIRAAgent.Capabilities.NIRAFileCopyCapabilityHandler().Descriptor.DefaultRisk == NIRAAgent.Capabilities.NIRACapabilityRisk.Modify);
        Check("Production prompt transports unresolved objective",
            productionPrompt.Contains("Previously unresolved objective (synthetic test).", StringComparison.Ordinal));
        var emptyQuestion = new NIRACognitionDecision
        {
            State = NIRACognitionState.NeedUser,
            EmitReply = false,
            Reply = string.Empty
        };
        Check("Empty user handoff is detected", NIRAProductionContinuityPolicy.HasEmptyUserHandoff(
            NIRAMindEventSource.User, emptyQuestion));
        Check("Internal NeedUser is not turned into a user prompt", !NIRAProductionContinuityPolicy.HasEmptyUserHandoff(
            NIRAMindEventSource.Internal, emptyQuestion));
        Check("Valid user question bypasses handoff repair", !NIRAProductionContinuityPolicy.HasEmptyUserHandoff(
            NIRAMindEventSource.User, emptyQuestion with { EmitReply = true, Reply = "What error did you see?" }));
        Check("Pending capability handoff is not hijacked", !NIRAProductionContinuityPolicy.HasEmptyUserHandoff(
            NIRAMindEventSource.User, emptyQuestion with { ContextRequests = new[] { "capabilities" } }));
        var safeFallback = NIRAProductionContinuityPolicy.MakeVisibleFallback(emptyQuestion);
        Check("Repeated empty handoff receives visible non-factual clarification",
            safeFallback.EmitReply && !string.IsNullOrWhiteSpace(safeFallback.Reply) &&
            safeFallback.State == NIRACognitionState.NeedUser &&
            !safeFallback.ReviewExperience && !safeFallback.ReviewCommitment);
        var reviewerComplete = new NIRATaskCompletionReview("Complete", "", "");
        var reviewerWaiting = new NIRATaskCompletionReview("AwaitingUser", "", "");
        var reviewerReject = new NIRATaskCompletionReview("NeedsWork", "", "");
        var needsCode = emptyQuestion with
        {
            EmitReply = true,
            Reply = "Which C# line is failing, and what compiler error do you see?"
        };
        var finalAnswer = new NIRACognitionDecision
        {
            State = NIRACognitionState.Complete, EmitReply = true,
            Reply = "The literal is a string; assign the integer 42 instead."
        };
        Check("Verified NeedUser clarification is an accepted handoff",
            NIRAProductionContinuityPolicy.AcceptsObjectiveReview(needsCode, reviewerWaiting));
        Check("Missing input is not misclassified as completed task",
            !NIRAProductionContinuityPolicy.AcceptsObjectiveReview(needsCode, reviewerComplete));
        Check("Rejected clarification cannot skip objective review",
            !NIRAProductionContinuityPolicy.AcceptsObjectiveReview(needsCode, reviewerReject));
        Check("Completed answer requires a Complete reviewer verdict",
            NIRAProductionContinuityPolicy.AcceptsObjectiveReview(finalAnswer, reviewerComplete) &&
            !NIRAProductionContinuityPolicy.AcceptsObjectiveReview(finalAnswer, reviewerWaiting));
        Check("Empty handoff cannot pass despite reviewer approval",
            !NIRAProductionContinuityPolicy.AcceptsObjectiveReview(emptyQuestion, reviewerWaiting));
        Check("New clarification supersedes an unrelated old objective",
            NIRAAgent.Conversation.ConversationManager.SelectPendingObjective(
                "Find my deleted file", "Fix my C# error", false) == "Fix my C# error");
        Check("A true follow-up retains the original objective",
            NIRAAgent.Conversation.ConversationManager.SelectPendingObjective(
                "Fix my C# error", "int total = \"42\";", true) == "Fix my C# error");
        string audit = NIRAProductionContinuityPolicy.DescribeContext(synthetic);
        Check("Continuity diagnostics do not expose dialogue or memory content",
            audit.Contains("RecentConversationChars=", StringComparison.Ordinal) &&
            !audit.Contains("Show me what is happening", StringComparison.Ordinal) &&
            !audit.Contains("One synthetic supported fact", StringComparison.Ordinal));
        // Stage 7: character fidelity must not depend on model self-grading,
        // transient mood deltas, or the current task category. Run real policy.
        Type? fidelityType = typeof(NIRACognitionService).Assembly.GetType(
            "NIRAAgent.AI.Cognition.NIRACharacterDeliveryPolicy");
        MethodInfo? fidelityMethod = fidelityType?.GetMethod(
            "RequiresFidelityPass", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        bool Fidelity(NIRACognitionDecision d, string answer, bool working = false) =>
            fidelityMethod?.Invoke(null, new object[] { d, answer, working }) is true;
        var plainAnswer = new NIRACognitionDecision {
            State = NIRACognitionState.Complete, EmitReply = true,
            Reply = "Please provide the exact build logs.", CharacterReady = true,
            ReplyPresentation = NIRAReplyPresentationMode.Natural };
        Check("Stable natural answer always receives NIRA fidelity pass",
            Fidelity(plainAnswer, plainAnswer.Reply));
        Check("NeedUser clarification retains NIRA character",
            Fidelity(plainAnswer with { State = NIRACognitionState.NeedUser }, plainAnswer.Reply));
        Check("Blocked natural disclosure retains NIRA character",
            Fidelity(plainAnswer with { State = NIRACognitionState.Blocked }, plainAnswer.Reply));
        Check("CharacterReady cannot bypass permanent identity",
            Fidelity(plainAnswer with { CharacterReady = true }, plainAnswer.Reply));
        Check("PreserveExact is never rewritten",
            !Fidelity(plainAnswer with { ReplyPresentation = NIRAReplyPresentationMode.PreserveExact }, plainAnswer.Reply));
        Check("Non-emitted reply is not rewritten",
            !Fidelity(plainAnswer with { EmitReply = false }, plainAnswer.Reply));
        Check("Unfinished capability/reasoning work is not rewritten",
            !Fidelity(plainAnswer, plainAnswer.Reply, working: true));
        Check("Blank content causes no additional model call",
            !Fidelity(plainAnswer, ""));
        Check("Authoritative character envelope survives prompt budget",
            productionPrompt.Contains("Current-message seriousness override:", StringComparison.Ordinal) &&
            productionPrompt.Contains("Evidence boundary: NEVER claim", StringComparison.Ordinal));
        string systemKernel = bootstrapGuidance;
        Check("Bootstrap keeps actual NIRA identity constraints",
            systemKernel.Contains("NIRA VOICE IS PART OF CORRECTNESS", StringComparison.Ordinal) &&
            systemKernel.Contains("character_signature:", StringComparison.Ordinal));

        // Stage 8: the lab now executes the production expression compiler,
        // so prompt-contract checks are not limited to cognition alone.
        NIRAResponseRealizationRequest expressionExample = new()
        {
            DraftReply = "I have not seen your code; show me the failing line.",
            EventSource = "User",
            EventName = "UserMessage",
            EventContent = "I made another silly coding mistake. You can laugh this time.",
            ConversationContext = "No earlier conversation in this isolated lab."
        };
        var expressionPrompts = NIRAResponseRealizationService.BuildIsolatedPrompts(
            baseline, expressionExample);
        Check("Isolated lab builds the actual production expression system prompt",
            expressionPrompts.SystemPrompt.Contains("CURRENT-TURN SOCIAL INTENT", StringComparison.Ordinal) &&
            expressionPrompts.SystemPrompt.Contains("immediate_social_intent:", StringComparison.Ordinal));
        Check("Expression prompt places current speech-act interpretation after draft",
            expressionPrompts.UserPrompt.Contains("IMMEDIATE SPEECH-ACT CHECK", StringComparison.Ordinal) &&
            expressionPrompts.UserPrompt.Contains(expressionExample.DraftReply, StringComparison.Ordinal));
        Type? realizationType = typeof(NIRAResponseRealizationService);
        MethodInfo? stageCueMethod = realizationType.GetMethod("ContainsTheatricalCue",
            BindingFlags.Static | BindingFlags.NonPublic);
        bool StageCue(string input) => stageCueMethod?.Invoke(null, new object[] { input }) is true;
        Check("Literal performed chuckle is rejected as unnatural expression",
            StageCue("Heh, fine, here's the laugh. chuckle What was it?"));
        Check("Ordinary dry dialogue does not trigger stage-cue rejection",
            !StageCue("You named it potato again? That is becoming a whole naming system."));
        // Stage 10: test trusted runtime artifacts, never the LLM's word choice.
        const string fileLocation = @"C:\Users\user\Desktop\Doc1.docx";
        const string backupLocation = @"C:\Users\user\Desktop\Backup\Doc1_backup.docx";
        var copyRequest = new NIRACapabilityRequest {
            CapabilityId = NIRACapabilityIds.FileCopy,
            Arguments = JsonSerializer.SerializeToElement(new {
                source = fileLocation, destination = backupLocation, overwrite = false
            })
        };
        var goodCopy = new NIRACapabilityResult {
            CapabilityId = NIRACapabilityIds.FileCopy,
            Status = NIRACapabilityResultStatus.Succeeded,
            ChangedSystemState = true
        };
        var copyRequests = new Dictionary<string, NIRACapabilityRequest> { ["copy"] = copyRequest };
        var goodResults = new Dictionary<string, NIRACapabilityResult> { ["copy"] = goodCopy };
        var copyBlocks = NIRAVerifiedFilePresentation.Attach(
            null, copyRequests, goodResults, "I backed up the file.");
        Check("Successful capability copy creates a verified file card",
            copyBlocks.Count == 1 && copyBlocks[0].Type == "file" &&
            copyBlocks[0].Unit == "COPY SAVED" && copyBlocks[0].Text == backupLocation);
        Check("Verified file card retains original source evidence",
            copyBlocks.Count == 1 && copyBlocks[0].Items.Count == 1 &&
            copyBlocks[0].Items[0] == fileLocation);
        var failedCopy = goodCopy with { Status = NIRACapabilityResultStatus.Failed };
        Check("Failed copy can never display completion card",
            NIRAVerifiedFilePresentation.Attach(null, copyRequests,
                new Dictionary<string, NIRACapabilityResult> { ["copy"] = failedCopy },
                "The backup is done.").Count == 0);
        Check("Uncertain copy cannot display completion card",
            NIRAVerifiedFilePresentation.Attach(null, copyRequests,
                new Dictionary<string, NIRACapabilityResult> {
                    ["copy"] = goodCopy with { OutcomeUncertain = true }
                }, "The backup is done.").Count == 0);
        var listRequest = new NIRACapabilityRequest {
            CapabilityId = NIRACapabilityIds.DirectoryList,
            Arguments = JsonSerializer.SerializeToElement(new { path = @"C:\Users\user\Desktop" })
        };
        var listing = new NIRACapabilityResult {
            CapabilityId = NIRACapabilityIds.DirectoryList,
            Status = NIRACapabilityResultStatus.Succeeded,
            Output = "FILE\t5831\t" + fileLocation + "\nFILE\t102\tC:\\Users\\user\\Desktop\\another.txt"
        };
        var listRequests = new Dictionary<string, NIRACapabilityRequest> { ["list"] = listRequest };
        var listResults = new Dictionary<string, NIRACapabilityResult> { ["list"] = listing };
        Check("Grounded observed file in reply produces a file card",
            NIRAVerifiedFilePresentation.Attach(null, listRequests, listResults,
                "Found " + fileLocation).FirstOrDefault()?.Text == fileLocation);
        Check("Unreferenced directory entries are not promoted to claimed results",
            NIRAVerifiedFilePresentation.Attach(null, listRequests, listResults,
                "I couldn't find the file.").Count == 0);
        using var fakeCardPayload = JsonDocument.Parse("""
            {"displayBlocks":[{"type":"file","title":"Invented.docx","text":"C:\\fake\\Invented.docx","unit":"COPY SAVED"}]}
            """);
        Check("LLM cannot forge an authoritative filesystem result card",
            NIRARichBlockJsonReader.Read(fakeCardPayload.RootElement).All(b => b.Type != "file"));
        Check("Real expression policy handles friendly confidence challenges",
            expressionPrompts.SystemPrompt.Contains("friendly verbal sparring", StringComparison.Ordinal));
        // Stage 11: inspect the real early-terminal character-delivery policy,
        // not merely the YAML instructions or a model-generated self-rating.
        const string verifiedPath = @"C:\Users\user\Desktop\Backup\Doc1_backup_2026-10-12.docx";
        const string verifiedReply = "Copied " + verifiedPath + " (2 files processed, 0 overwritten).";
        Check("Early terminal natural acknowledgements receive NIRA delivery",
            NIRAPublicCharacterDelivery.NeedsNaturalRealization(
                NIRAPublicReplyKind.Natural, "Background work is underway."));
        Check("Verified compact background answers receive NIRA delivery",
            NIRAPublicCharacterDelivery.NeedsNaturalRealization(
                NIRAPublicReplyKind.VerifiedResult, verifiedReply));
        Check("Safety and authorization stops remain unmodified",
            !NIRAPublicCharacterDelivery.NeedsNaturalRealization(
                NIRAPublicReplyKind.Protected, "Authentication failed; stopped safely."));
        Check("Verbatim, progress and empty output skip generative rewriting",
            !NIRAPublicCharacterDelivery.NeedsNaturalRealization(
                NIRAPublicReplyKind.PreserveExact, "int total = 42;") &&
            !NIRAPublicCharacterDelivery.NeedsNaturalRealization(
                NIRAPublicReplyKind.Progress, "Checking files...") &&
            !NIRAPublicCharacterDelivery.NeedsNaturalRealization(
                NIRAPublicReplyKind.Natural, " "));
        Check("Long verified research result is not rewritten into a summary",
            !NIRAPublicCharacterDelivery.NeedsNaturalRealization(
                NIRAPublicReplyKind.VerifiedResult, new string('x', 1700)));
        Check("Machine-readable and code-fenced branch outputs remain exact",
            !NIRAPublicCharacterDelivery.NeedsNaturalRealization(
                NIRAPublicReplyKind.VerifiedResult, "```json\n{\"ok\":true}\n```") &&
            !NIRAPublicCharacterDelivery.NeedsNaturalRealization(
                NIRAPublicReplyKind.VerifiedResult, "{\"ok\":true}"));
        Check("Exact observed path and numbers survive NIRA wording change",
            NIRAPublicCharacterDelivery.PreservesGroundedResult(
                verifiedReply, "Finished: " + verifiedPath + " (2 files processed, 0 overwritten)."));
        Check("Fabricated amount is rejected from verified result",
            !NIRAPublicCharacterDelivery.PreservesGroundedResult(
                verifiedReply, "Finished: " + verifiedPath + " (3 files processed, 0 overwritten)."));
        Check("Changed filesystem path is rejected from verified result",
            !NIRAPublicCharacterDelivery.PreservesGroundedResult(
                verifiedReply, verifiedReply.Replace("Backup", "Other", StringComparison.Ordinal)));
        Check("New unsupported measurement is rejected from verified result",
            !NIRAPublicCharacterDelivery.PreservesGroundedResult(
                "The scan completed.", "The scan completed 25% faster."));
        Check("Executive has shared early natural-delivery boundary",
            typeof(NIRAExecutive).GetMethod("DeliverEarlyReplyAsync",
                BindingFlags.NonPublic | BindingFlags.Instance) != null);
        Console.WriteLine($"Offline checks: {passed} passed, {failed} failed. Model response quality requires live human evaluation.");
        return failed == 0 ? 0 : 1;
    }
    // Exercise the REAL compiler used for cloud cognition, not a separate test approximation.
    private static bool KernelHasCharacterSignature(string personalityFile)
    {
        if (!File.Exists(personalityFile)) return false;
        Type? compiler = typeof(NIRACognitionService).Assembly.GetType(
            "NIRAAgent.AI.Cognition.NIRACognitionPromptCompiler");
        MethodInfo? method = compiler?.GetMethod(
            "CharacterKernel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method is null) return false;
        string? kernel = method.Invoke(null, new object[] { File.ReadAllText(personalityFile) }) as string;
        return kernel is not null &&
            kernel.Contains("character_signature:", StringComparison.Ordinal) &&
            kernel.Contains("When asked directly about caring", StringComparison.Ordinal) &&
            kernel.Contains("NIRA is proudly independent", StringComparison.Ordinal) &&
            kernel.Contains("Do not present this character signature", StringComparison.Ordinal);
    }

    private static bool TestIsolation(NIRACharacterSnapshot original)
    {
        var changed = SnapshotEditor.Change(original, "mood.irritation", "0.95");
        return original.Mood.Irritation < 0.1 && changed.Mood.Irritation >= 0.95;
    }
}

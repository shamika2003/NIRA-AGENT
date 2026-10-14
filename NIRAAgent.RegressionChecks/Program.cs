using System.Text.Json;
using System.Reflection;
using NIRAAgent.Capabilities;
using NIRAAgent.Browser;
using NIRAAgent.Embodiment;
using NIRAAgent.Presentation;
using NIRAAgent.AI.Ollama;
using NIRAAgent.AI.Cognition;
using NIRAAgent.Mind;

// Run with: dotnet run --project NIRAAgent.RegressionChecks
// These are offline contract checks, not simulated live model/Bridge tests.
int passed = 0;
int failed = 0;
Console.WriteLine($"[DeepRouting] REGRESSION | Revision={NIRAModelSelection.RoutingRevision} | " +
    $"Assembly={typeof(NIRAModelSelection).Assembly.Location}");
Console.WriteLine("[ConcurrentMind] REGRESSION | Revision=NIRA-CONCURRENT-20261014-S4-ADAPTIVE");

Check("Capability identity ignores transient request IDs and explanations", () =>
{
    var first = Request("elvara.tradeai.read", """{"resource":"signals","symbol":"EURUSD"}""");
    var second = Request("ELVARA.TRADEAI.READ", """{"symbol":"EURUSD","resource":"signals"}""")
        with { Reason = "Different wording", RequestId = Guid.NewGuid() };
    Equal(first.BuildSignature(), second.BuildSignature());
});

Check("Different observed targets are different work", () =>
{
    var eurusd = Request("elvara.tradeai.read", """{"resource":"signals","symbol":"EURUSD"}""");
    var gbpusd = Request("elvara.tradeai.read", """{"resource":"signals","symbol":"GBPUSD"}""");
    NotEqual(eurusd.BuildSignature(), gbpusd.BuildSignature());
});

Check("Different resources are different work", () =>
{
    var positions = Request("elvara.tradeai.read", """{"resource":"positions"}""");
    var signals = Request("elvara.tradeai.read", """{"resource":"signals"}""");
    NotEqual(positions.BuildSignature(), signals.BuildSignature());
});

Check("Missing capability ID cannot be normalized", () =>
{
    Throws<InvalidOperationException>(() =>
        Request("", "{}").Normalize());
});

Check("Capability arguments must be a JSON object", () =>
{
    Throws<InvalidOperationException>(() =>
        Request("system.storage.list", "[1,2]").Normalize());
});

Check("Known style IDs have unique catalog entries and valid preview mappings", () =>
{
    var styles = NIRABlobStyleCatalog.All;
    Equal(Enum.GetValues<NIRABlobStyleId>().Length, styles.Count);
    Equal(styles.Count, styles.Select(style => style.Id).Distinct().Count());
    foreach (var style in styles)
    {
        Equal(style.Id, NIRABlobStyleCatalog.Get(style.Id).Id);
        if (!Enum.IsDefined(style.PreviewPreset))
            throw new Exception($"Undefined preview preset for {style.Id}");
    }
});

Check("All app guidance fits NIRA's current 3000-character loader budget", () =>
{
    string? promptPath = LocatePromptFolder();
    if (promptPath is null)
        throw new Exception("Could not locate NIRAAgent/Prompt directory");
    var files = Directory.GetFiles(promptPath, "elvara_*.yaml");
    if (files.Length == 0)
        throw new Exception("No embedded-app prompts were found to validate");
    // Read the actual catalog constant, so the check stays in sync if the
    // loader budget changes. The runtime's non-authorizing loader remains private.
    Type catalog = typeof(NIRACapabilityRequest).Assembly.GetType(
        "NIRAAgent.AI.Cognition.NIRAScopedApplicationPromptCatalog", throwOnError: true)!;
    FieldInfo limitField = catalog.GetField(
        "MaximumPromptCharacters", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new Exception("Scoped prompt limit field is missing");
    int limit = (int)(limitField.GetRawConstantValue()
        ?? throw new Exception("Scoped prompt limit has no value"));
    foreach (var file in files)
    {
        int length = File.ReadAllText(file).Trim().Length;
        if (length > limit)
            throw new Exception($"{Path.GetFileName(file)} has {length} characters; runtime limit is {limit}");
    }
});

Check("Rich screen blocks are included in objective review", () =>
{
    string review = NIRAPresentationPolicy.ForObjectiveReview(
        "Here are the processes.", new NIRARichBlock[]
        {
            new() { Type = "table", Title = "Processes", Columns = new[] { "Name", "Memory" },
                Rows = new[] { (IReadOnlyList<string>)new[] { "Notepad", "22.50 MiB" } } }
        });
    if (!review.Contains("Notepad", StringComparison.Ordinal) ||
        !review.Contains("22.50 MiB", StringComparison.Ordinal))
        throw new Exception("Visible table missing from reviewer input");
});

Check("Simple chat review does not fabricate rich content", () =>
{
    Equal("Hello.", NIRAPresentationPolicy.ForObjectiveReview("Hello.", Array.Empty<NIRARichBlock>()));
});

Check("Markdown from screen is never spoken as markup", () =>
{
    Equal("Storage C: 21.25 GB free.", NIRAPresentationPolicy.PlainSpeechFallback(
        "## Storage\n- **C:** `21.25 GB` free."));
});

Check("Process memory sort accepts semantically equivalent aliases", () =>
{
    var handler = new NIRAProcessListCapabilityHandler();
    NIRACapabilityRequest request = Request("process.list", """{"sortBy":"Memory","maxResults":3}""");
    NIRACapabilityHandlerResult result = handler.ExecuteAsync(request).GetAwaiter().GetResult();
    if (!result.Summary.Contains("SortBy=WorkingSet", StringComparison.Ordinal) ||
        !result.Output.Contains("WorkingSetMiB", StringComparison.Ordinal))
        throw new Exception("Memory alias not resolved to working-set sort");
    var descending = Request("process.list", """{"sortBy":"memory_desc","maxResults":3}""");
    if (!handler.ExecuteAsync(descending).GetAwaiter().GetResult().Summary.Contains(
            "SortBy=WorkingSet", StringComparison.Ordinal))
        throw new Exception("Descending memory alias not normalized");
});

Check("Desktop discovery uses current Windows known folder before drives", () =>
{
    Type handler = typeof(NIRAFileLocationCapabilityHandler);
    var method = handler.GetMethod("ResolveWholePcSearchRoots", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new Exception("Known-folder root resolver missing");
    string[] roots = (string[])method.Invoke(null, null)!;
    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop) &&
        (roots.Length == 0 || !string.Equals(Path.GetFullPath(desktop), roots[0], StringComparison.OrdinalIgnoreCase)))
        throw new Exception("Windows Desktop was not first in the search roots");
});

Check("Browser heading evidence distinguishes observed absence from present H1", () =>
{
    Type formatter = typeof(NIRABrowserInspection).Assembly.GetType(
        "NIRAAgent.Capabilities.NIRABrowserCapabilityFormatting", throwOnError: true)!;
    MethodInfo method = formatter.GetMethod("Inspection", BindingFlags.Static | BindingFlags.Public)
        ?? throw new Exception("Browser inspection formatter unavailable");

    string absent = (string)method.Invoke(null, new object[]
    {
        new NIRABrowserInspection { Title = "Observed page title", Headings = Array.Empty<string>() }
    })!;
    if (!absent.Contains("DOMHeadingCount=0", StringComparison.Ordinal) ||
        !absent.Contains("DOMHeadings=[]", StringComparison.Ordinal) ||
        !absent.Contains("Title=Observed page title", StringComparison.Ordinal))
        throw new Exception("Missing explicit negative heading observation");

    string present = (string)method.Invoke(null, new object[]
    {
        new NIRABrowserInspection { Title = "Different title", Headings = new[] { "H1: Real heading" } }
    })!;
    if (!present.Contains("DOMHeadingCount=1", StringComparison.Ordinal) ||
        !present.Contains("DOMHeadings=[H1: Real heading]", StringComparison.Ordinal))
        throw new Exception("Grounded heading observation was lost");
});

Check("Runtime model assembly reports the synchronized routing revision", () =>
{
    Equal("NIRA-DEEP-EXEC-20261014-R5", NIRAModelSelection.RoutingRevision);
});

Check("NIRA's primary and deep models have distinct default roles", () =>
{
    Equal("gemma4:31b-cloud", NIRAModelSelection.PrimaryDefault);
    Equal("nemotron-3-ultra:cloud", NIRAModelSelection.DeepDefault);
});

Check("Delegated runs use Ultra without a new classifier", () =>
{
    Equal(NIRAModelSelection.Primary, NIRAModelSelection.ForCognition(false));
    Equal(NIRAModelSelection.Deep, NIRAModelSelection.ForCognition(true));
});

Check("Deep specialist can request multiple ordinary registered tools", () =>
{
    const string raw = """{"state":"Continue","emitReply":false,"capabilityRequests":[{"capabilityId":"browser.inspect","arguments":{}},{"capabilityId":"filesystem.list","arguments":{}}],"deepReasoningRequest":{"focus":"more reasoning"}}""";
    if (!NIRAModelSelection.TryRecoverDeepDecision(raw, out string recovered))
        throw new Exception("Lost multi-tool Executive handoff");
    using JsonDocument json = JsonDocument.Parse(recovered);
    JsonElement requests = json.RootElement.GetProperty("capabilityRequests");
    Equal(2, requests.GetArrayLength());
    if (json.RootElement.TryGetProperty("deepReasoningRequest", out _))
        throw new Exception("Nested delegation survived sanitization");
});

Check("Analysis-only output cannot terminate an Ultra Executive run", () =>
{
    if (NIRAModelSelection.IsUsableDeepDecision(
        """{"analysis":"External operations require idempotency."}"""))
        throw new Exception("Analysis-only specialist answer masqueraded as a decision");
});

Check("Strict JSON guard rejects malformed Ultra response without guessing actions", () =>
{
    const string malformed = """{"state":"Continue","capabilityRequests":[{"capabilityId":"filesystem.read"}],{bad}}""";
    if (NIRAModelSelection.HasValidJsonObjectSyntax(malformed) ||
        NIRAModelSelection.IsUsableDeepDecision(malformed))
        throw new Exception("Malformed specialist decision must not reach Executive");
});

Check("Valid decisions bypass the JSON repair route", () =>
{
    const string ordinary = """{"state":"Complete","emitReply":true,"reply":"Done."}""";
    string fenced = "```json\n" + ordinary + "\n```";
    if (!NIRAModelSelection.HasValidJsonObjectSyntax(ordinary) ||
        !NIRAModelSelection.HasValidJsonObjectSyntax(fenced))
        throw new Exception("Valid JSON would cause an extra model call");
});

Check("Semantic contract errors are not confused with JSON syntax", () =>
{
    const string incomplete = """{"analysis":"No tools used."}""";
    if (!NIRAModelSelection.HasValidJsonObjectSyntax(incomplete) ||
        NIRAModelSelection.IsUsableDeepDecision(incomplete))
        throw new Exception("Analysis-only JSON must not be Executable");
});

Check("Deep tool round and terminal result pass structural preflight", () =>
{
    const string tools = """{"state":"Continue","emitReply":false,"capabilityRequests":[{"capabilityId":"browser.inspect","arguments":{}}]}""";
    const string done = """{"state":"Complete","emitReply":true,"reply":"No exactly-once guarantee exists without external idempotency."}""";
    if (!NIRAModelSelection.IsUsableDeepDecision(tools) ||
        !NIRAModelSelection.IsUsableDeepDecision(done))
        throw new Exception("Valid specialist decision was rejected");
});

Check("Delegation-only Ultra output cannot masquerade as tool work", () =>
{
    if (NIRAModelSelection.IsUsableDeepDecision(
        """{"deepReasoningRequest":{"focus":"another model"}}"""))
        throw new Exception("Nested delegation was treated as completed work");
});

Check("Normal one-call cognition needs no deep delegation", () =>
{
    bool delegated = NIRAModelSelection.TryReadDelegation(
        """{"state":"Complete","emitReply":true,"reply":"Hello.","deepReasoningRequest":null}""",
        out string focus);
    if (delegated || focus.Length != 0)
        throw new Exception("Ordinary cognition incorrectly requested an extra model call");
});

Check("Structured deep request is recognized without extra classifier", () =>
{
    bool delegated = NIRAModelSelection.TryReadDelegation(
        """{"state":"Continue","emitReply":false,"deepReasoningRequest":{"focus":"Analyze conflicting build evidence"}}""",
        out string focus);
    if (!delegated || focus != "Analyze conflicting build evidence")
        throw new Exception("Grounded deep request not recognized");
});

Check("Malformed or unbounded deep requests do not trigger a call", () =>
{
    string[] bad = {
        "not json",
        """{"deepReasoningRequest":{"focus":""}}""",
        """{"deepReasoningRequest":{"focus":55}}""",
        """{"deepReasoningRequest":true}""",
        "{\"deepReasoningRequest\":{\"focus\":\"" + new string('x', 601) + "\"}}"
    };
    if (bad.Any(x => NIRAModelSelection.TryReadDelegation(x, out _)))
        throw new Exception("Invalid request escalated");
});

Check("Deep specialist's complete answer survives redundant delegation field", () =>
{
    const string original = """{"state":"Complete","emitReply":true,"reply":"Exactly-once execution cannot be guaranteed without idempotency or reconciliation.","deepReasoningRequest":{"focus":"Please reason even more"}}""";
    if (!NIRAModelSelection.TryRecoverDeepDecision(original, out string recovered))
        throw new Exception("Substantive Ultra answer was discarded");
    using JsonDocument decision = JsonDocument.Parse(recovered);
    if (decision.RootElement.TryGetProperty("deepReasoningRequest", out _) ||
        decision.RootElement.GetProperty("state").GetString() != "Complete" ||
        !decision.RootElement.GetProperty("reply").GetString()!.Contains("Exactly-once"))
        throw new Exception("Recovery changed the deep decision or retained nested routing");
});

Check("Empty specialist result cannot masquerade as a completed answer", () =>
{
    const string original = """{"state":"Continue","emitReply":false,"reply":"","deepReasoningRequest":{"focus":"Ask another model"}}""";
    if (NIRAModelSelection.TryRecoverDeepDecision(original, out _))
        throw new Exception("Delegation-only response was treated as useful work");
});

Check("Deep specialist's ordinary capability proposal survives redundant field", () =>
{
    const string original = """{"state":"Continue","emitReply":false,"capabilityRequests":[{"capabilityId":"system.storage.list"}],"deepReasoningRequest":{"focus":"Need further thinking"}}""";
    if (!NIRAModelSelection.TryRecoverDeepDecision(original, out string recovered))
        throw new Exception("Valid capability decision was discarded");
    using JsonDocument decision = JsonDocument.Parse(recovered);
    if (decision.RootElement.TryGetProperty("deepReasoningRequest", out _) ||
        decision.RootElement.GetProperty("capabilityRequests").GetArrayLength() != 1)
        throw new Exception("Recovery lost required capability proposal");
});

Check("Specialist analysis-only JSON is accepted without delegation", () =>
{
    string json = JsonSerializer.Serialize(new { analysis =
        "An external payment may have been performed before the worker crashed. " +
        "Automatic retries cannot guarantee exactly-once effects without external support." });
    if (!NIRAModelSelection.TryReadDeepAnalysis(json, out string analysis) ||
        !analysis.Contains("exactly-once", StringComparison.Ordinal))
        throw new Exception("Specialist analysis was not accepted");
    if (NIRAModelSelection.TryReadDelegation(json, out _))
        throw new Exception("Analysis-only response triggered recursive delegation");
});

Check("Specialist output with only a second delegation is rejected", () =>
{
    string json = """{"deepReasoningRequest":{"focus":"delegate again"}}""";
    if (NIRAModelSelection.TryReadDeepAnalysis(json, out _))
        throw new Exception("Recursive request was mistaken for analysis");
});

Check("Empty and oversized specialist analyses are rejected locally", () =>
{
    string[] invalid = {
        """{"analysis":""}""",
        """{"analysis":42}""",
        JsonSerializer.Serialize(new { analysis = new string('x', 16001) })
    };
    if (invalid.Any(v => NIRAModelSelection.TryReadDeepAnalysis(v, out _)))
        throw new Exception("Non-substantive specialist analysis was accepted");
});

Check("Source-read evidence retains old and new line ranges without duplicate reads", () =>
{
    string original = "AUTHORITATIVE CAPABILITY RESULT\nCapabilityId: filesystem.read\nStatus: Succeeded\n" +
        "Summary: Read 2 line(s) from 'C:\\src\\a.cs' starting at line 1. Length=300 bytes | Truncated=True.\n" +
        "Output:\nL1: class A {}\nL2: // start\n";
    string recent = "AUTHORITATIVE CAPABILITY RESULT\nCapabilityId: filesystem.read\nStatus: Succeeded\n" +
        "Summary: Read 2 line(s) from 'C:\\src\\a.cs' starting at line 100. Length=300 bytes | Truncated=False.\n" +
        "Output:\nL100: void Run() {}\nL101: // end\n";
    Type compiler = typeof(NIRAModelSelection).Assembly.GetType(
        "NIRAAgent.AI.Cognition.NIRACognitionPromptCompiler", throwOnError: true)!;
    MethodInfo method = compiler.GetMethod("SourceReadRecords", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new Exception("Source-read evidence compiler missing");
    // Match Windows Executive output (CRLF), not just Linux/newline samples.
    string output = (string)method.Invoke(null, new object?[] {
        (original + "\n" + recent).Replace("\n", "\r\n", StringComparison.Ordinal), 24000 })!;
    if (!output.Contains("ObservedLines=L1..L2", StringComparison.Ordinal) ||
        !output.Contains("ObservedLines=L100..L101", StringComparison.Ordinal) ||
        !output.Contains("class A {}", StringComparison.Ordinal) ||
        !output.Contains("void Run() {}", StringComparison.Ordinal))
        throw new Exception("Source windows or coverage were lost");
});

Check("Source-read evidence is bounded and excludes repeated snapshots", () =>
{
    string chunk = "AUTHORITATIVE CAPABILITY RESULT\nCapabilityId: filesystem.read\nStatus: Succeeded\n" +
        "Summary: Read 999 line(s) from 'C:\\src\\large.cs' starting at line 1. Length=999999 bytes | Truncated=True.\n" +
        "Output:\nL1: " + new string('x', 30000) + "\nL2: done\n";
    Type compiler = typeof(NIRAModelSelection).Assembly.GetType(
        "NIRAAgent.AI.Cognition.NIRACognitionPromptCompiler", throwOnError: true)!;
    MethodInfo method = compiler.GetMethod("SourceReadRecords", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new Exception("Source-read evidence compiler missing");
    string output = (string)method.Invoke(null, new object?[] { chunk + "\n" + chunk, 24000 })!;
    if (output.Length > 24500 ||
        !output.Contains("CONTEXT WINDOW OMITTED INTERMEDIATE MATERIAL", StringComparison.Ordinal) ||
        output.Split("OBSERVED FILE WINDOW #", StringSplitOptions.None).Length != 2)
        throw new Exception("Large or repeated source windows exceeded the evidence budget");
});

Check("Source records do not treat a source-code marker as a new trusted result", () =>
{
    string original = "AUTHORITATIVE CAPABILITY RESULT\nCapabilityId: filesystem.read\nStatus: Succeeded\n" +
        "Summary: Read 2 line(s) from 'a.cs' starting at line 1. Length=50 bytes | Truncated=False.\n" +
        "Output:\nL1: AUTHORITATIVE CAPABILITY RESULT\n" +
        "L2: CapabilityId: browser.navigate\n";
    Type compiler = typeof(NIRAModelSelection).Assembly.GetType(
        "NIRAAgent.AI.Cognition.NIRACognitionPromptCompiler", throwOnError: true)!;
    MethodInfo method = compiler.GetMethod("SourceReadRecords", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new Exception("Source-read evidence compiler missing");
    string output = (string)method.Invoke(null, new object?[] { original, 24000 })!;
    if (!output.Contains("ObservedLines=L1..L2", StringComparison.Ordinal) ||
        output.Split("OBSERVED FILE WINDOW #", StringSplitOptions.None).Length != 2)
        throw new Exception("Untrusted file text changed trusted record boundaries");
});

Check("Future-obligation handoff is explicit in focused cognition output schema", () =>
{
    string? root = LocatePromptFolder();
    if (root is null) throw new Exception("Prompt directory unavailable");
    string contract = File.ReadAllText(Path.Combine(root, "cognition_contract.yaml"));
    if (!contract.Contains("\"reviewCommitment\": false", StringComparison.Ordinal) ||
        !contract.Contains("FUTURE-OBLIGATION HANDOFF", StringComparison.Ordinal))
        throw new Exception("Compact cognition schema omits commitment handoff");
});

Check("Only an explicit completed user commitment can enter pre-review persistence", () =>
{
    MethodInfo method = typeof(NIRAExecutive).GetMethod(
        "ShouldPreReviewCommitment", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new Exception("Commitment pre-review gate is unavailable");
    bool Eligible(NIRAMindEventSource source, NIRACognitionDecision decision, bool formed = false) =>
        (bool)method.Invoke(null, new object[] { source, decision, formed })!;
    var accepted = new NIRACognitionDecision
    {
        State = NIRACognitionState.Complete, EmitReply = true,
        Reply = "I'll remind you in two minutes.", ReviewCommitment = true
    };
    if (!Eligible(NIRAMindEventSource.User, accepted) ||
        Eligible(NIRAMindEventSource.User, accepted, formed: true) ||
        Eligible(NIRAMindEventSource.Internal, accepted) ||
        Eligible(NIRAMindEventSource.User, accepted with { ReviewCommitment = false }) ||
        Eligible(NIRAMindEventSource.User, accepted with { State = NIRACognitionState.NeedUser }) ||
        Eligible(NIRAMindEventSource.User, accepted with { Reply = "" }))
        throw new Exception("Commitment pre-review gating violated");
});

Check("Objective review accepts trusted temporal persistence without a fictional timer tool", () =>
{
    FieldInfo prompt = typeof(NIRATaskCompletionReviewService).GetField(
        "ReviewerSystemPrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new Exception("Reviewer contract unavailable");
    string policy = (string)(prompt.GetRawConstantValue() ?? "");
    if (!policy.Contains("TEMPORAL COMMITMENT PERSISTENCE", StringComparison.Ordinal) ||
        !policy.Contains("not a generic tool capability", StringComparison.Ordinal))
        throw new Exception("Reviewer is missing verified commitment evidence semantics");
});

Check("Explicit deep reasoning is discoverable through the primary output contract", () =>
{
    string? root = LocatePromptFolder();
    if (root is null) throw new Exception("Prompt directory unavailable");
    string contract = File.ReadAllText(Path.Combine(root, "cognition_contract.yaml"));
    if (!contract.Contains("EXPLICITLY requests", StringComparison.Ordinal) ||
        !contract.Contains("deepReasoningRequest", StringComparison.Ordinal))
        throw new Exception("Explicit specialist delegation instruction missing");
});

Check("Concurrent user turns retain a bounded independent reasoning lane", () =>
{
    const int expectedSlots = 3;
    var field = typeof(NIRAMindRuntime).GetField("MaximumConcurrentUserRuns",
        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
        ?? throw new Exception("Bounded user-run admission missing");
    Equal(expectedSlots, (int)field.GetRawConstantValue()!);
});

Check("Ongoing user work snapshots are truthful and cleared after completion", () =>
{
    var ledger = new NIRAActiveTurnLedger();
    using (ledger.Enter("Analyze a complex project"))
    {
        if (!ledger.Snapshot().Contains("Analyze a complex project", StringComparison.Ordinal) ||
            !ledger.Snapshot().Contains("not completed", StringComparison.Ordinal))
            throw new Exception("In-flight work ledger lost status or falsely claimed completion");
    }
    Equal(string.Empty, ledger.Snapshot());
});

Check("Internal event ownership keys share a goal and isolate independent commitments", () =>
{
    var goal = Guid.NewGuid().ToString("D");
    var first = new NIRAMindEvent { Source = NIRAMindEventSource.Internal,
        TopicKey = "branch:first", Metadata = new Dictionary<string, string> { ["goalId"] = goal } };
    var second = first with { TopicKey = "branch:other" };
    Equal(NIRAInternalWorkGate.OwnershipKey(first), NIRAInternalWorkGate.OwnershipKey(second));
    var due = new NIRAMindEvent { Source = NIRAMindEventSource.Internal,
        Name = "TemporalCommitmentDue", TopicKey = "commitment:a",
        Metadata = new Dictionary<string, string> { ["commitmentId"] = Guid.NewGuid().ToString("D") } };
    NotEqual(NIRAInternalWorkGate.OwnershipKey(first), NIRAInternalWorkGate.OwnershipKey(due));
});

Check("Internal background admission keeps same goal serialized and temporal wake independent", () =>
{
    var gate = new NIRAInternalWorkGate();
    var shared = Guid.NewGuid().ToString("D");
    var first = new NIRAMindEvent { Source = NIRAMindEventSource.Internal,
        Name = "PersistentBranchWorkResult", TopicKey = "branch:a",
        Metadata = new Dictionary<string, string> { ["goalId"] = shared } };
    var same = first with { TopicKey = "branch:b" };
    var other = new NIRAMindEvent { Source = NIRAMindEventSource.Internal,
        TopicKey = "goal:independent" };
    var due = new NIRAMindEvent { Source = NIRAMindEventSource.Internal,
        Name = "TemporalCommitmentDue", TopicKey = "commitment:independent" };

    using IDisposable firstLease = gate.EnterAsync(first).GetAwaiter().GetResult();
    Task<IDisposable> waiting = gate.EnterAsync(same);
    if (waiting.IsCompleted) throw new Exception("Same goal overlapped");
    using (gate.EnterAsync(other).GetAwaiter().GetResult())
    using (gate.EnterAsync(due).GetAwaiter().GetResult())
    {
        // Temporal work has reserved admission even when ordinary slots are busy.
        if (waiting.IsCompleted) throw new Exception("Same goal lock was bypassed");
    }
    firstLease.Dispose();
    if (!waiting.Wait(TimeSpan.FromSeconds(3)))
        throw new Exception("Same-owner work never resumed after release");
    waiting.Result.Dispose();
});

Check("S2 captures stable task identities without treating in-flight work as finished", () =>
{
    var ledger = new NIRAActiveTurnLedger();
    using var audit = ledger.Enter("Inspect the NIRA source");
    NIRAActiveWorkObservation initial = ledger.Observe();
    if (!initial.Context.Contains("Inspect the NIRA source", StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(initial.ObservedIds) ||
        !string.IsNullOrEmpty(ledger.DeliveryUpdate(initial.ObservedIds)))
        throw new Exception("Active work was lost or declared finished prematurely");
});

Check("S2 completion while answering produces grounded zero-call delivery update", () =>
{
    var ledger = new NIRAActiveTurnLedger();
    using var audit = ledger.Enter("Read-only source audit");
    NIRAActiveWorkObservation seen = ledger.Observe();
    if (!string.IsNullOrEmpty(ledger.DeliveryUpdate(seen.ObservedIds)))
        throw new Exception("Running work was claimed completed");
    audit.Finish(resultPublished: true);
    string update = ledger.DeliveryUpdate(seen.ObservedIds);
    if (!update.Contains("finished processing", StringComparison.Ordinal) ||
        !update.Contains("response is already in this conversation", StringComparison.Ordinal) ||
        !string.IsNullOrEmpty(ledger.Snapshot()))
        throw new Exception("Finished run was not published or remained active");
});

Check("S2 stopped runs cannot claim successful work or a published answer", () =>
{
    var ledger = new NIRAActiveTurnLedger();
    NIRAActiveWorkObservation observed;
    using (ledger.Enter("Interrupted file analysis"))
        observed = ledger.Observe();
    string update = ledger.DeliveryUpdate(observed.ObservedIds);
    if (!update.Contains("stopped before finishing", StringComparison.Ordinal) ||
        update.Contains("response is already", StringComparison.Ordinal))
        throw new Exception("Stopped work was reported as successful");
});

Check("S2 task status binds only to previously observed work and does not cross runs", () =>
{
    var ledger = new NIRAActiveTurnLedger();
    using var older = ledger.Enter("Older audit");
    NIRAActiveWorkObservation before = ledger.Observe();
    using var unrelated = ledger.Enter("Other task");
    unrelated.Finish(resultPublished: true);
    if (!string.IsNullOrEmpty(ledger.DeliveryUpdate(before.ObservedIds)))
        throw new Exception("A newly started task contaminated the earlier conversation");
    older.Finish(resultPublished: false);
    string update = ledger.DeliveryUpdate(before.ObservedIds);
    if (!update.Contains("finished processing, but no answer was published", StringComparison.Ordinal) ||
        update.Contains("Other task", StringComparison.Ordinal))
        throw new Exception("Run correlation or publish-state guard failed");
});

Check("S2 keeps task-status correction behind the Executive delivery boundary", () =>
{
    MethodInfo? method = typeof(NIRAExecutive).GetMethod("AttachActiveWorkLedger",
        BindingFlags.Instance | BindingFlags.NonPublic);
    if (method is null)
        throw new Exception("Executive is missing authoritative task-state attachment");
});

Check("S3 conversation-only first turn can complete without repeated models", () =>
{
    var d = new NIRACognitionDecision
    {
        State = NIRACognitionState.Complete, EmitReply = true,
        Reply = "Well, hello there.", ReplyReady = true,
        CharacterReady = true, ConversationOnly = true
    };
    if (!NIRAConversationFastPath.CanUseDirectReply(d, true, false, false, false))
        throw new Exception("Complete conversation-only reply was not admitted");
    if (NIRAConversationFastPath.CanUseDirectReply(d with { ConversationOnly = false },
            true, false, false, false))
        throw new Exception("Ordinary evidence-bound task bypassed the reviewer");
});

Check("S3 conversation-only fast path refuses tool and commitment claims", () =>
{
    var social = new NIRACognitionDecision
    {
        State = NIRACognitionState.Complete, EmitReply = true,
        Reply = "Fine. That's cute.", ReplyReady = true,
        CharacterReady = true, ConversationOnly = true
    };
    var tool = social with { CapabilityRequests = new [] { Request("system.storage.list", "{}") } };
    if (NIRAConversationFastPath.CanUseDirectReply(tool, true, false, false, false) ||
        NIRAConversationFastPath.CanUseDirectReply(social with { ReviewCommitment = true }, true, false, false, false) ||
        NIRAConversationFastPath.CanUseDirectReply(social with { ReviewExperience = true }, true, false, false, false))
        throw new Exception("Action, formation or reminder bypassed verification");
});

Check("S3 fast path refuses retries, pending objectives, and embedded app access", () =>
{
    var d = new NIRACognitionDecision { State=NIRACognitionState.Complete,
        EmitReply=true, Reply="Hi.", ReplyReady=true, CharacterReady=true,
        ConversationOnly=true };
    if (NIRAConversationFastPath.CanUseDirectReply(d, false, false, false, false) ||
        NIRAConversationFastPath.CanUseDirectReply(d, true, true, false, false) ||
        NIRAConversationFastPath.CanUseDirectReply(d, true, false, true, false) ||
        NIRAConversationFastPath.CanUseDirectReply(d, true, false, false, true))
        throw new Exception("Execution context bypassed the trusted reviewer");
});

Check("S3 character realization is skipped only with grounded stable ready reply", () =>
{
    Type policy = typeof(NIRAModelSelection).Assembly.GetType(
        "NIRAAgent.AI.Cognition.NIRACharacterDeliveryPolicy", true)!;
    MethodInfo method = policy.GetMethod("RequiresOptimizedFidelityPass", BindingFlags.Static |
        BindingFlags.Public, null, new[] { typeof(NIRACognitionDecision), typeof(string),
            typeof(bool), typeof(bool) }, null)
        ?? throw new Exception("Four-input authoritative delivery route missing");
    bool Needs(NIRACognitionDecision d, bool shift) =>
        (bool)method.Invoke(null, new object[] { d, d.Reply, false, shift })!;
    var d = new NIRACognitionDecision { State=NIRACognitionState.Complete,
        EmitReply=true, Reply="Nice try.", ReplyReady=true, CharacterReady=true,
        ConversationOnly=true };
    if (Needs(d,false) || !Needs(d,true) ||
        !Needs(d with { CharacterReady=false },false) ||
        !Needs(d with { ConversationOnly=false },false))
        throw new Exception("Stable personal delivery or state-shift safeguard broken");
});

Check("S3 current source checkpoint count is based on complete result lines", () =>
{
    Type cognition = typeof(NIRAModelSelection).Assembly.GetType(
        "NIRAAgent.AI.Cognition.NIRACognitionService", true)!;
    MethodInfo count = cognition.GetMethod("CountSourceReads", BindingFlags.Static |
        BindingFlags.NonPublic) ?? throw new Exception("Read counter unavailable");
    string evidence = "CapabilityId: filesystem.read\r\nL1: CapabilityId: filesystem.read\r\n" +
        "CapabilityId: filesystem.read\r\n";
    Equal(2, (int)count.Invoke(null, new object?[] { evidence })!);
});

Check("S3 recovery rejects runs containing non-read capability results", () =>
{
    Type cognition = typeof(NIRAModelSelection).Assembly.GetType(
        "NIRAAgent.AI.Cognition.NIRACognitionService", true)!;
    MethodInfo gate = cognition.GetMethod("HasOnlyFileReadCapabilityResults", BindingFlags.Static |
        BindingFlags.NonPublic) ?? throw new Exception("Recovery capability gate unavailable");
    bool Allowed(string raw) => (bool)gate.Invoke(null, new object?[] { raw })!;
    if (!Allowed("CapabilityId: filesystem.read\nCapabilityId: filesystem.read\n") ||
        Allowed("CapabilityId: filesystem.read\nCapabilityId: shell.execute\n") ||
        Allowed("CapabilityId: filesystem.read\n"))
        throw new Exception("Read-only recovery boundary broken");
});

Check("S3 status correction tracks original outstanding work, not finished check-in", () =>
{
    var ledger = new NIRAActiveTurnLedger();
    using var audit = ledger.Enter("Full architecture audit");
    using var checkin = ledger.Enter("Quick status check");
    var observation = ledger.Observe();
    if (!observation.Context.Contains("Full architecture audit", StringComparison.Ordinal))
        throw new Exception("Primary active work was lost");
    checkin.Finish(true);
    if (!string.IsNullOrEmpty(ledger.DeliveryUpdate(observation.ObservedIds)))
        throw new Exception("A finished check-in contaminated the audit's delivery state");
    audit.Finish(false);
    if (!ledger.DeliveryUpdate(observation.ObservedIds)
            .Contains("Full architecture audit", StringComparison.Ordinal))
        throw new Exception("Audit lifecycle was not observed");
});

Check("S3 prompt contract carries explicit single-call social classification", () =>
{
    string root = LocatePromptFolder() ?? throw new Exception("Missing prompt directory");
    string contract = File.ReadAllText(Path.Combine(root,"cognition_contract.yaml"));
    if (!contract.Contains("\"conversationOnly\": false", StringComparison.Ordinal) ||
        !contract.Contains("CONVERSATION-ONLY OPT-IN", StringComparison.Ordinal))
        throw new Exception("Focused cognitive schema lacks conversation-only control");
});

Check("S3 new messages see recent finished-work truth without extra model calls", () =>
{
    var ledger = new NIRAActiveTurnLedger();
    if (ledger.RecentOutcomeContext().Length != 0)
        throw new Exception("New ledger fabricated a prior task");
    using var audit = ledger.Enter("Code audit");
    audit.Finish(true);
    string note = ledger.RecentOutcomeContext();
    if (!note.Contains("Processing ended", StringComparison.Ordinal) ||
        !note.Contains("Code audit", StringComparison.Ordinal) ||
        note.Contains("succeeded", StringComparison.OrdinalIgnoreCase))
        throw new Exception("Recent lifecycle state was ungrounded");
});

Check("S4 due-reminder direct transition is tied to authoritative ID", () =>
{
    MethodInfo? gate = typeof(NIRAExecutive).GetMethod("ShouldPreReviewCommitment",
        BindingFlags.NonPublic | BindingFlags.Static);
    if (gate == null) throw new Exception("Trusted commitment review contract missing");
    // No direct mutation is authorized by the model or by this test.
    string source = File.ReadAllText(Path.Combine(
        Directory.GetParent(LocatePromptFolder() ??
            throw new Exception("Prompt folder unavailable"))?.Parent?.FullName ??
            throw new Exception("Project root unavailable"),
        "NIRAAgent", "Mind", "NIRAExecutive.cs"));
    if (!source.Contains("proposalGuid == dueGuid", StringComparison.Ordinal) ||
        !source.Contains("directTemporalWakeVerified = true", StringComparison.Ordinal) ||
        !source.Contains("verifiedChanges > 0", StringComparison.Ordinal))
        throw new Exception("Due event ID or persistence gates missing");
});

Check("S4 stable interpersonal overload remains compatible", () =>
{
    Type policy = typeof(NIRAModelSelection).Assembly.GetType(
        "NIRAAgent.AI.Cognition.NIRACharacterDeliveryPolicy", true)!;
    if (!policy.GetMethods(BindingFlags.Static | BindingFlags.Public).Any(x =>
        x.Name == "RequiresOptimizedFidelityPass" && x.GetParameters().Length == 4))
        throw new Exception("Compatibility overload missing");
});

Check("S4 direct reminder structure remains untrusted and bounded", () =>
{
    var reminder = new NIRACognitionDecision { State=NIRACognitionState.Complete,
        EmitReply=true, Reply="I'll remind you in two minutes.",
        ReplyReady=true, CharacterReady=true, SelfContainedAnswer=true,
        CommitmentProposals = new [] { new NIRAAgent.Self.Model.NIRACommitmentFormationProposal {
            Action = NIRAAgent.Self.Model.NIRACommitmentProposalAction.Create,
            EvidenceSource = NIRAAgent.Self.Model.NIRACommitmentEvidenceSource.NIRAReply,
            EvidenceQuote = "I'll remind you", Summary = "Check NIRA reasoning" } } };
    if (NIRAConversationFastPath.CanUseDirectReply(reminder, true, false, false, false))
        throw new Exception("Model-written reminder bypassed verified persistence");
});

Check("S4 general explanation can finish in one reviewed-by-contract cognition call", () =>
{
    var explanation = new NIRACognitionDecision { State = NIRACognitionState.Complete,
        EmitReply = true, Reply = "async enables await, which suspends without blocking a thread.",
        ReplyReady = true, CharacterReady = true, SelfContainedAnswer = true };
    if (!NIRAConversationFastPath.CanUseDirectReply(explanation, true, false, false, false))
        throw new Exception("Simple general explanation was forced through redundant cloud stages");
    if (NIRAConversationFastPath.CanUseDirectReply(explanation with {
            CapabilityRequests = new[] { Request("system.storage.list", "{}") }
        }, true, false, false, false) ||
        NIRAConversationFastPath.CanUseDirectReply(explanation, true, true, false, false) ||
        NIRAConversationFastPath.CanUseDirectReply(explanation with {
            ReviewCommitment = true }, true, false, false, false))
        throw new Exception("Knowledge classification bypassed observed work / commitment rules");
});

Check("S4 verified completed answer avoids redundant character call only while stable", () =>
{
    Type policy = typeof(NIRAModelSelection).Assembly.GetType(
        "NIRAAgent.AI.Cognition.NIRACharacterDeliveryPolicy", true)!;
    MethodInfo? method = policy.GetMethods(BindingFlags.Static | BindingFlags.Public)
        .SingleOrDefault(x => x.Name == "RequiresOptimizedFidelityPass" &&
            x.GetParameters().Length == 5);
    if (method == null) throw new Exception("Reviewed natural delivery overload is absent");
    var draft = new NIRACognitionDecision { State = NIRACognitionState.Complete,
        EmitReply = true, Reply = "C: has 9.38 GB free.", ReplyReady = true,
        CharacterReady = true, ReplyPresentation = NIRAReplyPresentationMode.Natural };
    bool Needs(bool shift, bool review, NIRACognitionDecision d) =>
        (bool)method.Invoke(null, new object[] { d, d.Reply, false, shift, review })!;
    if (Needs(false, true, draft) || !Needs(false, false, draft) ||
        !Needs(true, true, draft) || !Needs(false, true, draft with { CharacterReady = false }))
        throw new Exception("Unverified or materially changed reply bypassed character safety");
});

Check("S4 knowledge contract is explicit and cannot authorize tools", () =>
{
    string root = LocatePromptFolder() ?? throw new Exception("Missing prompt directory");
    string contract = File.ReadAllText(Path.Combine(root, "cognition_contract.yaml"));
    if (!contract.Contains("\"selfContainedAnswer\": false", StringComparison.Ordinal) ||
        !contract.Contains("SELF-CONTAINED ANSWER OPT-IN", StringComparison.Ordinal))
        throw new Exception("Knowledge one-call contract is missing");
});

Console.WriteLine($"REGRESSION CHECKS: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static NIRACapabilityRequest Request(string id, string json) => new()
{
    CapabilityId = id,
    Arguments = JsonDocument.Parse(json).RootElement.Clone()
};

void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS: {name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL: {name} - {ex.Message}"); }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}");
}

static void NotEqual<T>(T left, T right)
{
    if (EqualityComparer<T>.Default.Equals(left, right))
        throw new Exception($"Values should differ: {left}");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

static string? LocatePromptFolder()
{
    string[] origins = { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
    foreach (string origin in origins)
    {
        var directory = new DirectoryInfo(origin);
        for (int i = 0; directory != null && i < 8; directory = directory.Parent, i++)
        {
            string candidate = Path.Combine(directory.FullName, "NIRAAgent", "Prompt");
            if (Directory.Exists(candidate)) return candidate;
        }
    }
    return null;
}

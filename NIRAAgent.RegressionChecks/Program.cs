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

using System.Text.Json;
using System.Reflection;
using NIRAAgent.Capabilities;
using NIRAAgent.Embodiment;
using NIRAAgent.Presentation;

// Run with: dotnet run --project NIRAAgent.RegressionChecks
// These are offline contract checks, not simulated live model/Bridge tests.
int passed = 0;
int failed = 0;

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
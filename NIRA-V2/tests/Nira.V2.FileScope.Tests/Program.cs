using System.Text.Json;
using Nira.V2.Contracts;
using Nira.V2.Filesystem;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

int passed = 0, failed = 0;
Func<string, string?> resolver = name => Path.Combine(Path.GetTempPath(),
    "nira-phase4c1-fixture", name);

await Test("file observation requires exact independent opt-in", () =>
{
    Check(RequestedPathObservationRegistration.IsConsented("ALLOW_REQUESTED_PATH_METADATA") &&
        !RequestedPathObservationRegistration.IsConsented("allow_requested_path_metadata") &&
        !RequestedPathObservationRegistration.IsConsented("true") &&
        !RequestedPathObservationRegistration.IsConsented(null));
    return Task.CompletedTask;
});
await Test("without consent no file capability is registered or probed", () =>
{
    using var temp = new TempDir();
    var probe = new FakeProbe(RequestedPathStatus.Directory);
    var vault = new AgentPayloadVault(temp.Path);
    Check(RequestedPathObservationRegistration.Create(vault, null,
        "Check Documents/NIRA_R9_TOOL_LAB_01", probe, resolver).Count == 0 &&
        RequestedPathObservationRegistration.Create(vault, "true",
        "Check Documents/NIRA_R9_TOOL_LAB_01", probe, resolver).Count == 0 &&
        probe.Calls == 0);
    return Task.CompletedTask;
});
await Test("memory references alone never grant a path tool", () =>
{
    using var temp = new TempDir();
    var tools = RequestedPathObservationRegistration.Create(new AgentPayloadVault(temp.Path),
        "ALLOW_REQUESTED_PATH_METADATA", "Do those remembered project folders exist?",
        new FakeProbe(RequestedPathStatus.Directory), resolver);
    Check(tools.Count == 0);
    return Task.CompletedTask;
});
await Test("one explicit current-turn path registers one read-only tool", () =>
{
    using var temp = new TempDir();
    var tools = RequestedPathObservationRegistration.Create(new AgentPayloadVault(temp.Path),
        "ALLOW_REQUESTED_PATH_METADATA", "Check Documents/NIRA_R9_TOOL_LAB_01",
        new FakeProbe(RequestedPathStatus.Directory), resolver);
    Check(tools.Count == 1 && tools[0].Spec.Id == "filesystem.requested_path_metadata" &&
        tools[0].Spec.Access == AccessLevel.Observe);
    return Task.CompletedTask;
});
await Test("quoted known-folder path with spaces resolves as ONE scope", () =>
{
    Check(RequestedPathObservationRegistration.TryCreateScope(
        "Please check `Documents/My Project/Notes` today", resolver, out var scope) &&
        scope!.ChildSegments.SequenceEqual(new[] { "My Project", "Notes" }));
    return Task.CompletedTask;
});
await Test("unquoted sentence punctuation is not part of a known-folder path", () =>
{
    const string request = "List the files and folders directly inside Documents/TestFolder. " +
        "Use your real directory listing tool. Don't guess, and tell me if the listing is incomplete.";
    Check(RequestedPathObservationRegistration.TryCreateScope(request, resolver, out var scope) &&
        scope!.UserLabel == "Documents/TestFolder" &&
        scope.ChildSegments.SequenceEqual(new[] { "TestFolder" }));
    Check(RequestedPathObservationRegistration.TryCreateScope(
        "Check Documents/report.md. Then explain.", resolver, out var fileScope) &&
        fileScope!.ChildSegments.SequenceEqual(new[] { "report.md" }));
    return Task.CompletedTask;
});
await Test("quoted trailing dots and double dots remain invalid", () =>
{
    Check(!RequestedPathObservationRegistration.TryCreateScope(
        "List `Documents/TestFolder.`", resolver, out _));
    Check(!RequestedPathObservationRegistration.TryCreateScope(
        "List Documents/TestFolder..", resolver, out _));
    return Task.CompletedTask;
});
await Test("two period-terminated distinct paths never widen scope", () =>
{
    Check(!RequestedPathObservationRegistration.TryCreateScope(
        "List Documents/A. Then list Documents/B.", resolver, out _));
    return Task.CompletedTask;
});
await Test("two different paths are ambiguous and authorize neither", () =>
{
    Check(!RequestedPathObservationRegistration.TryCreateScope(
        "Compare Documents/ProjectA and Documents/ProjectB", resolver, out _));
    return Task.CompletedTask;
});
await Test("duplicate mentions of the SAME path do not broaden scope", () =>
{
    Check(RequestedPathObservationRegistration.TryCreateScope(
        "Is Documents/ProjectA the same as Documents/ProjectA?", resolver, out var scope) &&
        scope!.ChildSegments.Count == 1);
    Check(RequestedPathObservationRegistration.TryCreateScope(
        "Check `Documents/ProjectA` and also \"Documents/ProjectA\"", resolver, out _));
    return Task.CompletedTask;
});
await Test("traversal and dangerous metacharacters never grant scopes", () =>
{
    string[] invalid = {
        "`Documents/../secret`", "`Documents/foo/../../private`",
        "`Documents/%APPDATA%`", "`Documents/file:stream`",
        "`Documents/foo*`", "`Documents/CON`", "`Documents/NUL.txt`"
    };
    Check(invalid.All(x => !RequestedPathObservationRegistration.TryCreateScope(
        "Check " + x, resolver, out _)));
    return Task.CompletedTask;
});
await Test("unavailable known-folder root cannot grant capability", () =>
{
    Check(!RequestedPathObservationRegistration.TryCreateScope(
        "Check Documents/ProjectA", _ => null, out _));
    return Task.CompletedTask;
});
await Test("dynamic model schema has NO path argument", () =>
{
    using var temp = new TempDir();
    var tools = RequestedPathObservationRegistration.Create(new AgentPayloadVault(temp.Path),
        "ALLOW_REQUESTED_PATH_METADATA", "Check Documents/ProjectA",
        new FakeProbe(RequestedPathStatus.Directory), resolver);
    var registry = new CapabilityRegistry(tools);
    // Inspect the FUNCTION ARGUMENT SCHEMA, not arbitrary serialized text.
    // The safe function name itself ends with "_path", so searching the whole
    // provider payload for 'path"' gives a false failure.
    using var provider = JsonDocument.Parse(JsonSerializer.Serialize(
        registry.BuildProviderTools(registry.Tools.Select(t => t.Spec).ToArray())));
    Check(provider.RootElement.ValueKind == JsonValueKind.Array &&
        provider.RootElement.GetArrayLength() == 1);
    JsonElement function = provider.RootElement[0].GetProperty("function");
    JsonElement parameters = function.GetProperty("parameters");
    JsonElement properties = parameters.GetProperty("properties");
    JsonElement required = parameters.GetProperty("required");
    Check(registry.Count == 1 &&
        function.GetProperty("name").GetString() == "filesystem_check_requested_path" &&
        parameters.GetProperty("type").GetString() == "object" &&
        properties.ValueKind == JsonValueKind.Object &&
        !properties.EnumerateObject().Any() &&
        required.ValueKind == JsonValueKind.Array &&
        required.GetArrayLength() == 0 &&
        parameters.GetProperty("additionalProperties").ValueKind == JsonValueKind.False);
    return Task.CompletedTask;
});
await Test("model cannot substitute a path argument", () =>
{
    using var temp = new TempDir();
    var tool = CreateTool(new AgentPayloadVault(temp.Path),
        new FakeProbe(RequestedPathStatus.Directory));
    Check(tool.ValidateArguments(JsonSerializer.SerializeToElement(new { })) &&
        !tool.ValidateArguments(JsonSerializer.SerializeToElement(new { path = "Documents/Other" })) &&
        !tool.ValidateArguments(JsonSerializer.SerializeToElement(new[] { 1 })));
    return Task.CompletedTask;
});
await Test("actual successful observation binds to host-requested path", async () =>
{
    using var temp = new TempDir();
    var vault = new AgentPayloadVault(temp.Path);
    var probe = new FakeProbe(RequestedPathStatus.Directory);
    var tool = CreateTool(vault, probe);
    var (receipt, result) = await Invoke(tool, vault, new { });
    Check(receipt.Succeeded && probe.Calls == 1 &&
        result.GetProperty("requestedPath").GetString() == "Documents/ProjectA" &&
        result.GetProperty("exists").GetBoolean() &&
        result.GetProperty("kind").GetString() == "directory" &&
        !result.GetRawText().Contains(probe.Seen!.FullPath, StringComparison.Ordinal));
});
await Test("missing path is reported as observed absence, not guessed", async () =>
{
    using var temp = new TempDir();
    var vault = new AgentPayloadVault(temp.Path);
    var tool = CreateTool(vault, new FakeProbe(RequestedPathStatus.Missing));
    var (receipt, result) = await Invoke(tool, vault, new { });
    Check(receipt.Succeeded && !result.GetProperty("exists").GetBoolean() &&
        result.GetProperty("kind").GetString() == "missing");
});
await Test("unsafe symlinks and unreadable directories never count as proof", async () =>
{
    using var temp = new TempDir();
    var vault = new AgentPayloadVault(temp.Path);
    foreach (var status in new[] { RequestedPathStatus.UnsafeLink, RequestedPathStatus.Unavailable })
    {
        var tool = CreateTool(vault, new FakeProbe(status));
        var (receipt, _) = await Invoke(tool, vault, new { });
        Check(!receipt.Succeeded && receipt.ResultReference is null &&
            receipt.OutcomeCode == (status == RequestedPathStatus.UnsafeLink ?
                "PATH_LINK_NOT_FOLLOWED" : "PATH_OBSERVATION_UNAVAILABLE"));
    }
});
await Test("forged cross-tool and malformed tokens never reach filesystem", async () =>
{
    using var temp = new TempDir();
    var vault = new AgentPayloadVault(temp.Path);
    var probe = new FakeProbe(RequestedPathStatus.File);
    var tool = CreateTool(vault, probe);
    string foreign = await vault.SaveArgumentAsync(Guid.NewGuid(), "windows.visible_apps",
        JsonSerializer.SerializeToElement(new { }));
    var a = await tool.ExecuteAsync(foreign, default);
    var b = await tool.ExecuteAsync("not-an-opaque-reference", default);
    Check(!a.Succeeded && !b.Succeeded && probe.Calls == 0);
});
await Test("saved arguments with extra keys fail before filesystem observation", async () =>
{
    using var temp = new TempDir();
    var vault = new AgentPayloadVault(temp.Path);
    var probe = new FakeProbe(RequestedPathStatus.File);
    var tool = CreateTool(vault, probe);
    var (receipt, _) = await Invoke(tool, vault, new { extra = "C:/users/private" });
    Check(!receipt.Succeeded && receipt.OutcomeCode == "INVALID_PATH_ARGUMENTS" &&
        probe.Calls == 0);
});
await Test("real local file and folder existence are read-only", () =>
{
    using var temp = new TempDir();
    string root = Path.Combine(temp.Path, "Documents");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(root, "ProjectA"));
    string f = Path.Combine(root, "ProjectA", "note.txt");
    File.WriteAllText(f, "must never be read by this capability");
    var probe = new LocalRequestedPathProbe();
    foreach (var (relative, expected) in new[] {
        ("Documents/ProjectA", RequestedPathStatus.Directory),
        ("Documents/ProjectA/note.txt", RequestedPathStatus.File),
        ("Documents/ProjectA/missing.txt", RequestedPathStatus.Missing)
    })
    {
        Check(RequestedPathObservationRegistration.TryCreateScope(
            "Check " + relative, _ => root, out var scope));
        Check(probe.Observe(scope!, default) == expected);
    }
    Check(File.ReadAllText(f) == "must never be read by this capability");
    return Task.CompletedTask;
});

Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

RequestedPathMetadataCapability CreateTool(AgentPayloadVault vault, FakeProbe probe)
{
    var capabilities = RequestedPathObservationRegistration.Create(vault,
        "ALLOW_REQUESTED_PATH_METADATA", "Check Documents/ProjectA", probe, resolver);
    Check(capabilities.Count == 1);
    return (RequestedPathMetadataCapability)capabilities[0];
}
static async Task<(AgentToolReceipt, JsonElement)> Invoke(IAgentTool tool,
    AgentPayloadVault vault, object args)
{
    Guid runId = Guid.NewGuid();
    string token = await vault.SaveArgumentAsync(runId, tool.Spec.Id,
        JsonSerializer.SerializeToElement(args));
    AgentToolReceipt receipt = await tool.ExecuteAsync(token, default);
    JsonElement result = receipt.ResultReference is null ? default :
        await vault.ReadResultAsync(runId, tool.Spec.Id, receipt.ResultReference);
    return (receipt, result);
}
async Task Test(string label, Func<Task> work)
{
    try { await work(); passed++; Console.WriteLine("PASS " + label); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + label + ": " + ex.GetType().Name + " " + ex.Message); }
}
static void Check(bool condition, string message = "assertion failed")
{
    if (!condition) throw new Exception(message);
}
sealed class FakeProbe(RequestedPathStatus status) : IRequestedPathProbe
{
    public int Calls { get; private set; }
    public RequestedPathScope? Seen { get; private set; }
    public RequestedPathStatus Observe(RequestedPathScope scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Calls++; Seen = scope; return status;
    }
}
sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "nira-4c1-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
}

using System.Text.Json;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.Filesystem;
using Nira.V2.General;
using Nira.V2.LegacyAdapters;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new InvalidOperationException("FAIL " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

string temp = Path.Combine(Path.GetTempPath(), "nira-v2-directory-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    string documents = Path.Combine(temp, "Documents");
    string project = Path.Combine(documents, "NIRA");
    Directory.CreateDirectory(project);
    string child = Path.Combine(project, "README.md");
    await File.WriteAllTextAsync(child, "test file");
    Func<string, string?> resolver = name => name.Equals("Documents", StringComparison.OrdinalIgnoreCase)
        ? documents : null;
    var vault = new AgentPayloadVault(Path.Combine(temp, "vault"));
    var fake = new FakeListExecutor { OutputText = $"FILE\t9\t{child}\nDIR\t{Path.Combine(project,"src")}" };
    string ask = "List files and folders in Documents/NIRA";
    string consent = LegacyDirectoryListRegistration.ConsentValue;

    Check("explicit opt-in is required", LegacyDirectoryListRegistration.Create(vault,
        fake, null, ask, rootResolver: resolver).Count == 0);
    Check("case-altered opt-in fails closed", LegacyDirectoryListRegistration.Create(vault,
        fake, consent.ToLowerInvariant(), ask, rootResolver: resolver).Count == 0);
    Check("opt-in constant is exact", LegacyDirectoryListRegistration.IsConsented(consent));
    Check("a remembered path gives no grant", LegacyDirectoryListRegistration.Create(vault,
        fake, consent, "List the directory we discussed earlier", rootResolver: resolver).Count == 0);
    Check("a plain directory-existence question does not grant listing", LegacyDirectoryListRegistration.Create(vault,
        fake, consent, "Does Documents/NIRA exist?", rootResolver: resolver).Count == 0);
    Check("a file-read question does not grant directory listing", LegacyDirectoryListRegistration.Create(vault,
        fake, consent, "Read Documents/NIRA/README.md", rootResolver: resolver).Count == 0);
    Check("two distinct explicit paths cannot authorize either", LegacyDirectoryListRegistration.Create(vault,
        fake, consent, "List Documents/NIRA and Documents/Others", rootResolver: resolver).Count == 0);
    Check("traversal cannot grant directory access", LegacyDirectoryListRegistration.Create(vault,
        fake, consent, "List `Documents/../NIRA`", rootResolver: resolver).Count == 0);
    Check("unknown folder aliases are not granted", LegacyDirectoryListRegistration.Create(vault,
        fake, consent, "List Secrets/NIRA", rootResolver: resolver).Count == 0);

    IReadOnlyList<IGeneralCapability> available = LegacyDirectoryListRegistration.Create(vault,
        fake, consent, ask, rootResolver: resolver);
    Check("exact single directory capability registered", available.Count == 1 &&
        available[0].Spec.Id == "legacy.filesystem.list_exact" && available[0].Spec.Access == AccessLevel.Observe);
    IGeneralCapability tool = available[0];
    Check("native model function is dynamic registry entry", new CapabilityRegistry(available)
        .TryGetFunction("nira_list_exact_user_directory", out IGeneralCapability registered) &&
        ReferenceEquals(tool, registered));
    using (JsonDocument schema = JsonDocument.Parse(tool.InputSchema.GetRawText()))
        Check("schema has no model-controlled path or options", schema.RootElement.GetProperty("properties")
            .EnumerateObject().Count() == 0 &&
            schema.RootElement.GetProperty("additionalProperties").ValueKind == JsonValueKind.False);
    Check("model path injection rejected", !tool.ValidateArguments(JsonSerializer.SerializeToElement(
        new { path = "Documents/elsewhere" })));
    Check("model recursive search rejected", !tool.ValidateArguments(JsonSerializer.SerializeToElement(
        new { recursive = true })));
    Check("empty schema accepted", tool.ValidateArguments(JsonSerializer.SerializeToElement(new { })));

    Guid run = Guid.NewGuid();
    string token = await vault.SaveArgumentAsync(run, tool.Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    AgentToolReceipt receipt = await tool.ExecuteAsync(token, CancellationToken.None);
    Check("delegates to existing NIRA filesystem.list", fake.Calls == 1 &&
        fake.LastRequest?.CapabilityId == NIRACapabilityIds.DirectoryList);
    Check("host owns path and listing bounds", fake.LastRequest is not null &&
        fake.LastRequest.Arguments.GetProperty("path").GetString() == project &&
        fake.LastRequest.Arguments.GetProperty("pattern").GetString() == "*" &&
        fake.LastRequest.Arguments.GetProperty("recursive").ValueKind == JsonValueKind.False &&
        fake.LastRequest.Arguments.GetProperty("maxEntries").GetInt32() == 30 &&
        fake.LastRequest.Arguments.GetProperty("offset").GetInt32() == 0);
    Check("audited read-only directory result completed", receipt.Succeeded &&
        !receipt.OutcomeConfirmed && receipt.ResultReference is not null);
    JsonElement observed = await vault.ReadResultAsync(run, tool.Spec.Id, receipt.ResultReference!);
    // Contract regression: the default serializer would otherwise emit PascalCase
    // record properties and break model-visible entry-name parsing at this point.
    JsonElement observedEntries = observed.GetProperty("entries");
    Check("only immediate relative names in model observation",
        observedEntries.GetArrayLength() == 2 &&
        observedEntries[0].GetProperty("name").GetString() == "README.md" &&
        observedEntries[0].GetProperty("kind").GetString() == "file" &&
        observedEntries[0].GetProperty("sizeBytes").GetInt64() == 9 &&
        !observedEntries[0].TryGetProperty("Name", out _) &&
        observedEntries[1].GetProperty("name").GetString() == "src" &&
        observedEntries[1].GetProperty("kind").GetString() == "directory" &&
        observedEntries[1].GetProperty("sizeBytes").ValueKind == JsonValueKind.Null &&
        !observed.GetRawText().Contains(project, StringComparison.OrdinalIgnoreCase));
    Check("bounded listing explicitly marked partial", observed.GetProperty("firstPageOnly").GetBoolean());
    Check("a forged argument reference cannot execute", !(await tool.ExecuteAsync("invalid-token", default)).Succeeded &&
        fake.Calls == 1);
    string wrong = await vault.SaveArgumentAsync(Guid.NewGuid(), tool.Spec.Id,
        JsonSerializer.SerializeToElement(new { path = "Documents/evil" }));
    Check("persisted injected arguments cannot execute", !(await tool.ExecuteAsync(wrong, default)).Succeeded &&
        fake.Calls == 1);
    var fileScope = new RequestedPathScope("Documents/NIRA/README.md", documents, child,
        ["NIRA", "README.md"]);
    var fileTool = new LegacyDirectoryListCapability(vault, fileScope, fake, new LocalRequestedPathProbe());
    int callSnapshot = fake.Calls;
    Check("a file cannot be treated as a directory", !(await fileTool.ExecuteAsync(token, default)).Succeeded &&
        fake.Calls == callSnapshot);
    var unsafeTool = new LegacyDirectoryListCapability(vault,
        new RequestedPathScope("Documents/NIRA", documents, project, ["NIRA"]), fake, new UnsafeProbe());
    Check("symlink/junction scope rejected before delegation", !(await unsafeTool.ExecuteAsync(token, default)).Succeeded &&
        fake.Calls == callSnapshot);

    fake.Status = NIRACapabilityResultStatus.AuthorizationRequired;
    Check("permission-required result remains incomplete", (await tool.ExecuteAsync(token, default)).OutcomeCode ==
        "LEGACY_PERMISSION_REQUIRED");
    fake.Status = NIRACapabilityResultStatus.Succeeded;
    fake.Mutated = true;
    Check("unexpected legacy write receipt rejected", !(await tool.ExecuteAsync(token, default)).Succeeded);
    fake.Mutated = false;
    fake.Audited = false;
    Check("missing audit receipt rejected", !(await tool.ExecuteAsync(token, default)).Succeeded);
    fake.Audited = true;
    fake.WrongId = true;
    Check("wrong request identity rejected", !(await tool.ExecuteAsync(token, default)).Succeeded);
    fake.WrongId = false;
    fake.WrongCapability = true;
    Check("wrong capability identity rejected", !(await tool.ExecuteAsync(token, default)).Succeeded);
    fake.WrongCapability = false;
    fake.Fails = true;
    Check("legacy exception cannot become successful listing", !(await tool.ExecuteAsync(token, default)).Succeeded);
    fake.Fails = false;

    string outside = Path.Combine(documents, "outside.md");
    Check("parent escape from legacy output is rejected", !LegacyDirectoryListCapability.TryParseOutput(
        $"FILE\t3\t{outside}", project, out _, out _));
    Check("invalid absolute path is rejected", !LegacyDirectoryListCapability.TryParseOutput(
        "FILE\t3\tfile.txt", project, out _, out _));
    Check("malformed listing records are rejected", !LegacyDirectoryListCapability.TryParseOutput(
        "DIR\tEXTRA\t" + project, project, out _, out _));
    Check("negative reported file size is rejected", !LegacyDirectoryListCapability.TryParseOutput(
        $"FILE\t-1\t{child}", project, out _, out _));
    Check("overlong legacy output is rejected", !LegacyDirectoryListCapability.TryParseOutput(
        new string('x', 24001), project, out _, out _));
    Check("more than 30 entries is rejected", !LegacyDirectoryListCapability.TryParseOutput(
        string.Join("\n", Enumerable.Repeat($"DIR\t{Path.Combine(project,"src")}", 31)),
        project, out _, out _));
    Check("duplicate ambiguous names are rejected", !LegacyDirectoryListCapability.TryParseOutput(
        $"DIR\t{Path.Combine(project,"src")}\nDIR\t{Path.Combine(project,"src")}",
        project, out _, out _));
    Check("sensitive and hidden names are omitted", LegacyDirectoryListCapability.TryParseOutput(
        $"FILE\t8\t{Path.Combine(project,"api_token.txt")}\nDIR\t{Path.Combine(project,".git")}\nFILE\t4\t{child}",
        project, out IReadOnlyList<DirectoryEntryObservation> safe, out int filtered) &&
        safe.Count == 1 && filtered == 2 && safe[0].Name == "README.md");
    Check("empty legacy listing is valid but marked as first page", LegacyDirectoryListCapability.TryParseOutput(
        "", project, out IReadOnlyList<DirectoryEntryObservation> empty, out _) && empty.Count == 0);
    Check("names are screened for sensitive patterns", !LegacyDirectoryListRegistration.IsSafeEntryName("credential_dump.md") &&
        !LegacyDirectoryListRegistration.IsSafeEntryName(".env") &&
        LegacyDirectoryListRegistration.IsSafeEntryName("MainWindow.xaml"));
    Console.WriteLine($"RESULT: {passed} Phase4E2 directory delegation checks passed; 0 failed.");
}
finally
{
    try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
}

sealed class FakeListExecutor : ILegacyDirectoryListExecutor
{
    public string OutputText { get; set; } = "";
    public int Calls { get; private set; }
    public NIRACapabilityRequest? LastRequest { get; private set; }
    public NIRACapabilityResultStatus Status { get; set; } = NIRACapabilityResultStatus.Succeeded;
    public bool Mutated { get; set; }
    public bool Audited { get; set; } = true;
    public bool WrongId { get; set; }
    public bool WrongCapability { get; set; }
    public bool Fails { get; set; }
    public Task<NIRACapabilityResult> ExecuteListAsync(NIRACapabilityRequest request, CancellationToken ct)
    {
        Calls++;
        LastRequest = request;
        if (Fails) throw new InvalidOperationException("Fake executor error");
        return Task.FromResult(new NIRACapabilityResult
        {
            RequestId = WrongId ? Guid.NewGuid() : request.RequestId,
            CapabilityId = WrongCapability ? NIRACapabilityIds.FileWrite : request.CapabilityId,
            Risk = NIRACapabilityRisk.Observe,
            Status = Status,
            ChangedSystemState = Mutated,
            AuditRecorded = Audited,
            Output = OutputText
        });
    }
}
sealed class UnsafeProbe : IRequestedPathProbe
{
    public RequestedPathStatus Observe(RequestedPathScope scope, CancellationToken ct) =>
        RequestedPathStatus.UnsafeLink;
}

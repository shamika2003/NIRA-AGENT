using System.Text.Json;
using Nira.Core;
using Nira.Infrastructure;

var cases = new (string Name, Func<Task> Test)[] {
    ("schema-rejects-unexpected-properties", SchemaRejectsUnexpectedProperties),
    ("clarification-resumes-original-objective", ClarificationResumes),
    ("authorized-workspace-write-has-observed-receipt", WorkspaceWrite),
    ("out-of-scope-write-cannot-run-without-consent", ExternalWriteDenied),
    ("dispatched-action-restart-requires-reconciliation", InterruptedWrite),
    ("existing-file-is-not-overwritten", NoOverwrite),
    ("sqlite-state-is-optimistically-versioned", OptimisticConcurrency),
    ("invalid-tool-arguments-do-not-touch-disk", InvalidArguments),
    ("multi-step-plan-is-persisted", PlanPersistence),
    ("unverified-action-claim-is-rejected", UnverifiedCompletion),
    ("user-approved-outside-write-is-audited", OutsideWriteApproved),
    ("dpapi-does-not-store-conversation-as-plaintext", ProtectedConversation),
    ("workspace-defaults-to-user-appdata", WorkspaceDefaultsToAppData),
    ("workspace-custom-location-preserves-existing-content", WorkspaceCustomLocation),
    ("workspace-blocked-location-explains-recovery", WorkspaceBlockedLocation),
    ("workspace-rejects-relative-override", WorkspaceRelativeOverride),
    ("ordinary-conversation-completes-without-finish-tool", OrdinaryConversation),
    ("empty-model-reply-retries-without-polluting-history", EmptyReplyRecovery),
    ("empty-tool-mode-falls-back-to-tool-free-chat", ToolFreeRecovery),
    ("empty-model-replies-pause-without-fabricating", PersistentEmptyReply),
    ("unfinished-plan-does-not-implicitly-complete", PlainReplyWithPendingPlan),
    ("read-tool-followed-by-plain-answer-completes", ReadThenPlainAnswer),
    ("openai-compatible-plain-text-content", WirePlainText),
    ("openai-compatible-text-parts-content", WireContentParts),
    ("model-credential-dpapi-roundtrip-no-plaintext", CredentialEncrypted),
    ("model-credential-scoped-to-provider", CredentialEndpointIsolation),
    ("model-credential-corrupt-record-can-be-replaced", CredentialCorruptedRecord),
    ("provider-401-is-typed-authentication-failure", ProviderUnauthorized),
    ("provider-uses-encrypted-saved-bearer-key", ProviderUsesSavedKey),
    ("credential-retry-does-not-duplicate-user-input", CredentialRetryDoesNotDuplicate),
    ("mutation-plain-reply-requires-receipt-confirmation", MutationPlainReplyNeedsEvidence),
    ("repeated-mutation-plain-replies-pause-safely", MutationPlainReplyPauses),
    ("action-cannot-be-finished-as-information", MutationMustFinishAsAction),
    ("failed-tool-call-can-recover-with-new-action", ToolFailureCanRecover),
    ("unfinished-plan-cannot-be-marked-complete", UnfinishedPlanRejectsFinish),
    ("http-tool-protocol-real-multistep-write-read-hash", WireEndToEndToolPipeline),
    ("directory-create-has-observed-receipt", DirectoryCreateObserved),
    ("directory-existing-file-is-preserved", DirectoryExistingPreserved),
    ("directory-outside-workspace-requires-approval", DirectoryOutsideDenied),
    ("edit-preserves-utf8-bom-and-crlf", EditTextPreservesBytes),
    ("edit-stale-hash-rejected", EditTextStaleHash),
    ("edit-ambiguous-match-rejected", EditTextAmbiguous),
    ("edit-outside-workspace-requires-approval", EditTextApprovedOutside),
    ("http-tool-protocol-versioned-edit-readback", WireEditTextPipeline),
    ("process-execution-requires-explicit-approval", ProcessDenied),
    ("process-direct-argv-captures-exit-and-output", ProcessDotnetVersion),
    ("process-working-directory-cannot-escape-workspace", ProcessWorkingDirectoryScope),
    ("process-does-not-inherit-api-key", ProcessSecretsExcluded),
    ("process-kills-timeout-and-records-it", ProcessTimeout),
    ("process-nonzero-exit-still-reports-effects", ProcessNonzeroExit),
    ("process-agent-reports-approved-execution-evidence", ProcessAgentExecution)
};
int failed = 0;
foreach (var (name, test) in cases)
{
    try { await test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + " : " + ex); }
}
Console.WriteLine($"NIRA KERNEL TESTS: {cases.Length - failed}/{cases.Length} passed");
return failed == 0 ? 0 : 1;

static Task SchemaRejectsUnexpectedProperties()
{
    JsonElement schema = JsonToolSchema.Schema("""
      {"type":"object","required":["path"],"properties":{"path":{"type":"string"}},
       "additionalProperties":false}
      """);
    Assert(!JsonToolSchema.Validate(schema,
        JsonSerializer.SerializeToElement(new { path="abc", unsafeFlag=true }), out _),
        "Unknown property should be rejected");
    Assert(JsonToolSchema.Validate(schema,
        JsonSerializer.SerializeToElement(new { path="abc" }), out _),
        "Valid property should be accepted");
    return Task.CompletedTask;
}

static async Task ClarificationResumes()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("ask1", "nira_ask_user", new { question="Which location should I use?" })]),
        new ModelReply(null, [Call("finish2", "nira_finish", new {
            outcome="answer", answer="I can use the location you provided.", evidence_ids=Array.Empty<string>() })])
    ]);
    var engine = setup.Engine(model);
    Guid id = Guid.NewGuid();
    var first = await engine.RunAsync(id, "Help me with an unspecified destination.", new FakeApprovals(false));
    Assert(first.Status == ConversationStatus.AwaitingUser, "AwaitingUser should be persisted");
    var next = await engine.RunAsync(id, "Use my Documents folder.", new FakeApprovals(false));
    Assert(next.Status == ConversationStatus.Completed, "Follow-up should finish same run");
    ConversationState saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Objective == "Help me with an unspecified destination.", "Objective must survive follow-up");
    Assert(saved.Messages.Any(x => x.Role == "user" && x.Content == "Use my Documents folder."), "Follow-up must enter transcript");
}

static async Task WorkspaceWrite()
{
    using var setup = new TestContext();
    const string fileName = "research-summary.txt";
    var model = new FakeModel(messages => {
        var observed = messages.LastOrDefault(m => m.Role == "tool" && m.Content?.Contains("receiptId", StringComparison.Ordinal) == true);
        if (observed is null)
            return new ModelReply(null, [Call("write1", "filesystem.write_text", new { path=fileName, content="Grounded text." })]);
        using var result = JsonDocument.Parse(observed.Content!);
        string receipt = result.RootElement.GetProperty("receiptId").GetString()!;
        return new ModelReply(null, [Call("finish1", "nira_finish", new {
            outcome="action", answer="Created and verified the file.", evidence_ids=new[] { receipt } })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create a new text file", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Verified write should complete");
    Assert(File.ReadAllText(Path.Combine(setup.Workspace, fileName)) == "Grounded text.", "File contents must match");
    Assert(result.EvidenceIds.Count == 1, "Receipt must appear in result");
}

static async Task ExternalWriteDenied()
{
    using var setup = new TestContext();
    string external = Path.Combine(Path.GetTempPath(), "nira-unapproved-" + Guid.NewGuid().ToString("N") + ".txt");
    var model = new FakeModel([
        new ModelReply(null, [Call("outside1", "filesystem.write_text", new { path=external, content="Denied" })]),
        new ModelReply(null, [Call("finish2", "nira_finish", new {
            outcome="answer", answer="I did not write outside the workspace.", evidence_ids=Array.Empty<string>() })])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Write to this outside path", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Denial can end with an honest answer");
    Assert(!File.Exists(external), "No unauthorized file should exist");
}

static async Task InterruptedWrite()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var state = new ConversationState { Id = id, Objective = "Write important output", Status = ConversationStatus.Running };
    state.Operations.Add(new AgentOperation { Id="pending123", ToolName="filesystem.write_text",
        Status = OperationStatus.Dispatched, ArgumentsJson="{}" });
    await setup.Store.SaveAsync(state, default);
    var engine = setup.Engine(new FakeModel([]));
    var result = await engine.RunAsync(id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.NeedsReconciliation,
        "In-flight action must not replay after restart");
}

static async Task NoOverwrite()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "existing.txt");
    await File.WriteAllTextAsync(path, "ORIGINAL");
    var model = new FakeModel([
        new ModelReply(null, [Call("write1", "filesystem.write_text", new { path="existing.txt", content="OVERWRITE" })]),
        new ModelReply(null, [Call("finish2", "nira_finish", new {
            outcome="answer", answer="The existing file was protected.", evidence_ids=Array.Empty<string>() })])
    ]);
    await setup.Engine(model).RunAsync(Guid.NewGuid(), "Replace file", new FakeApprovals(false));
    Assert(await File.ReadAllTextAsync(path) == "ORIGINAL", "Existing file must not be overwritten");
}

static async Task OptimisticConcurrency()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    await setup.Store.SaveAsync(new ConversationState { Id=id, Objective="one" }, default);
    ConversationState a = (await setup.Store.LoadAsync(id, default))!;
    ConversationState b = (await setup.Store.LoadAsync(id, default))!;
    a.Objective = "changed";
    await setup.Store.SaveAsync(a, default);
    bool threw = false;
    try { await setup.Store.SaveAsync(b, default); }
    catch (AgentException) { threw = true; }
    Assert(threw, "Stale writers must not overwrite state");
}

static async Task InvalidArguments()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("invalid", "filesystem.write_text", new { path="bad.txt" })]),
        new ModelReply(null, [Call("ask", "nira_ask_user", new { question="What content should I write?" })])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Write content", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.AwaitingUser, "Invalid shape should allow user recovery");
    Assert(!File.Exists(Path.Combine(setup.Workspace, "bad.txt")), "Malformed call should not write");
}

static async Task PlanPersistence()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("plan1", "nira_plan", new { steps = new[] {
            new { description="Inspect the environment", successCriterion="Observation recorded" },
            new { description="Verify the result", successCriterion="Actual output checked" }
        } })]),
        new ModelReply(null, [Call("update1", "nira_plan_update", new { index=0, status="completed" })]),
        new ModelReply(null, [Call("ask1", "nira_ask_user", new { question="What should I verify next?" })])
    ]);
    Guid id = Guid.NewGuid();
    var outcome = await setup.Engine(model).RunAsync(id, "Investigate this task", new FakeApprovals(false));
    Assert(outcome.Status == ConversationStatus.AwaitingUser, "Task should await clarification");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Plan.Count == 2 && state.Plan[0].Status == "completed", "Plan steps must be durable");
}

static async Task UnverifiedCompletion()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("finish1", "nira_finish", new {
            outcome="action", answer="I saved the file", evidence_ids=Array.Empty<string>() })]),
        new ModelReply(null, [Call("ask2", "nira_ask_user", new { question="I lack a write receipt; may I proceed?" })])
    ]);
    var outcome = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create a file", new FakeApprovals(false));
    Assert(outcome.Status == ConversationStatus.AwaitingUser, "Unverified effect must not finish as success");
}

static async Task OutsideWriteApproved()
{
    using var setup = new TestContext();
    string externalDir = Path.Combine(setup.Root, "external");
    Directory.CreateDirectory(externalDir);
    string path = Path.Combine(externalDir, "approved.txt");
    var model = new FakeModel(messages => {
        var observed = messages.LastOrDefault(m => m.Role == "tool" && m.Content?.Contains("receiptId", StringComparison.Ordinal) == true);
        if (observed is null)
            return new ModelReply(null, [Call("outside1", "filesystem.write_text", new { path, content="Approved" })]);
        using var result = JsonDocument.Parse(observed.Content!);
        string receipt = result.RootElement.GetProperty("receiptId").GetString()!;
        return new ModelReply(null, [Call("finish2", "nira_finish", new {
            outcome="action", answer="Created approved file", evidence_ids=new[] { receipt } })]);
    });
    var answer = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Write an approved external file", new FakeApprovals(true));
    Assert(answer.Status == ConversationStatus.Completed && File.Exists(path), "Approved external write should be allowed");
}

static async Task ProtectedConversation()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    const string marker = "NIRA_TEST_PRIVATE_PHRASE_88273419";
    await setup.Store.SaveAsync(new ConversationState { Id = id, Objective = marker }, default);

    // SQLite uses WAL and pooled connections. Reading the live .db file directly
    // with File.ReadAllBytesAsync can fail on Windows because SQLite still holds
    // a handle, and a raw file read can miss the latest row in the WAL anyway.
    // Read the persisted ciphertext through SQLite's normal snapshot semantics.
    var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {
        DataSource = setup.Database,
        Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString();
    string ciphertext;
    await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM conversations WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        ciphertext = await command.ExecuteScalarAsync() as string
            ?? throw new InvalidOperationException("No stored conversation payload was found.");
    }

    Assert(!ciphertext.Contains(marker, StringComparison.Ordinal),
        "Plaintext objective leaked into persisted SQLite payload");
    byte[] protectedBytes = Convert.FromBase64String(ciphertext);
    Assert(protectedBytes.Length > 32,
        "Persisted conversation does not have a plausible protected payload");
    var recovered = await setup.Store.LoadAsync(id, default);
    Assert(recovered?.Objective == marker, "DPAPI roundtrip failed");
}

static Task WorkspaceDefaultsToAppData()
{
    using var setup = new TestContext();
    string appDataRoot = Path.Combine(setup.Root, "application-data");
    string workspace = NiraWorkspaceLocation.Open(appDataRoot, null);
    Assert(workspace == Path.Combine(appDataRoot, "Workspace"),
        "Default workspace must be app-private, not the Windows Documents folder");
    Assert(Directory.Exists(workspace), "Default workspace must exist");
    Assert(!Directory.EnumerateFileSystemEntries(workspace).Any(),
        "Write-access probe must not leave files behind");
    return Task.CompletedTask;
}

static async Task WorkspaceCustomLocation()
{
    using var setup = new TestContext();
    string custom = Path.Combine(setup.Root, "chosen-workspace");
    Directory.CreateDirectory(custom);
    string existing = Path.Combine(custom, "existing.txt");
    await File.WriteAllTextAsync(existing, "KEEP THIS DATA");
    string selected = NiraWorkspaceLocation.Open(Path.Combine(setup.Root, "appdata"), custom);
    Assert(selected == Path.GetFullPath(custom), "Explicit workspace choice must be respected");
    Assert(await File.ReadAllTextAsync(existing) == "KEEP THIS DATA",
        "Existing workspace content must remain intact");
    Assert(Directory.EnumerateFiles(custom).Count() == 1,
        "Write probe must not leave any temporary files behind");
}

static async Task WorkspaceBlockedLocation()
{
    using var setup = new TestContext();
    string blocked = Path.Combine(setup.Root, "occupied-by-a-file");
    await File.WriteAllTextAsync(blocked, "ORIGINAL");
    bool explained = false;
    try { NiraWorkspaceLocation.Open(Path.Combine(setup.Root, "appdata"), blocked); }
    catch (IOException ex)
    {
        explained = ex.Message.Contains("NIRA_WORKSPACE", StringComparison.Ordinal) &&
            ex.Message.Contains(blocked, StringComparison.OrdinalIgnoreCase);
    }
    Assert(explained, "Unavailable explicit workspace must provide actionable diagnostics");
    Assert(await File.ReadAllTextAsync(blocked) == "ORIGINAL",
        "Rejected workspace must not alter existing data");
}

static Task WorkspaceRelativeOverride()
{
    using var setup = new TestContext();
    bool rejected = false;
    try { NiraWorkspaceLocation.Open(setup.Root, "relative\\not-absolute"); }
    catch (ArgumentException) { rejected = true; }
    Assert(rejected, "Relative workspace override must be rejected");
    return Task.CompletedTask;
}

static async Task OrdinaryConversation()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply("Hello! How can I help?", [])
    ]);
    Guid id = Guid.NewGuid();
    AgentRunResult result = await setup.Engine(model).RunAsync(id, "hi", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Ordinary chat must complete immediately");
    Assert(result.Message == "Hello! How can I help?", "Normal assistant text should be displayed");
    Assert(result.EvidenceIds.Count == 0, "A greeting must not invent action evidence");
    ConversationState saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.LastAnswer == result.Message, "Reply must survive restart");
    Assert(saved.Messages.Count(x => x.Role == "assistant") == 1,
        "The model's normal answer must not be rejected or duplicated");
    Assert(!saved.Messages.Any(x => x.Role == "system"),
        "No artificial repair prompt may be persisted in conversation history");
}

static async Task EmptyReplyRecovery()
{
    using var setup = new TestContext();
    int calls = 0;
    var model = new FakeModel(messages =>
    {
        calls++;
        if (calls == 1) return new ModelReply(null, []);
        Assert(messages.Any(x => x.Role == "system" &&
            x.Content?.Contains("neither user-visible text nor a tool call", StringComparison.Ordinal) == true),
            "An empty response should receive transient model-level recovery guidance");
        return new ModelReply("Ready.", []);
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Hello", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.Message == "Ready.",
        "One empty provider output should not break an otherwise valid turn");
    Assert(calls == 2, "Recovery must be bounded");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Messages.Count == 2 && saved.Messages[0].Role == "user" &&
        saved.Messages[1].Role == "assistant",
        "Transient recovery guidance must not corrupt durable conversation history");
}

static async Task ToolFreeRecovery()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, []), new ModelReply(null, []),
        new ModelReply("Hello from the ordinary chat endpoint.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "hi", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed,
        "An empty tool-enabled response must recover through tool-free chat");
    Assert(result.Message == "Hello from the ordinary chat endpoint.",
        "Recovery must return the provider's actual answer, not a hardcoded greeting");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 0 && result.EvidenceIds.Count == 0,
        "A tool-free fallback must not imply an external action occurred");
}

static async Task PersistentEmptyReply()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, []), new ModelReply(" ", []), new ModelReply(null, [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Describe the workspace", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused, "Missing visible output must not complete");
    Assert(result.Message.Contains("no user-visible answer", StringComparison.Ordinal),
        "The reason for the pause must be concrete");
    Assert(result.EvidenceIds.Count == 0, "No phantom action receipt");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Messages.All(x => x.Role == "user"), "No reasoning or fake assistant output persisted");
}

static async Task PlainReplyWithPendingPlan()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("plan1", "nira_plan", new { steps = new[] {
            new { description = "Inspect environment", successCriterion = "Inspect receipt" },
            new { description = "Verify results", successCriterion = "Readback receipt" }
        } })]),
        new ModelReply("I have a plan but have not completed it.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Investigate and verify", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused, "Plain text must not complete an open plan");
    Assert(result.EvidenceIds.Count == 0, "Planning must not create action evidence");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Plan.Count == 2 && saved.Plan.All(x => x.Status == "pending"),
        "Unfinished plan must survive the pause");
}

static async Task ReadThenPlainAnswer()
{
    using var setup = new TestContext();
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "facts.txt"), "NIRA_FACTS");
    var model = new FakeModel([
        new ModelReply(null, [Call("read1", "filesystem.read_text", new { path = "facts.txt" })]),
        new ModelReply("The file contains NIRA_FACTS.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Read facts.txt and summarize", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "A read tool should allow an ordinary final reply");
    Assert(result.Message.Contains("NIRA_FACTS", StringComparison.Ordinal), "Visible answer missing");
    Assert(result.EvidenceIds.Count == 0, "Read-only tools must not be treated as mutation evidence");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Succeeded,
        "Read action must be audited");
}

static async Task WirePlainText()
{
    using var handler = new FakeApiHandler("""
        {"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"Hello there!"}}]}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"), "mock", () => "test-key");
    ModelReply reply = await model.CompleteAsync([new ChatEntry("user", "hi")], [], default);
    Assert(reply.Content == "Hello there!" && reply.ToolCalls.Count == 0,
        "OpenAI-compatible plain content must be exposed to the engine");
    Assert(handler.Calls == 1, "One provider response expected");
    using var wireRequest = JsonDocument.Parse(handler.RequestBody!);
    Assert(!wireRequest.RootElement.TryGetProperty("tools", out _) &&
        !wireRequest.RootElement.TryGetProperty("tool_choice", out _),
        "Tool-free API requests must omit tools and tool_choice for provider compatibility");
}

static async Task WireContentParts()
{
    using var handler = new FakeApiHandler("""
        {"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":[{"type":"text","text":"Hello "},{"type":"text","text":"there!"}]}}]}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"), "mock", () => null);
    ModelReply reply = await model.CompleteAsync([new ChatEntry("user", "hi")], [], default);
    Assert(reply.Content == "Hello \nthere!" && reply.ToolCalls.Count == 0,
        "Visible text parts must be extracted; reasoning is not user content");
}

static Task CredentialEncrypted()
{
    using var setup = new TestContext();
    string appRoot = Path.Combine(setup.Root, "app-auth");
    var store = new NiraModelCredentialStore(appRoot, new Uri("https://example.invalid/v1/"));
    const string marker = "NIRA_TEST_PROVIDER_KEY_PRIVATE_4610";
    store.Save(marker);
    string[] files = Directory.GetFiles(Path.Combine(appRoot, "Credentials"), "*.dpapi");
    Assert(files.Length == 1, "Only one provider-scoped encrypted credential expected");
    Assert(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(files[0])).Contains(marker, StringComparison.Ordinal),
        "Credential plaintext leaked to disk");
    var reopened = new NiraModelCredentialStore(appRoot, new Uri("https://example.invalid/v1/"));
    Assert(reopened.ReadStored() == marker && reopened.Resolve() == marker,
        "DPAPI credential must persist and take priority over process environment");
    reopened.ClearStored();
    Assert(reopened.ReadStored() is null, "Clearing the stored credential must remove it");
    return Task.CompletedTask;
}

static Task CredentialEndpointIsolation()
{
    using var setup = new TestContext();
    string appRoot = Path.Combine(setup.Root, "app-auth");
    var provider1 = new NiraModelCredentialStore(appRoot, new Uri("https://one.example/v1/"));
    var provider2 = new NiraModelCredentialStore(appRoot, new Uri("https://two.example/v1/"));
    provider1.Save("NIRA_KEY_PROVIDER_ONE");
    Assert(provider2.ReadStored() is null, "Credentials must not cross provider endpoints");
    provider2.Save("NIRA_KEY_PROVIDER_TWO");
    Assert(provider1.ReadStored() == "NIRA_KEY_PROVIDER_ONE" &&
        provider2.ReadStored() == "NIRA_KEY_PROVIDER_TWO", "Independent provider keys must remain separate");
    return Task.CompletedTask;
}

static Task CredentialCorruptedRecord()
{
    using var setup = new TestContext();
    string appRoot = Path.Combine(setup.Root, "app-auth");
    var store = new NiraModelCredentialStore(appRoot, new Uri("https://example.invalid/v1/"));
    store.Save("ORIGINAL_KEY");
    string file = Directory.GetFiles(Path.Combine(appRoot, "Credentials"), "*.dpapi").Single();
    File.WriteAllBytes(file, new byte[48]);
    Assert(store.ReadStored() is null, "Invalid protected record must not be mistaken for a key");
    store.Save("REPLACEMENT_KEY");
    Assert(store.ReadStored() == "REPLACEMENT_KEY", "An invalid record must be recoverable");
    return Task.CompletedTask;
}

static async Task ProviderUnauthorized()
{
    using var handler = new ModelAuthHttpHandler(System.Net.HttpStatusCode.Unauthorized);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"),
        "mock", () => null);
    bool authenticated = false;
    try { await model.CompleteAsync([new ChatEntry("user", "hi")], [], default); }
    catch (ModelAuthenticationException error)
    { authenticated = error.Message.Contains("401", StringComparison.Ordinal); }
    Assert(authenticated, "HTTP 401 must be distinguishable from other HTTP provider failures");
    Assert(handler.RequestAuthorization is null, "No absent API key should be fabricated");
}

static async Task ProviderUsesSavedKey()
{
    using var setup = new TestContext();
    var endpoint = new Uri("https://example.invalid/v1/");
    var store = new NiraModelCredentialStore(Path.Combine(setup.Root, "auth"), endpoint);
    store.Save("NIRA_EXAMPLE_BEARER_KEY");
    using var handler = new FakeApiHandler("""
        {"choices":[{"message":{"role":"assistant","content":"Hi!"}}]}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, endpoint, "mock", store.Resolve);
    ModelReply reply = await model.CompleteAsync([new ChatEntry("user", "hi")], [], default);
    Assert(reply.Content == "Hi!", "Encrypted key should authenticate a normal chat response");
    Assert(handler.AuthorizationHeader == "Bearer NIRA_EXAMPLE_BEARER_KEY",
        "Provider must receive saved key in the Bearer header, never the request body");
    Assert(handler.RequestBody is not null &&
        !handler.RequestBody.Contains("NIRA_EXAMPLE_BEARER_KEY", StringComparison.Ordinal),
        "Credentials must not appear in the JSON request body");
}

static async Task CredentialRetryDoesNotDuplicate()
{
    using var setup = new TestContext();
    int calls = 0;
    var model = new FakeModel(_ => {
        calls++;
        if (calls == 1) throw new ModelAuthenticationException("HTTP 401");
        return new ModelReply("Hello from the live model.", []);
    });
    Guid conversation = Guid.NewGuid();
    var engine = setup.Engine(model);
    bool rejected = false;
    try { await engine.RunAsync(conversation, "hi", new FakeApprovals(false)); }
    catch (ModelAuthenticationException) { rejected = true; }
    Assert(rejected, "Simulated provider must reject the first key");
    var saved = (await setup.Store.LoadAsync(conversation, default))!;
    Assert(saved.Messages.Count(x => x.Role == "user") == 1,
        "User text must be checkpointed exactly once before auth failure");
    AgentRunResult recovered = await engine.RunAsync(conversation, null, new FakeApprovals(false));
    Assert(recovered.Status == ConversationStatus.Completed && recovered.Message == "Hello from the live model.",
        "Credential replacement must resume original objective");
    saved = (await setup.Store.LoadAsync(conversation, default))!;
    Assert(saved.Messages.Count(x => x.Role == "user") == 1,
        "Retry after HTTP 401 must not append the same message twice");
}

static async Task MutationPlainReplyNeedsEvidence()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages =>
    {
        rounds++;
        if (rounds == 1)
            return new ModelReply(null, [Call("write", "filesystem.write_text",
                new { path = "verified.txt", content = "Verified contents" })]);
        if (rounds == 2)
            return new ModelReply("I created the file.", []);
        Assert(messages.Any(m => m.Role == "system" &&
            m.Content?.Contains("recorded changed-state receipt ID", StringComparison.Ordinal) == true),
            "The engine must give transient, evidence-based completion guidance");
        string receipt = ReceiptIdForMutation(messages);
        return new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome = "action", answer = "The file was created and verified.",
            evidence_ids = new[] { receipt }
        })]);
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Create a file", new FakeApprovals(false));
    Assert(rounds == 3 && result.Status == ConversationStatus.Completed,
        "The model should correct an unverified plain answer without another user turn");
    Assert(result.EvidenceIds.Count == 1, "Changed-state receipt required");
    Assert(File.ReadAllText(Path.Combine(setup.Workspace, "verified.txt")) == "Verified contents",
        "The real file must exist");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Messages.All(m => m.Content != "I created the file."),
        "An unverified intermediate claim must not become part of the answer transcript");
}

static async Task MutationPlainReplyPauses()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("write", "filesystem.write_text",
            new { path = "pending.txt", content = "Persisted" })]),
        new ModelReply("Done!", []),
        new ModelReply("Completed!", []),
        new ModelReply("Success!", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Write and verify", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused, "Repeated unsupported claims must not complete");
    Assert(!result.Message.Contains("Success!", StringComparison.Ordinal),
        "Final output must not repeat an unverified model claim");
    Assert(File.Exists(Path.Combine(setup.Workspace, "pending.txt")),
        "Pausing must not undo or silently replay a completed operation");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Succeeded,
        "The receipt must be retained for future resume");
}

static async Task MutationMustFinishAsAction()
{
    using var setup = new TestContext();
    int calls = 0;
    var model = new FakeModel(messages =>
    {
        calls++;
        if (calls == 1)
            return new ModelReply(null, [Call("write", "filesystem.write_text",
                new { path = "effect.txt", content = "Actual" })]);
        string receipt = ReceiptIdForMutation(messages);
        if (calls == 2)
            return new ModelReply(null, [Call("invalid", "nira_finish", new {
                outcome = "answer", answer = "I created the file", evidence_ids = new[] { receipt }
            })]);
        Assert(messages.Any(x => x.Role == "tool" &&
            x.Content?.Contains("UNVERIFIED_EFFECT", StringComparison.Ordinal) == true),
            "The invalid informational finish must have a tool error receipt");
        return new ModelReply(null, [Call("valid", "nira_finish", new {
            outcome = "action", answer = "Created the file", evidence_ids = new[] { receipt }
        })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create file", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && calls == 3,
        "A model can repair its incorrect outcome but must not bypass mutation evidence");
}

static async Task ToolFailureCanRecover()
{
    using var setup = new TestContext();
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "exists.txt"), "ORIGINAL");
    int calls = 0;
    var model = new FakeModel(messages =>
    {
        calls++;
        if (calls == 1)
            return new ModelReply(null, [Call("attempt", "filesystem.write_text",
                new { path = "exists.txt", content = "Never overwrite" })]);
        if (calls == 2)
        {
            Assert(messages.Any(m => m.Role == "tool" &&
                m.Content?.Contains("ALREADY_EXISTS", StringComparison.Ordinal) == true),
                "The model should see why the first attempt failed");
            return new ModelReply(null, [Call("alternative", "filesystem.write_text",
                new { path = "alternative.txt", content = "RECOVERED" })]);
        }
        return new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome = "action", answer = "Created the alternative file",
            evidence_ids = new[] { ReceiptIdForMutation(messages) }
        })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Create a file without overwriting existing data", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && calls == 3,
        "Recoverable tool failure should allow an alternative action");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "exists.txt")) == "ORIGINAL",
        "Existing content must remain intact");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "alternative.txt")) == "RECOVERED",
        "Alternative result must actually exist");
}

static async Task UnfinishedPlanRejectsFinish()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("plan", "nira_plan", new { steps = new[] {
            new { description="Inspect workspace", successCriterion="Observed entries" },
            new { description="Verify result", successCriterion="Readback" }
        } })]),
        new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome="answer", answer="Task complete", evidence_ids=Array.Empty<string>()
        })]),
        new ModelReply(null, [Call("ask", "nira_ask_user", new {
            question="The task remains unfinished. Can I continue?"
        })])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Complete a multistep task", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.AwaitingUser, "An incomplete plan cannot finish");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Messages.Any(m => m.Role == "tool" &&
        m.Content?.Contains("PLAN_UNFINISHED", StringComparison.Ordinal) == true),
        "The model should receive the unfinished-plan error as a tool observation");
}


static async Task ProcessDenied()
{
    using var setup = new TestContext();
    string marker = Path.Combine(setup.Workspace, "must-not-create.txt");
    var model = new FakeModel([
        new ModelReply(null, [Call("run-denied", "process.run", new {
            executable = "powershell.exe", arguments = new[] {
                "-NoProfile", "-NonInteractive", "-Command",
                "$null = New-Item -ItemType File -LiteralPath 'must-not-create.txt'"
            }, workingDirectory = setup.Workspace
        })]),
        new ModelReply("The process was not approved or started.", [])
    ]);
    var answer = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Attempt to run a command", new FakeApprovals(false));
    Assert(answer.Status == ConversationStatus.Completed && !File.Exists(marker),
        "Process execution must require explicit approval even inside the workspace");
}

static async Task ProcessDotnetVersion()
{
    using var setup = new TestContext();
    ToolReceipt result = await InvokeProcessTool(setup, "dotnet", ["--version"]);
    Assert(result.Success && result.ChangedState, "Started process must have a journalable effects receipt");
    Assert(result.Data.GetProperty("exitCode").GetInt32() == 0 &&
           !string.IsNullOrWhiteSpace(result.Data.GetProperty("stdout").GetString()),
           "Separate argv process invocation must capture the actual dotnet version");
    Assert(result.Data.GetProperty("workingDirectory").GetString() == setup.Workspace,
           "Process must run with the selected workspace working directory");
}

static Task ProcessWorkingDirectoryScope()
{
    using var setup = new TestContext();
    var tool = new ProcessRunTool(new WorkspacePathResolver(setup.Workspace));
    bool rejected = false;
    try { tool.GetTarget(JsonSerializer.SerializeToElement(new {
        executable = "dotnet", arguments = new[] { "--version" },
        workingDirectory = setup.Root
    })); }
    catch (ArgumentException) { rejected = true; }
    Assert(rejected, "A process working directory outside the workspace must be rejected");
    return Task.CompletedTask;
}

static async Task ProcessSecretsExcluded()
{
    using var setup = new TestContext();
    const string marker = "NIRA_TEST_KEY_MUST_NOT_REACH_CHILD_98211";
    string? before = Environment.GetEnvironmentVariable("NIRA_API_KEY");
    try
    {
        Environment.SetEnvironmentVariable("NIRA_API_KEY", marker);
        ToolReceipt result = await InvokeProcessTool(setup, "powershell.exe", [
            "-NoProfile", "-NonInteractive", "-Command",
            "if ($env:NIRA_API_KEY) { 'LEAK' } else { 'NOT_PRESENT' }"
        ]);
        Assert(result.Success && result.Data.GetProperty("exitCode").GetInt32() == 0,
               "Secret-environment probe must execute");
        string output = result.Data.GetProperty("stdout").GetString() ?? "";
        Assert(output.Contains("NOT_PRESENT", StringComparison.Ordinal) &&
               !output.Contains("LEAK", StringComparison.Ordinal),
               "Model-run child process must not inherit NIRA_API_KEY");
    }
    finally { Environment.SetEnvironmentVariable("NIRA_API_KEY", before); }
}

static async Task ProcessTimeout()
{
    using var setup = new TestContext();
    ToolReceipt result = await InvokeProcessTool(setup, "powershell.exe", [
        "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 15"
    ], timeout: 1);
    Assert(result.Success && result.ChangedState &&
           result.Data.GetProperty("timedOut").GetBoolean(),
           "Timed-out processes must be terminated and recorded as potentially state-changing");
}

static async Task ProcessNonzeroExit()
{
    using var setup = new TestContext();
    ToolReceipt result = await InvokeProcessTool(setup, "powershell.exe", [
        "-NoProfile", "-NonInteractive", "-Command", "exit 7"
    ]);
    Assert(result.Success && result.ChangedState &&
           result.Data.GetProperty("exitCode").GetInt32() == 7,
           "Nonzero exit is an observed process result, not proof of zero side effects");
}

static async Task ProcessAgentExecution()
{
    using var setup = new TestContext();
    var model = new FakeModel(messages => {
        string? receipt = messages.Where(x => x.Role == "tool" && x.Content is not null)
            .Select(x => x.Content).FirstOrDefault(x => x!.Contains("receiptId", StringComparison.Ordinal));
        if (receipt is null)
            return new ModelReply(null, [Call("process1", "process.run", new {
                executable = "dotnet", arguments = new[] { "--version" },
                workingDirectory = setup.Workspace
            })]);
        using var data = JsonDocument.Parse(receipt);
        string receiptId = data.RootElement.GetProperty("receiptId").GetString()!;
        return new ModelReply(null, [Call("finished", "nira_finish", new {
            outcome = "action", answer = "Executed dotnet --version with recorded output.",
            evidence_ids = new[] { receiptId }
        })]);
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Execute the installed dotnet CLI version check", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
           "An approved subprocess must finish only with its changed-state receipt");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Succeeded,
           "Subprocess execution must be durably recorded");
}

static Task<ToolReceipt> InvokeProcessTool(TestContext context, string executable,
    string[] arguments, int timeout = 30)
{
    var tool = new ProcessRunTool(new WorkspacePathResolver(context.Workspace));
    JsonElement request = JsonSerializer.SerializeToElement(new {
        executable, arguments, workingDirectory = context.Workspace,
        timeoutSeconds = timeout
    });
    return tool.ExecuteAsync(request, default);
}

static string ReceiptIdForMutation(IReadOnlyList<ChatEntry> messages)
{
    foreach (var message in messages.Where(m => m.Role == "tool" && m.Content is not null))
    {
        using var doc = JsonDocument.Parse(message.Content!);
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("ChangedState", out var changed) &&
            changed.ValueKind == JsonValueKind.True &&
            root.TryGetProperty("receiptId", out var id))
            return id.GetString()!;
    }
    throw new InvalidOperationException("No observed changed-state receipt");
}

static async Task WireEndToEndToolPipeline()
{
    using var setup = new TestContext();
    using var handler = new WireToolPipelineHandler(setup.Workspace);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"),
        "mock", () => "wire-test-key");
    Guid conversation = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(conversation,
        "Inspect the workspace, create a file, read it back and verify its hash.",
        new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed,
        "Full provider-wire sequence must finish with real receipts");
    Assert(handler.RequestCount == 6 && handler.ReadbackSeen && handler.HashSeen,
        "All six real HTTP wire turns must be processed with observed outputs");
    string file = Path.Combine(setup.Workspace, WireToolPipelineHandler.FileName);
    Assert(File.Exists(file) && await File.ReadAllTextAsync(file) == WireToolPipelineHandler.FileContent,
        "The actual file must be created and readable");
    var persisted = (await setup.Store.LoadAsync(conversation, default))!;
    Assert(persisted.Operations.Count == 5 && persisted.Operations.All(o =>
        o.Status == OperationStatus.Succeeded), "Every real operation must be durably audited");
    Assert(result.EvidenceIds.Count == 1, "Only the file mutation counts as changed-state evidence");
}

static async Task DirectoryCreateObserved()
{
    using var setup = new TestContext();
    var model = new FakeModel(messages => {
        if (!messages.Any(m => m.Role == "tool"))
            return new ModelReply(null, [Call("mkdir", "filesystem.mkdir", new { path = "new-folder" })]);
        return new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome = "action", answer = "Created the directory.",
            evidence_ids = new[] { ReceiptIdForMutation(messages) }
        })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create a directory", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
        "Directory creation must finish with observed mutation receipt");
    Assert(Directory.Exists(Path.Combine(setup.Workspace, "new-folder")), "Actual new directory missing");
}

static async Task DirectoryExistingPreserved()
{
    using var setup = new TestContext();
    string occupied = Path.Combine(setup.Workspace, "occupied");
    await File.WriteAllTextAsync(occupied, "PROTECT EXISTING DATA");
    var model = new FakeModel([
        new ModelReply(null, [Call("mkdir", "filesystem.mkdir", new { path = "occupied" })]),
        new ModelReply("The name already exists, so it was not replaced.", [])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create occupied directory", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Failed creation should permit an honest answer");
    Assert(await File.ReadAllTextAsync(occupied) == "PROTECT EXISTING DATA", "Existing file must remain untouched");
}

static async Task DirectoryOutsideDenied()
{
    using var setup = new TestContext();
    string outside = Path.Combine(setup.Root, "not-allowed-directory");
    var model = new FakeModel([
        new ModelReply(null, [Call("mkdir", "filesystem.mkdir", new { path = outside })]),
        new ModelReply("The external directory was not created because consent was denied.", [])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create an external directory", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && !Directory.Exists(outside),
        "Workspace authority must prevent external mkdir without authorization");
}

static async Task EditTextPreservesBytes()
{
    using var setup = new TestContext();
    string file = Path.Combine(setup.Workspace, "existing-source.txt");
    byte[] before = [0xEF, 0xBB, 0xBF, ..System.Text.Encoding.UTF8.GetBytes("first\r\nSECOND\r\nthird\r\n")];
    await File.WriteAllBytesAsync(file, before);
    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(before)).ToLowerInvariant();
    var model = new FakeModel(messages => {
        if (!messages.Any(m => m.Role == "tool"))
            return new ModelReply(null, [Call("edit", "filesystem.edit_text", new {
                path = "existing-source.txt", expectedSha256 = hash,
                oldText = "SECOND", newText = "CHANGED"
            })]);
        return new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome = "action", answer = "Verified revision was published.",
            evidence_ids = new[] { ReceiptIdForMutation(messages) }
        })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Revise the existing text", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
        "Versioned edit must have a verified receipt");
    byte[] expected = [0xEF, 0xBB, 0xBF, ..System.Text.Encoding.UTF8.GetBytes("first\r\nCHANGED\r\nthird\r\n")];
    Assert((await File.ReadAllBytesAsync(file)).SequenceEqual(expected), "BOM, CRLF and unaffected bytes must be preserved");
    Assert(!Directory.EnumerateFiles(setup.Workspace, ".nira-edit-*").Any(),
        "Successful edit must not leave temporary or rollback files");
}

static async Task EditTextStaleHash()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "guarded.txt");
    await File.WriteAllTextAsync(path, "the actual version");
    string staleHash = new string('0', 64);
    var model = new FakeModel([
        new ModelReply(null, [Call("edit", "filesystem.edit_text", new {
            path = "guarded.txt", expectedSha256 = staleHash,
            oldText = "actual", newText = "incorrect"
        })]),
        new ModelReply("The hash did not match, so the source was left unchanged.", [])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Edit a stale revision", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Stale write should allow honest reporting");
    Assert(await File.ReadAllTextAsync(path) == "the actual version", "Stale edits must not touch the current version");
}

static async Task EditTextAmbiguous()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "ambiguous.txt");
    await File.WriteAllTextAsync(path, "repeat / repeat");
    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        await File.ReadAllBytesAsync(path))).ToLowerInvariant();
    var model = new FakeModel([
        new ModelReply(null, [Call("edit", "filesystem.edit_text", new {
            path = "ambiguous.txt", expectedSha256 = hash,
            oldText = "repeat", newText = "changed"
        })]),
        new ModelReply("Multiple matching spans were found; no edit was made.", [])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Edit unique text", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Ambiguous edit should be reported accurately");
    Assert(await File.ReadAllTextAsync(path) == "repeat / repeat", "Ambiguous edits must preserve input");
}

static async Task EditTextApprovedOutside()
{
    using var setup = new TestContext();
    string outside = Path.Combine(setup.Root, "external-approved.txt");
    await File.WriteAllTextAsync(outside, "BEFORE");
    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        await File.ReadAllBytesAsync(outside))).ToLowerInvariant();
    var model = new FakeModel(messages => {
        if (!messages.Any(m => m.Role == "tool"))
            return new ModelReply(null, [Call("edit", "filesystem.edit_text", new {
                path = outside, expectedSha256 = hash, oldText = "BEFORE", newText = "AFTER"
            })]);
        return new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome = "action", answer = "User-approved outside edit was verified.",
            evidence_ids = new[] { ReceiptIdForMutation(messages) }
        })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Edit this approved outside file", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
        "Outside edit requires explicit approval and receipts");
    Assert(await File.ReadAllTextAsync(outside) == "AFTER", "Approved outside edit was not published");
}

static async Task WireEditTextPipeline()
{
    using var setup = new TestContext();
    string file = Path.Combine(setup.Workspace, "wire-edit.txt");
    await File.WriteAllTextAsync(file, "V1 INITIAL\n");
    using var handler = new WireEditPipelineHandler(file);
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => "integration-key");
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Hash, edit and read back the existing file", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
        "HTTP tool pipeline edit must finish with changed-state evidence");
    Assert(handler.Rounds == 4 && handler.ReadbackVerified,
        "Provider transcript must include the real readback and a verified finish");
    Assert(await File.ReadAllTextAsync(file) == "V2 UPDATED\n", "Actual file must contain the edited content");
}

static ModelToolCall Call(string id, string name, object args) =>
    new(id, name, JsonSerializer.Serialize(args));
static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed class TestContext : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "nira-kernel-" + Guid.NewGuid().ToString("N"));
    public string Workspace { get; }
    public string Database => Path.Combine(Root, "test.db");
    public SqliteConversationStore Store { get; }
    private readonly HttpClient _http = new();
    public TestContext()
    {
        Workspace = Path.Combine(Root, "workspace");
        Directory.CreateDirectory(Workspace);
        Store = new SqliteConversationStore(Database);
    }
    public NiraEngine Engine(IChatModel model) => new(model,
        NiraStandardTools.Create(Workspace, Store, _http), Store,
        new WorkspaceAuthority(Workspace));
    public void Dispose()
    {
        _http.Dispose();
        // SQLite pooling may hold database handles; clearing ensures cleanup on Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
    }
}

internal sealed class FakeApprovals(bool approved) : IApprovalBroker
{
    public Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct) => Task.FromResult(approved);
}

internal sealed class FakeModel : IChatModel
{
    private readonly Queue<ModelReply>? _script;
    private readonly Func<IReadOnlyList<ChatEntry>, ModelReply>? _respond;
    public FakeModel(IEnumerable<ModelReply> replies) { _script = new Queue<ModelReply>(replies); }
    public FakeModel(Func<IReadOnlyList<ChatEntry>, ModelReply> responder) { _respond = responder; }
    public Task<ModelReply> CompleteAsync(IReadOnlyList<ChatEntry> messages,
        IReadOnlyList<ToolDescriptor> tools, CancellationToken ct)
    {
        if (_respond is not null) return Task.FromResult(_respond(messages));
        if (_script is null || _script.Count == 0) throw new InvalidOperationException("Fake model out of script");
        return Task.FromResult(_script.Dequeue());
    }
}

internal sealed class FakeApiHandler(string responseBody) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public string? RequestBody { get; private set; }
    public string? AuthorizationHeader { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        AuthorizationHeader = request.Headers.Authorization?.ToString();
        RequestBody = request.Content is null ? null :
            await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>
/// Exercises the real OpenAI-compatible HTTP transport, model tool calls,
/// matching tool_call_id transcript, workspace writes, reads and hash results.
/// This is a deterministic wire-protocol integration test, not a live Ollama test.
/// </summary>
internal sealed class WireToolPipelineHandler(string workspace) : HttpMessageHandler
{
    public const string FileName = "wire-created.txt";
    public const string FileContent = "NIRA_TOOL_PIPELINE_CONFIRMED";
    public int RequestCount { get; private set; }
    public bool ReadbackSeen { get; private set; }
    public bool HashSeen { get; private set; }
    private string? _mutationId;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.ToString() != "Bearer wire-test-key")
            throw new InvalidOperationException("Wire request lost the Bearer header");
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        JsonElement root = json.RootElement;
        var wire = root.GetProperty("messages").EnumerateArray().ToArray();
        int step = RequestCount++;
        var toolMessages = wire.Where(m => m.GetProperty("role").GetString() == "tool").ToArray();
        if (toolMessages.Length != step)
            throw new InvalidOperationException("Previous tool observations missing from provider transcript");
        if (step > 0)
        {
            JsonElement last = toolMessages[^1];
            string? callId = last.GetProperty("tool_call_id").GetString();
            var declared = wire.Where(m => m.GetProperty("role").GetString() == "assistant" &&
                m.TryGetProperty("tool_calls", out _))
                .SelectMany(m => m.GetProperty("tool_calls").EnumerateArray().ToArray())
                .Any(c => c.GetProperty("id").GetString() == callId);
            if (!declared) throw new InvalidOperationException("Tool response ID was not declared by assistant");
        }
        if (step == 3)
        {
            using var receipt = JsonDocument.Parse(toolMessages[^1].GetProperty("content").GetString()!);
            if (receipt.RootElement.GetProperty("ChangedState").ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("The write must have an actual changed-state receipt");
            _mutationId = receipt.RootElement.GetProperty("receiptId").GetString();
        }
        if (step == 4)
        {
            using var read = JsonDocument.Parse(toolMessages[^1].GetProperty("content").GetString()!);
            ReadbackSeen = read.RootElement.GetProperty("Data").GetProperty("content").GetString() == FileContent;
            if (!ReadbackSeen) throw new InvalidOperationException("Readback did not match the written bytes");
        }
        if (step == 5)
        {
            using var hash = JsonDocument.Parse(toolMessages[^1].GetProperty("content").GetString()!);
            string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(FileContent))).ToLowerInvariant();
            HashSeen = hash.RootElement.GetProperty("Data").GetProperty("sha256").GetString() == expected;
            if (!HashSeen) throw new InvalidOperationException("Independent SHA-256 did not match");
        }
        object parameters = step switch
        {
            0 => new { },
            1 => new { path = workspace },
            2 => new { path = FileName, content = FileContent },
            3 => new { path = FileName },
            4 => new { path = FileName },
            5 => new { outcome = "action", answer = "Created, read and hash-verified a file.",
                evidence_ids = new[] { _mutationId ?? throw new InvalidOperationException("No mutation receipt") } },
            _ => throw new InvalidOperationException("Unexpected HTTP model round")
        };
        string name = step switch
        {
            0 => "workspace.info", 1 => "filesystem.list", 2 => "filesystem.write_text",
            3 => "filesystem.read_text", 4 => "filesystem.sha256", 5 => "nira_finish",
            _ => throw new InvalidOperationException("Unexpected model tool")
        };
        string body = JsonSerializer.Serialize(new {
            choices = new[] { new { message = new { role = "assistant", content = (string?)null,
                tool_calls = new[] { new { id = "wire-" + step, type = "function",
                    function = new { name, arguments = JsonSerializer.Serialize(parameters) } } }
            } } }
        });
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class WireEditPipelineHandler(string path) : HttpMessageHandler
{
    public int Rounds { get; private set; }
    public bool ReadbackVerified { get; private set; }
    private string? _hash;
    private string? _editReceiptId;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.ToString() != "Bearer integration-key")
            throw new InvalidOperationException("Authorization header lost in edit wire test");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        JsonElement[] messages = body.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        int round = Rounds++;
        JsonElement[] tools = messages.Where(m => m.GetProperty("role").GetString() == "tool").ToArray();
        if (tools.Length != round)
            throw new InvalidOperationException("Tool observation count mismatches provider turns");
        if (round == 1)
        {
            using var receipt = JsonDocument.Parse(tools[^1].GetProperty("content").GetString()!);
            _hash = receipt.RootElement.GetProperty("Data").GetProperty("sha256").GetString();
            if (_hash is null) throw new InvalidOperationException("Hash receipt missing");
        }
        if (round == 2)
        {
            using var receipt = JsonDocument.Parse(tools[^1].GetProperty("content").GetString()!);
            if (receipt.RootElement.GetProperty("ChangedState").ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("Edit did not yield a changed-state receipt");
            _editReceiptId = receipt.RootElement.GetProperty("receiptId").GetString();
        }
        if (round == 3)
        {
            using var receipt = JsonDocument.Parse(tools[^1].GetProperty("content").GetString()!);
            ReadbackVerified = receipt.RootElement.GetProperty("Data").GetProperty("content").GetString() == "V2 UPDATED\n";
            if (!ReadbackVerified) throw new InvalidOperationException("Readback mismatch");
        }
        string name = round switch {
            0 => "filesystem.sha256", 1 => "filesystem.edit_text",
            2 => "filesystem.read_text", 3 => "nira_finish",
            _ => throw new InvalidOperationException("Unexpected edit wire round")
        };
        object args = round switch {
            0 => new { path },
            1 => new { path, expectedSha256 = _hash, oldText = "V1 INITIAL", newText = "V2 UPDATED" },
            2 => new { path },
            3 => new { outcome = "action", answer = "Edited file and verified readback.",
                evidence_ids = new[] { _editReceiptId ?? throw new InvalidOperationException("Edit receipt not observed") } },
            _ => throw new InvalidOperationException("Unexpected edit wire round")
        };
        string serialized = JsonSerializer.Serialize(new {
            choices = new[] { new { message = new { role = "assistant", content = (string?)null,
                tool_calls = new[] { new { id = "edit-wire-" + round, type = "function",
                    function = new { name, arguments = JsonSerializer.Serialize(args) } } }
            } } }
        });
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(serialized, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class ModelAuthHttpHandler(System.Net.HttpStatusCode code) : HttpMessageHandler
{
    public string? RequestAuthorization { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestAuthorization = request.Headers.Authorization?.ToString();
        return Task.FromResult(new HttpResponseMessage(code));
    }
}

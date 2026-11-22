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
    ("mutation-plain-final-attaches-host-receipt", MutationPlainReplyNeedsEvidence),
    ("mutation-plain-final-does-not-loop", MutationPlainReplyPauses),
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
    ("process-agent-reports-approved-execution-evidence", ProcessAgentExecution),
    ("process-plain-final-host-attests-stdout-and-exit", ProcessPlainFinalEvidence),
    ("mutation-after-recoverable-failure-attests-only-success", PlainFinalRecoverableFailure),
    ("mutation-plain-final-refuses-unfinished-plan", MutationPlainPendingPlan),
    ("uncertain-operation-cannot-plain-complete", UnknownOperationPlainFinal),
    ("tool-approval-question-routes-to-registered-action", ToolApprovalQuestionRoutesToAction),
    ("outside-write-denial-cannot-be-replaced-by-chat-consent", DeniedActionCannotBeBypassed),
    ("legitimate-missing-detail-still-asks-user", LegitimateMissingDetailQuestion),
    ("repeated-permission-probes-pause-without-user-interruption", RepeatedPermissionProbesPause),
    ("http-permission-question-gate-preserves-tool-transcript", HttpPermissionQuestionGate),
    ("process-inherits-safe-windows-os-identity", ProcessInheritsOsIdentity),
    ("explicit-lowercase-yes-authorizes-only-yes", ExplicitYesVariants),
    ("denied-process-pauses-with-no-repeat", DeniedProcessPausesWithoutReplay),
    ("plain-permission-question-recovers-to-process-tool", PlainPermissionRecoversToProcess),
    ("repeated-plain-permission-questions-pause", RepeatedPlainPermissionPauses),
    ("structured-finish-cannot-ask-tool-permission", StructuredFinishPermissionRejected),
    ("denial-after-write-preserves-partial-evidence", DenialAfterWritePreservesReceipt),
    ("permission-question-after-success-does-not-rerun", ObservedProcessNotRepeatedOnPermissionReply),
    ("tool-free-fallback-permission-prose-cannot-complete", ToolFreePermissionFallbackPauses),
    ("permission-probe-counter-resets-after-observed-tool", PermissionProbeCounterResetsAfterTool),
    ("browser-rejects-http-and-credentials", BrowserRejectsNonHttpsAndCredentials),
    ("browser-rejects-internal-hosts-and-ports", BrowserRejectsInternalHostAndPorts),
    ("browser-dns-address-filter-blocks-private", BrowserPublicDnsAddressGuards),
    ("browser-origin-and-method-routing", BrowserSameHostAndMethodEnforced),
    ("browser-tool-schema-is-strict", BrowserInspectSchemaIsStrict),
    ("browser-approval-denial-never-visits", BrowserDenialPreventsVisit),
    ("browser-approved-page-observation-is-audited", BrowserApprovedObservationIsAudited),
    ("browser-cross-origin-redirect-refused", BrowserRedirectOutsideApprovedOriginRejected),
    ("browser-accepts-public-url-and-dns", BrowserValidPublicAddressAndUrl),
    ("browser-http-wire-tool-transcript", BrowserHttpWireProtocolRoundTrip),
    ("browser-elements-disclose-metadata-not-values", BrowserElementsMetadataOnly),
    ("browser-link-schema-requires-exact-approved-url", BrowserLinkStrictSchema),
    ("browser-link-target-requires-valid-same-host-url", BrowserLinkTargetGuard),
    ("browser-link-denied-before-visiting", BrowserLinkDenialNeverVisits),
    ("browser-link-approved-two-page-observation", BrowserLinkApprovedTwoPageObservation),
    ("browser-link-changed-href-never-visits-destination", BrowserLinkChangedHrefStops),
    ("browser-link-missing-index-never-visits-destination", BrowserLinkMissingIndexStops),
    ("browser-link-source-redirect-blocked", BrowserLinkSourceRedirectStopped),
    ("browser-link-target-redirect-blocked", BrowserLinkDestinationRedirectStopped),
    ("browser-link-http-wire-receipt-reaches-model", BrowserLinkWireReceipt),
    ("browser-follow-permission-question-routes-to-host", BrowserFollowPermissionRoutes),
    ("browser-session-schema-and-destination-guards", SessionSchemasAndDestinations),
    ("browser-session-open-denial-prevents-launch", SessionOpenDenied),
    ("browser-session-open-approved-records-receipt", SessionOpenApproved),
    ("browser-session-inspection-reuses-current-page", SessionInspectUsesExisting),
    ("browser-session-follow-denied-before-navigation", SessionFollowDenied),
    ("browser-session-follow-recorded-after-approval", SessionFollowApproved),
    ("browser-session-link-changed-refused", SessionChangedLinkRefused),
    ("browser-session-cross-host-approval-denied", SessionCrossHostRejected),
    ("browser-session-close-disposes-and-expires", SessionCloseAndExpired),
    ("browser-text-entry-dom-index-visible", TextEntryDomIndexVisible),
    ("browser-text-entry-target-validation", TextEntryTargetValidation),
    ("browser-text-entry-is-dangerous", TextEntryDangerousRisk),
    ("browser-text-entry-denial-prevents-fill", TextEntryDenied),
    ("browser-text-entry-approved-receipt-redacted", TextEntryApprovedAndRedacted),
    ("browser-text-entry-stale-label-refused", TextEntryStaleLabel),
    ("browser-text-entry-password-and-disabled-refused", TextEntryPasswordAndDisabled),
    ("browser-text-entry-close-prevents-retry", TextEntryAfterClose),
    ("browser-text-entry-second-fill-refused", TextEntrySecondFillRefused),
    ("browser-text-entry-uncertain-effect-reconciled", TextEntryUncertainReconciliation),
    ("browser-button-click-strict-schema", ButtonClickStrictSchema),
    ("browser-button-click-target-guards", ButtonClickTargetGuards),
    ("browser-button-click-dangerous-approval-details", ButtonClickDangerousApproval),
    ("browser-button-click-denial-prevents-action", ButtonClickDenied),
    ("browser-button-click-approved-records-receipt", ButtonClickApproved),
    ("browser-button-click-stale-metadata-refused", ButtonClickStaleMetadata),
    ("browser-button-click-submit-and-disabled-refused", ButtonClickSubmitAndDisabled),
    ("browser-button-click-after-close-refused", ButtonClickClosedSession),
    ("browser-button-click-only-once-per-session", ButtonClickOnceOnly),
    ("browser-button-click-uncertain-effect-reconciled", ButtonClickUncertainReconciliation),
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
    Assert(result.Status == ConversationStatus.Paused && result.Message.Contains("Not executed", StringComparison.Ordinal),
        "A declined outside write must pause immediately without another model round");
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
        return new ModelReply("I created the file.", []);
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Create a file", new FakeApprovals(false));
    Assert(rounds == 2 && result.Status == ConversationStatus.Completed,
        "A normal final answer after verified write must finish without a special tool roundtrip");
    Assert(result.EvidenceIds.Count == 1, "Changed-state receipt must be attached by the host");
    Assert(result.Message.Contains("filesystem.write_text", StringComparison.Ordinal) &&
           result.Message.Contains(result.EvidenceIds[0], StringComparison.Ordinal) &&
           result.Message.Contains("verified.txt", StringComparison.Ordinal),
        "Final answer must include host-attested tool identity, receipt ID, and saved location");
    Assert(File.ReadAllText(Path.Combine(setup.Workspace, "verified.txt")) == "Verified contents",
        "The real file must exist");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Status == ConversationStatus.Completed && state.LastAnswer == result.Message,
        "The host-attested answer must be durably checkpointed");
}

static async Task MutationPlainReplyPauses()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1)
            return new ModelReply(null, [Call("write", "filesystem.write_text",
                new { path = "pending.txt", content = "Persisted" })]);
        if (rounds == 2) return new ModelReply("Created the file.", []);
        throw new InvalidOperationException("A third model request is an unnecessary retry");
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Write and verify", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && rounds == 2,
        "An already observed write must not trigger repetitive completion rounds");
    Assert(result.EvidenceIds.Count == 1 &&
           result.Message.Contains("Host-recorded execution evidence:", StringComparison.Ordinal),
        "Host evidence must accompany model text");
    Assert(File.Exists(Path.Combine(setup.Workspace, "pending.txt")),
        "Completing must not undo or silently replay an observed operation");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Succeeded,
        "The operation must still be durably audited");
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
    Assert(answer.Status == ConversationStatus.Paused && !File.Exists(marker),
        "A declined process must be paused without executing or retrying it");
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

static async Task ProcessPlainFinalEvidence()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1)
            return new ModelReply(null, [Call("process1", "process.run", new {
                executable = "dotnet", arguments = new[] { "--version" },
                workingDirectory = setup.Workspace
            })]);
        if (rounds == 2)
            return new ModelReply("The dotnet version command returned a result.", []);
        throw new InvalidOperationException("Do not re-execute the process for completion formatting");
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Check installed SDK version", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Completed && rounds == 2,
        "An observed approved process must finish after a normal final text answer");
    Assert(result.EvidenceIds.Count == 1 &&
           result.Message.Contains(result.EvidenceIds[0], StringComparison.Ordinal) &&
           result.Message.Contains("exitCode: 0", StringComparison.Ordinal) &&
           result.Message.Contains("stdout:", StringComparison.Ordinal),
        "Host must report real stdout, exit code, and audited receipt independent of model text");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Status == ConversationStatus.Completed,
        "A process must execute once and its completion must persist");
}

static async Task PlainFinalRecoverableFailure()
{
    using var setup = new TestContext();
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "exists.txt"), "ORIGINAL");
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1) return new ModelReply(null, [Call("failed", "filesystem.write_text",
            new { path = "exists.txt", content = "new" })]);
        if (rounds == 2) return new ModelReply(null, [Call("success", "filesystem.write_text",
            new { path = "new-file.txt", content = "new" })]);
        return new ModelReply("Created the replacement file without overwriting.", []);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Create an unused file", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
        "Only successful effects receive completion evidence");
    Assert(result.Message.Contains("new-file.txt", StringComparison.Ordinal) &&
           !result.Message.Contains("exists.txt", StringComparison.Ordinal),
        "Failed tool attempts cannot appear as successful host-attested operations");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "exists.txt")) == "ORIGINAL",
        "The original file must be preserved");
}

static async Task MutationPlainPendingPlan()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("plan", "nira_plan", new { steps = new[] {
            new { description="Create a file", successCriterion="File exists" },
            new { description="Verify final content", successCriterion="Readback observed" }
        } })]),
        new ModelReply(null, [Call("write", "filesystem.write_text",
            new { path = "pending-plan.txt", content = "data" })]),
        new ModelReply("Done!", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Create and verify", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused,
        "Host must reject a plain final when recorded plan steps are unfinished");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Succeeded &&
           saved.Status == ConversationStatus.Paused,
        "The actual write must persist without falsely completing the plan");
}

static async Task UnknownOperationPlainFinal()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var state = new ConversationState { Id=id, Objective="Report prior action", Status=ConversationStatus.Paused };
    state.Operations.Add(new AgentOperation {
        Id="uncertain", ToolName="process.run", Status=OperationStatus.Unknown,
        ArgumentsJson="{}"
    });
    await setup.Store.SaveAsync(state, default);
    var result = await setup.Engine(new FakeModel([
        new ModelReply("Completed!", [])
    ])).RunAsync(id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.NeedsReconciliation,
        "Unknown process effects must never be converted into trusted completion");
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
    Assert(result.Status == ConversationStatus.Paused && !Directory.Exists(outside),
        "Workspace authority must halt denied external mkdir without retrying");
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

static async Task ToolApprovalQuestionRoutesToAction()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("permission", "nira_ask_user", new {
            question = "May I create a directory and write source code in the workspace?" })]),
        new ModelReply(null, [Call("write", "filesystem.write_text", new {
            path = "routed.txt", content = "ROUTED_BY_HOST" })]),
        new ModelReply("File was created through the registered tool.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Create a new file", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Model-only approval must not interrupt task");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "routed.txt")) == "ROUTED_BY_HOST",
        "Registered workspace tool must perform the actual action");
    Assert(result.EvidenceIds.Count == 1, "Actual file write must have one host receipt");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.PendingQuestion is null, "Permission question must never be displayed to user");
    Assert(saved.Messages.Any(x => x.Role == "tool" &&
        x.Content?.Contains("USE_HOST_TOOL_APPROVAL", StringComparison.Ordinal) == true),
        "The model must receive actionable, auditable tool feedback");
}

static async Task DeniedActionCannotBeBypassed()
{
    using var setup = new TestContext();
    string outside = Path.Combine(setup.Root, "unapproved-outside.txt");
    var model = new FakeModel([
        new ModelReply(null, [Call("write", "filesystem.write_text", new {
            path = outside, content = "FORBIDDEN" })]),
        new ModelReply(null, [Call("permission", "nira_ask_user", new {
            question = "May I write to the outside directory instead?" })]),
        new ModelReply("The operation was not approved; I did not write the file.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Write this file outside the workspace only with approval", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused, "A declined outside write must stop model execution");
    Assert(!File.Exists(outside) && result.EvidenceIds.Count == 0,
        "A conversational request must not bypass the denied host operation");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Rejected,
        "Operation must remain rejected after attempted conversational approval");
}

static async Task LegitimateMissingDetailQuestion()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("clarify", "nira_ask_user", new {
            question = "Can you tell me which existing folder contains the project?" })])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Inspect the project in my selected folder", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.AwaitingUser,
        "A request for truly missing information must still reach the user");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.PendingQuestion?.Contains("which existing folder", StringComparison.Ordinal) == true,
        "Actual clarification must be preserved and resumable");
}

static async Task RepeatedPermissionProbesPause()
{
    using var setup = new TestContext();
    var responses = Enumerable.Range(0, 24).Select(i =>
        new ModelReply(null, [Call("permission" + i, "nira_ask_user", new {
            question = "May I execute the next registered tool?" })]));
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(new FakeModel(responses)).RunAsync(id,
        "Run a previously described task", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused,
        "A looping provider must pause safely rather than ask permission indefinitely");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.PendingQuestion is null && saved.Operations.Count == 0,
        "Repeated approval probes may neither interrupt the user nor run tools");
    Assert(saved.Messages.Count(x => x.Role == "tool" &&
        x.Content?.Contains("USE_HOST_TOOL_APPROVAL", StringComparison.Ordinal) == true) == 24,
        "Every repeated probe should receive deterministic tool feedback");
}

static async Task HttpPermissionQuestionGate()
{
    using var setup = new TestContext();
    using var handler = new WirePermissionGateHandler();
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"),
        "mock", () => "wire-auth");
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Describe the current workspace using available tools", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.Message.Contains("Workspace inspected"),
        "HTTP transcript should recover from a conversational permission probe");
    Assert(handler.Rounds == 3 && handler.HostDenialObserved,
        "Real transport must return the host guidance to the provider before tool execution");
    Assert(result.EvidenceIds.Count == 0,
        "Read-only workspace information must not fabricate mutation evidence");
}

static async Task ProcessInheritsOsIdentity()
{
    using var setup = new TestContext();
    string? prior = Environment.GetEnvironmentVariable("OS");
    try
    {
        Environment.SetEnvironmentVariable("OS", "Windows_NT");
        ToolReceipt receipt = await InvokeProcessTool(setup, "powershell.exe", [
            "-NoProfile", "-NonInteractive", "-Command", "$env:OS"
        ]);
        Assert(receipt.Success && receipt.Data.GetProperty("exitCode").GetInt32() == 0,
            "OS identity probe must execute successfully");
        Assert(receipt.Data.GetProperty("stdout").GetString()!.Trim() == "Windows_NT",
            "Approved child must receive only the allowlisted OS identity needed by dotnet templates");
    }
    finally { Environment.SetEnvironmentVariable("OS", prior); }
}


static Task ExplicitYesVariants()
{
    foreach (string input in new[] { "yes", "YES", "YeS", " yes ", "\tYES\t" })
        Assert(ApprovalInput.IsExplicitYes(input), "Explicit case-insensitive yes must authorize: " + input);
    foreach (string? input in new string?[] { null, "", " ", "y", "no", "yes please", "yes!", "yes\nno" })
        Assert(!ApprovalInput.IsExplicitYes(input), "Only an unambiguous yes token may authorize: " + input);
    return Task.CompletedTask;
}

static async Task DeniedProcessPausesWithoutReplay()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(_ => {
        if (++rounds != 1) throw new InvalidOperationException("Model retried a denied command");
        return new ModelReply(null, [Call("process", "process.run", new {
            executable = "dotnet", arguments = new[] { "--version" }, workingDirectory = setup.Workspace
        })]);
    });
    var broker = new FakeApprovals(false);
    Guid id = Guid.NewGuid();
    var answer = await setup.Engine(model).RunAsync(id,
        "Check dotnet version", broker);
    Assert(answer.Status == ConversationStatus.Paused && rounds == 1 && broker.Calls == 1,
        "Refusal must result in one authorization attempt and no retry");
    Assert(answer.EvidenceIds.Count == 0 && answer.Message.Contains("Not executed", StringComparison.Ordinal),
        "A denied process must not look completed or create execution evidence");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Status == ConversationStatus.Paused && saved.Operations.Count == 1 &&
        saved.Operations[0].Status == OperationStatus.Rejected &&
        saved.Messages.Any(m => m.Role == "tool" && m.Content?.Contains("NOT_AUTHORIZED", StringComparison.Ordinal) == true),
        "The denied invocation and model-facing tool result must be persisted");
}

static async Task PlainPermissionRecoversToProcess()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1) return new ModelReply("May I run `dotnet --version` to check the SDK?", []);
        if (rounds == 2)
        {
            Assert(messages.Any(m => m.Role == "system" &&
                m.Content?.Contains("previous visible reply was only a request for permission", StringComparison.Ordinal) == true),
                "Permission-only prose must be recovered using transient guidance");
            return new ModelReply(null, [Call("version", "process.run", new {
                executable = "dotnet", arguments = new[] { "--version" }, workingDirectory = setup.Workspace
            })]);
        }
        return new ModelReply("The installed .NET SDK version was observed from the process output.", []);
    });
    var approvals = new FakeApprovals(true);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Report actual dotnet SDK version using an execution tool", approvals);
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1 &&
        result.Message.Contains("exitCode: 0", StringComparison.Ordinal) && approvals.Calls == 1,
        "Recovered permission text must lead to exactly one authorized process with host evidence");
    Assert(rounds == 3, "Permission-only text should recover without additional model loops");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(!state.Messages.Any(m => m.Role == "assistant" &&
        m.Content?.StartsWith("May I run", StringComparison.Ordinal) == true),
        "Rejected permission prose must not pollute durable user-facing transcript");
}

static async Task RepeatedPlainPermissionPauses()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(_ => {
        rounds++;
        return new ModelReply("May I execute `dotnet --version`?", []);
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Execute dotnet --version", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Paused && rounds == 2 && result.EvidenceIds.Count == 0,
        "Repeated permission prose must pause without any executed action");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Status == ConversationStatus.Paused && state.Operations.Count == 0 &&
        state.LastAnswer is null && state.Messages.All(m => m.Role == "user"),
        "Permission prose must not become a fabricated completed answer");
}

static async Task StructuredFinishPermissionRejected()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("bad-finish", "nira_finish", new {
            outcome = "answer", answer = "May I execute dotnet --version?", evidence_ids = Array.Empty<string>()
        })]),
        new ModelReply("I did not execute a process or obtain a version.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Find dotnet version", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 0 &&
        result.Message.Contains("did not execute", StringComparison.Ordinal),
        "A corrected honest answer can finish, but a permission request cannot");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Messages.Any(m => m.Role == "tool" &&
        m.Content?.Contains("PERMISSION_QUESTION_NOT_COMPLETION", StringComparison.Ordinal) == true),
        "Structured finish permission request must receive explicit tool error");
}

static async Task DenialAfterWritePreservesReceipt()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(_ => {
        rounds++;
        if (rounds == 1) return new ModelReply(null, [Call("write", "filesystem.write_text", new {
            path = "partial.txt", content = "PARTIAL" })]);
        if (rounds == 2) return new ModelReply(null, [Call("run", "process.run", new {
            executable = "dotnet", arguments = new[] { "--version" }, workingDirectory = setup.Workspace
        })]);
        throw new InvalidOperationException("A rejected execution must not be re-proposed automatically");
    });
    var broker = new FakeApprovals(false);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Create a file and check dotnet version", broker);
    Assert(result.Status == ConversationStatus.Paused && rounds == 2 && broker.Calls == 1 &&
        result.EvidenceIds.Count == 1, "Partial authorized actions must stay audited after later denial");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "partial.txt")) == "PARTIAL",
        "Previously committed workspace file must not be rolled back on subsequent denial");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Operations.Count == 2 && state.Operations[0].Status == OperationStatus.Succeeded &&
        state.Operations[1].Status == OperationStatus.Rejected && state.Status == ConversationStatus.Paused,
        "Partial success and refusal must be separately recorded without declaring completion");
}

static async Task ObservedProcessNotRepeatedOnPermissionReply()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1) return new ModelReply(null, [Call("run", "process.run", new {
            executable = "dotnet", arguments = new[] { "--version" }, workingDirectory = setup.Workspace
        })]);
        if (rounds == 2) return new ModelReply("May I run dotnet --version?", []);
        Assert(messages.Any(m => m.Role == "system" &&
            m.Content?.Contains("already executed", StringComparison.Ordinal) == true),
            "Recovery should tell model to report existing receipts, not rerun an approved process");
        return new ModelReply("The previously executed command's version is recorded in stdout.", []);
    });
    var broker = new FakeApprovals(true);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Get installed dotnet version", broker);
    Assert(result.Status == ConversationStatus.Completed && rounds == 3 && broker.Calls == 1 &&
        result.EvidenceIds.Count == 1 && result.Message.Contains("stdout:", StringComparison.Ordinal),
        "An already observed invocation must not need another approval to format its final result");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Operations.Count == 1 && state.Operations[0].Status == OperationStatus.Succeeded,
        "One actual command execution expected");
}

static async Task ToolFreePermissionFallbackPauses()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, []), new ModelReply(null, []),
        new ModelReply("May I run dotnet --version?", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Check the installed dotnet version", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Paused && result.EvidenceIds.Count == 0 &&
        result.Message.Contains("No action was verified", StringComparison.Ordinal),
        "Tool-free fallback must not turn an unexecuted permission question into Completed");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Operations.Count == 0 && state.LastAnswer is null,
        "Fallback permission prose must not be saved as a verified user answer");
}

static async Task PermissionProbeCounterResetsAfterTool()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1) return new ModelReply("May I inspect the workspace?", []);
        if (rounds == 2) return new ModelReply(null, [Call("inspect", "workspace.info", new { })]);
        if (rounds == 3) return new ModelReply("May I execute dotnet --version?", []);
        if (rounds == 4) return new ModelReply(null, [Call("version", "process.run", new {
            executable = "dotnet", arguments = new[] { "--version" }, workingDirectory = setup.Workspace
        })]);
        return new ModelReply("The command returned the installed SDK version.", []);
    });
    var approval = new FakeApprovals(true);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Inspect the workspace then execute dotnet --version", approval);
    Assert(result.Status == ConversationStatus.Completed && rounds == 5 && approval.Calls == 1 &&
        result.EvidenceIds.Count == 1 && result.Message.Contains("exitCode: 0", StringComparison.Ordinal),
        "Permission-probe recovery should reset after a valid tool observation");
}

static Task BrowserRejectsNonHttpsAndCredentials()
{
    foreach (string bad in new[] { "http://www.example.com", "https://user:secret@www.example.com/",
        "https://www.example.com/#section", "ftp://www.example.com/" })
    {
        bool rejected = false;
        try { _ = PublicBrowserUrlPolicy.Parse(bad); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "Browser URL policy must reject: " + bad);
    }
    return Task.CompletedTask;
}

static Task BrowserRejectsInternalHostAndPorts()
{
    foreach (string bad in new[] { "https://localhost", "https://inside.local/",
        "https://admin.internal/", "https://host.test/", "https://10.0.0.1/",
        "https://127.0.0.1/", "https://[::1]/", "https://www.example.com:8443/" })
    {
        bool rejected = false;
        try { _ = PublicBrowserUrlPolicy.Parse(bad); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "Browser URL policy must reject internal target: " + bad);
    }
    return Task.CompletedTask;
}

static Task BrowserPublicDnsAddressGuards()
{
    string[] blocked = { "10.0.1.2", "127.0.0.1", "169.254.12.2", "172.20.0.1",
        "192.168.1.2", "100.64.1.1", "198.18.0.1", "203.0.113.9", "::1", "fc00::1", "fe80::1", "2001:db8::2" };
    foreach (string bad in blocked)
        Assert(!PublicBrowserUrlPolicy.IsPublicAddress(System.Net.IPAddress.Parse(bad)),
            "Non-public address must never be allowed: " + bad);
    Assert(PublicBrowserUrlPolicy.IsPublicAddress(System.Net.IPAddress.Parse("8.8.8.8")) &&
        PublicBrowserUrlPolicy.IsPublicAddress(System.Net.IPAddress.Parse("2606:4700:4700::1111")),
        "Valid globally routable addresses should be recognized");
    bool failed = false;
    try { PublicBrowserUrlPolicy.RequirePublicDns(new[] {
        System.Net.IPAddress.Parse("8.8.8.8"), System.Net.IPAddress.Parse("192.168.1.15") }); }
    catch (InvalidOperationException) { failed = true; }
    Assert(failed, "Mixed DNS results containing private IP must fail closed");
    return Task.CompletedTask;
}

static Task BrowserSameHostAndMethodEnforced()
{
    Assert(PublicBrowserUrlPolicy.AllowsSameHostGet("https://www.example.com/path", "www.example.com", "GET"),
        "Approved host HTTPS GET should pass");
    Assert(PublicBrowserUrlPolicy.AllowsSameHostGet("https://www.example.com/a.css", "www.example.com", "HEAD"),
        "HEAD resources should pass");
    Assert(!PublicBrowserUrlPolicy.AllowsSameHostGet("https://not.example.com/", "www.example.com", "GET") &&
        !PublicBrowserUrlPolicy.AllowsSameHostGet("http://www.example.com/", "www.example.com", "GET") &&
        !PublicBrowserUrlPolicy.AllowsSameHostGet("https://www.example.com/api", "www.example.com", "POST"),
        "Other origins, insecure transport and POST must be blocked");
    return Task.CompletedTask;
}

static Task BrowserInspectSchemaIsStrict()
{
    IAgentTool tool = new PublicBrowserInspectionTool(new FakeBrowserReader());
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { url="https://www.example.com", dangerous=true }), out _),
        "Unexpected browser arguments must fail schema validation");
    Assert(tool.Descriptor.Risk == ToolRisk.ReadExternal && !tool.Descriptor.HasSideEffects,
        "Browser inspection must require the network approval path and not claim local writes");
    return Task.CompletedTask;
}

static async Task BrowserDenialPreventsVisit()
{
    using var setup = new TestContext();
    var reader = new FakeBrowserReader();
    var registry = new ToolRegistry([new PublicBrowserInspectionTool(reader)]);
    var model = new FakeModel([new ModelReply(null, [Call("browser-denied", "browser.inspect_public",
        new { url = "https://www.example.com/" })])]);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace));
    Guid conversation = Guid.NewGuid();
    var denied = await engine.RunAsync(conversation, "Inspect a public page", new FakeApprovals(false));
    Assert(denied.Status == ConversationStatus.Paused && reader.Calls == 0,
        "Declined browser visit must pause without making any network request");
    var persisted = (await setup.Store.LoadAsync(conversation, default))!;
    Assert(persisted.Operations.Count == 1 && persisted.Operations[0].Status == OperationStatus.Rejected &&
        persisted.Operations[0].ErrorCode == "NOT_AUTHORIZED", "Denied browser visit must be audited");
}

static async Task BrowserApprovedObservationIsAudited()
{
    using var setup = new TestContext();
    var reader = new FakeBrowserReader();
    var registry = new ToolRegistry([new PublicBrowserInspectionTool(reader)]);
    var model = new FakeModel([
        new ModelReply(null, [Call("browser-approved", "browser.inspect_public", new {
            url = "https://www.example.com/" })]),
        new ModelReply("Observed a public page with title Example.", [])
    ]);
    var approvals = new FakeApprovals(true);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace));
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, "Inspect public example page", approvals);
    Assert(result.Status == ConversationStatus.Completed && approvals.Calls == 1 && reader.Calls == 1,
        "Approved browser visit must run once and finish from the actual observation");
    var persisted = (await setup.Store.LoadAsync(id, default))!;
    Assert(persisted.Operations.Count == 1 && persisted.Operations[0].Status == OperationStatus.Succeeded &&
        persisted.Operations[0].ResultJson!.Contains("Example Page", StringComparison.Ordinal),
        "Browser observation must be present in the durable tool journal");
}

static async Task BrowserRedirectOutsideApprovedOriginRejected()
{
    var reader = new FakeBrowserReader { FinalUrl = "https://other.example.net/" };
    var tool = new PublicBrowserInspectionTool(reader);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { url = "https://www.example.com/" }), default);
    Assert(!receipt.Success && receipt.ErrorCode == "BROWSER_REDIRECT_BLOCKED" && reader.Calls == 1,
        "Tool must reject any final page outside the explicitly approved hostname");
}

static Task BrowserValidPublicAddressAndUrl()
{
    Uri uri = PublicBrowserUrlPolicy.Parse("https://www.example.com:443/path?q=1");
    Assert(uri.Scheme == "https" && uri.Port == 443 && uri.Host == "www.example.com",
        "Public HTTPS URL with standard port and query should be accepted");
    PublicBrowserUrlPolicy.RequirePublicDns(new[] { System.Net.IPAddress.Parse("8.8.8.8") });
    bool emptyRejected = false;
    try { PublicBrowserUrlPolicy.RequirePublicDns(Array.Empty<System.Net.IPAddress>()); }
    catch (InvalidOperationException) { emptyRejected = true; }
    Assert(emptyRejected, "Empty DNS reply must fail closed");
    return Task.CompletedTask;
}

static async Task BrowserHttpWireProtocolRoundTrip()
{
    using var setup = new TestContext();
    using var handler = new BrowserWireHandler();
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://api.example.invalid/v1/"),
        "mock", () => "wire-browser-key");
    var reader = new FakeBrowserReader();
    var engine = new NiraEngine(model, new ToolRegistry([new PublicBrowserInspectionTool(reader)]),
        setup.Store, new WorkspaceAuthority(setup.Workspace));
    var approved = new FakeApprovals(true);
    var result = await engine.RunAsync(Guid.NewGuid(), "Inspect https://www.example.com/", approved);
    Assert(result.Status == ConversationStatus.Completed && handler.Rounds == 2 &&
        handler.ObservedReceipt && reader.Calls == 1 && approved.Calls == 1,
        "Real model HTTP protocol must carry the observed page receipt back to GPT-OSS tool transcript");
}

static ModelToolCall Call(string id, string name, object args) =>
    new(id, name, JsonSerializer.Serialize(args));
static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static Task BrowserElementsMetadataOnly()
{
    var snapshot = new BrowserSnapshot("https://www.example.com/", "https://www.example.com/",
        "Example", 200, "Public text", false, [], [
            new BrowserElement("button", "", "Search", "button", "Search", false),
            new BrowserElement("input", "", "Keyword", "text", "", false)]);
    string json = JsonSerializer.Serialize(snapshot);
    Assert(json.Contains("Keyword", StringComparison.Ordinal) && json.Contains("Search", StringComparison.Ordinal),
        "A page should report visible control labels");
    Assert(typeof(BrowserElement).GetProperties().All(p => p.Name is not ("Value" or "Password" or "Html")),
        "Public DOM element contract must not return input contents or page HTML");
    Assert(snapshot.Elements?.Count == 2, "Control metadata count incorrect");
    return Task.CompletedTask;
}

static Task BrowserLinkStrictSchema()
{
    var tool = new PublicBrowserLinkTool(new BrowserJourneyReader());
    var valid = JsonSerializer.SerializeToElement(new {
        sourceUrl="https://www.example.com/", linkIndex=0, expectedLinkUrl="https://www.example.com/about" });
    Assert(JsonToolSchema.Validate(tool.Descriptor.Parameters, valid, out _), "Valid link payload rejected");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { sourceUrl="https://www.example.com/", linkIndex=0 }), out _),
        "Missing expected exact link destination must be rejected");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { sourceUrl="https://www.example.com/", linkIndex=0,
            expectedLinkUrl="https://www.example.com/about", allowWrites=true }), out _),
        "Unexpected property must be rejected");
    return Task.CompletedTask;
}

static Task BrowserLinkTargetGuard()
{
    var tool = new PublicBrowserLinkTool(new BrowserJourneyReader());
    static object Args(string source, int index, string target) => new {
        sourceUrl=source, linkIndex=index, expectedLinkUrl=target };
    string target = tool.GetTarget(JsonSerializer.SerializeToElement(
        Args("https://www.example.com/",0,"https://www.example.com/about")));
    Assert(target.Contains("https://www.example.com/about",StringComparison.Ordinal),
        "Authorization must display exact intended destination");
    foreach (var args in new[] {
        Args("http://www.example.com/",0,"https://www.example.com/about"),
        Args("https://www.example.com/",-1,"https://www.example.com/about"),
        Args("https://www.example.com/",24,"https://www.example.com/about"),
        Args("https://www.example.com/",0,"https://iana.org/help"),
        Args("https://www.example.com/",0,"http://www.example.com/about") })
    {
        bool denied=false;
        try { _ = tool.GetTarget(JsonSerializer.SerializeToElement(args)); }
        catch (ArgumentException) { denied=true; }
        Assert(denied, "Malformed, cross-host or out-of-bounds link target must fail before approval");
    }
    return Task.CompletedTask;
}

static async Task BrowserLinkDenialNeverVisits()
{
    using var setup = new TestContext();
    var reader = new BrowserJourneyReader();
    var tools = new ToolRegistry([new PublicBrowserLinkTool(reader)]);
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null, [Call("deny-link", "browser.follow_public_link", new {
            sourceUrl="https://www.example.com/", linkIndex=0,
            expectedLinkUrl="https://www.example.com/about" })])
    ]), tools, setup.Store, new WorkspaceAuthority(setup.Workspace));
    var auth = new FakeApprovals(false);
    var result = await engine.RunAsync(Guid.NewGuid(), "Follow the public example link", auth);
    Assert(result.Status == ConversationStatus.Paused && reader.Calls == 0 && auth.Calls == 1,
        "Declined link follow must pause before opening a browser");
}

static async Task BrowserLinkApprovedTwoPageObservation()
{
    using var setup = new TestContext();
    var reader = new BrowserJourneyReader();
    var tools = new ToolRegistry([new PublicBrowserLinkTool(reader)]);
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null, [Call("follow", "browser.follow_public_link", new {
            sourceUrl="https://www.example.com/", linkIndex=0,
            expectedLinkUrl="https://www.example.com/about" })]),
        new ModelReply("Visited the About page based on the recorded destination title.", [])
    ]), tools, setup.Store, new WorkspaceAuthority(setup.Workspace));
    var auth = new FakeApprovals(true);
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, "Follow the observed public link", auth);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && reader.Calls == 2 && auth.Calls == 1,
        "Approved link should visit source and destination once, under one specific authorization");
    Assert(state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Succeeded &&
        state.Operations[0].ResultJson!.Contains("About Page",StringComparison.Ordinal),
        "Audit receipt must contain actually observed destination data");
}

static async Task BrowserLinkChangedHrefStops()
{
    var reader = new BrowserJourneyReader { SourceLinkUrl="https://www.example.com/changed" };
    var tool = new PublicBrowserLinkTool(reader);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sourceUrl="https://www.example.com/", linkIndex=0,
        expectedLinkUrl="https://www.example.com/about" }), default);
    Assert(!receipt.Success && receipt.ErrorCode=="BROWSER_LINK_CHANGED" && reader.Calls==1,
        "A changed link must never cause navigation to a different approved target");
}

static async Task BrowserLinkMissingIndexStops()
{
    var reader = new BrowserJourneyReader();
    var tool = new PublicBrowserLinkTool(reader);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sourceUrl="https://www.example.com/", linkIndex=5,
        expectedLinkUrl="https://www.example.com/about" }), default);
    Assert(!receipt.Success && receipt.ErrorCode=="BROWSER_LINK_MISSING" && reader.Calls==1,
        "Missing index must not navigate to another URL");
}

static async Task BrowserLinkSourceRedirectStopped()
{
    var reader = new BrowserJourneyReader { SourceFinalUrl="https://iana.org/help" };
    var tool = new PublicBrowserLinkTool(reader);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sourceUrl="https://www.example.com/", linkIndex=0,
        expectedLinkUrl="https://www.example.com/about" }), default);
    Assert(!receipt.Success && receipt.ErrorCode=="BROWSER_SOURCE_CHANGED_ORIGIN" && reader.Calls==1,
        "A source redirect off the approved host must be rejected before second visit");
}

static async Task BrowserLinkDestinationRedirectStopped()
{
    var reader = new BrowserJourneyReader { DestinationFinalUrl="https://iana.org/help" };
    var tool = new PublicBrowserLinkTool(reader);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sourceUrl="https://www.example.com/", linkIndex=0,
        expectedLinkUrl="https://www.example.com/about" }), default);
    Assert(!receipt.Success && receipt.ErrorCode=="BROWSER_TARGET_CHANGED_ORIGIN" && reader.Calls==2,
        "Destination redirect must never yield a successful result");
}

static async Task BrowserLinkWireReceipt()
{
    using var setup = new TestContext();
    var browser = new BrowserJourneyReader();
    using var handler = new BrowserLinkWireHandler();
    using var http = new HttpClient(handler);
    var client = new OpenAiCompatibleModel(http, new Uri("https://fake.example/v1/"),
        "test-model", () => "wire-link-key");
    var engine = new NiraEngine(client, new ToolRegistry([new PublicBrowserLinkTool(browser)]),
        setup.Store, new WorkspaceAuthority(setup.Workspace));
    var output = await engine.RunAsync(Guid.NewGuid(), "Follow an observed same-host page link", new FakeApprovals(true));
    Assert(output.Status==ConversationStatus.Completed && handler.ObservedReceipt &&
        handler.Rounds==2 && browser.Calls==2,
        "Model HTTP transcript must contain real link observation and use one tool invocation");
}


static async Task BrowserFollowPermissionRoutes()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions();
    var registry = new ToolRegistry([new BrowserSessionFollowTool(sessions)]);
    var model = new FakeModel([
        new ModelReply(null, [Call("permission", "nira_ask_user", new {
            question="Shall I follow the link to https://www.example.com/about?" })]),
        new ModelReply(null, [Call("follow", "browser.session_follow", new {
            sessionId=sessions.Id, expectedSourceUrl="https://www.example.com/",
            linkIndex=0, expectedLinkUrl="https://www.example.com/about" })]),
        new ModelReply("Observed the destination page after host approval.", [])
    ]);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace));
    var approvals = new FakeApprovals(true);
    var result = await engine.RunAsync(Guid.NewGuid(), "Follow an observed link using my existing public browser session", approvals);
    Assert(result.Status==ConversationStatus.Completed && sessions.FollowCalls==1 && approvals.Calls==1,
        "A 'Shall I follow' permission probe must route to host-approved action without AwaitingUser");
}

static Task SessionSchemasAndDestinations()
{
    var sessions = new FakePublicBrowserSessions();
    var open = new BrowserSessionOpenTool(sessions);
    var follow = new BrowserSessionFollowTool(sessions);
    var inspect = new BrowserSessionInspectTool(sessions);
    var close = new BrowserSessionCloseTool(sessions);
    var valid = JsonSerializer.SerializeToElement(new { sessionId=sessions.Id,
        expectedSourceUrl="https://www.example.com/", linkIndex=0,
        expectedLinkUrl="https://www.example.com/about" });
    Assert(JsonToolSchema.Validate(follow.Descriptor.Parameters,valid,out _), "Valid session-follow schema rejected");
    Assert(!JsonToolSchema.Validate(follow.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { sessionId=sessions.Id,
            expectedSourceUrl="https://www.example.com/",linkIndex=0 }), out _),
        "Destination must be required in session follow");
    Assert(!JsonToolSchema.Validate(open.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { url="https://www.example.com/", password="secret" }),out _),
        "Session open may not accept arbitrary inputs or credentials");
    foreach (var tool in new IAgentTool[] { inspect, close })
        Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
            JsonSerializer.SerializeToElement(new { sessionId=sessions.Id, arbitrary=true }),out _),
            "Session tools must reject unexpected parameters");
    Assert(follow.GetTarget(valid).Contains("https://www.example.com/about",StringComparison.Ordinal),
        "User approval must display exact link URL");
    Assert(open.Descriptor.Risk==ToolRisk.ReadExternal && follow.Descriptor.Risk==ToolRisk.ReadExternal,
        "Navigation must require host read-external approval");
    Assert(close.Descriptor.Risk==ToolRisk.ReadLocal, "Closing a session is a local cleanup");
    return Task.CompletedTask;
}

static async Task SessionOpenDenied()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions();
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("open","browser.session_open",new {url="https://www.example.com/"})])
    ]), new ToolRegistry([new BrowserSessionOpenTool(sessions)]), setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approver = new FakeApprovals(false);
    var answer=await engine.RunAsync(Guid.NewGuid(),"Open a public browser session",approver);
    Assert(answer.Status==ConversationStatus.Paused && sessions.OpenCalls==0 && approver.Calls==1,
        "A rejected browser session must not start a browser");
}

static async Task SessionOpenApproved()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions();
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("open","browser.session_open",new {url="https://www.example.com/"})]),
        new ModelReply("Opened the temporary browser session.",[])
    ]), new ToolRegistry([new BrowserSessionOpenTool(sessions)]), setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid conversation=Guid.NewGuid();
    var answer=await engine.RunAsync(conversation,"Open a public browser session",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(conversation,default))!;
    Assert(answer.Status==ConversationStatus.Completed && sessions.OpenCalls==1,
        "Approved session opening must run once");
    Assert(state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Succeeded &&
        state.Operations[0].ResultJson!.Contains(sessions.Id,StringComparison.Ordinal),
        "Session ID and page observation must be saved as durable evidence");
}

static async Task SessionInspectUsesExisting()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionInspectTool(sessions);
    var first=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new{sessionId=sessions.Id}),default);
    var next=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new{sessionId=sessions.Id}),default);
    Assert(first.Success && next.Success && sessions.InspectCalls==2 && sessions.OpenCalls==0 &&
        sessions.FollowCalls==0 && first.Data.GetProperty("FinalUrl").GetString()=="https://www.example.com/",
        "Repeated inspection should reuse the open session without navigating");
}

static async Task SessionFollowDenied()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("follow","browser.session_follow",new {
            sessionId=sessions.Id, expectedSourceUrl="https://www.example.com/",
            linkIndex=0, expectedLinkUrl="https://www.example.com/about" })])
    ]),new ToolRegistry([new BrowserSessionFollowTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approver=new FakeApprovals(false);
    var result=await engine.RunAsync(Guid.NewGuid(),"Follow a public link",approver);
    Assert(result.Status==ConversationStatus.Paused && approver.Calls==1 && sessions.FollowCalls==0,
        "Denied navigation must pause without touching the session");
}

static async Task SessionFollowApproved()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("follow","browser.session_follow",new {
            sessionId=sessions.Id, expectedSourceUrl="https://www.example.com/",
            linkIndex=0, expectedLinkUrl="https://www.example.com/about" })]),
        new ModelReply("Observed About page within the same browser session.",[])
    ]),new ToolRegistry([new BrowserSessionFollowTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid conversation=Guid.NewGuid();
    var output=await engine.RunAsync(conversation,"Follow observed public link",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(conversation,default))!;
    Assert(output.Status==ConversationStatus.Completed && sessions.FollowCalls==1 && sessions.Current=="https://www.example.com/about",
        "Approved exact URL should navigate once and preserve the session ID");
    Assert(state.Operations[0].ResultJson!.Contains("About Page",StringComparison.Ordinal),
        "Observed destination page must be in the audit receipt");
}

static async Task SessionChangedLinkRefused()
{
    var sessions=new FakePublicBrowserSessions { ChangedLink=true };
    var tool=new BrowserSessionFollowTool(sessions);
    var result=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id, expectedSourceUrl="https://www.example.com/",linkIndex=0,
        expectedLinkUrl="https://www.example.com/about" }),default);
    Assert(!result.Success && result.ErrorCode=="BROWSER_SESSION_FOLLOW_FAILED" &&
        sessions.Current=="https://www.example.com/" && sessions.SuccessfulNavigations==0,
        "Changed links must not perform navigation");
}

static Task SessionCrossHostRejected()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionFollowTool(sessions);
    foreach(var args in new object[] {
        new { sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/",linkIndex=0,
            expectedLinkUrl="https://other.example.org/" },
        new { sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/",linkIndex=99,
            expectedLinkUrl="https://www.example.com/about" },
        new { sessionId="not-a-real-session",expectedSourceUrl="https://www.example.com/",linkIndex=0,
            expectedLinkUrl="https://www.example.com/about" } })
    {
        bool rejected=false;
        try { _=tool.GetTarget(JsonSerializer.SerializeToElement(args)); }
        catch(ArgumentException) { rejected=true; }
        Assert(rejected,"Cross-host, out-of-range, or malformed session navigation must fail before approval");
    }
    Assert(sessions.FollowCalls==0,"Rejected inputs must not invoke browser operations");
    return Task.CompletedTask;
}

static async Task SessionCloseAndExpired()
{
    var sessions=new FakePublicBrowserSessions();
    var close=new BrowserSessionCloseTool(sessions);
    var inspect=new BrowserSessionInspectTool(sessions);
    var args=JsonSerializer.SerializeToElement(new {sessionId=sessions.Id});
    var first=await close.ExecuteAsync(args,default);
    var again=await close.ExecuteAsync(args,default);
    var dead=await inspect.ExecuteAsync(args,default);
    Assert(first.Success && !again.Success && !dead.Success && sessions.CloseCalls==2,
        "Closing a session must discard it and prevent future inspection");
}

static Task TextEntryDomIndexVisible()
{
    var field = new BrowserElement("input", "", "Keyword", "search", "", false, 3);
    Assert(field.DomIndex == 3 && BrowserSessionFillTextTool.IsAllowedControl(field),
        "Observed fields must disclose the actual DOM index without reading their values");
    Assert(!typeof(BrowserElement).GetProperties().Any(p => p.Name is "Value" or "Password"),
        "Browser observation must not include input values or password content");
    return Task.CompletedTask;
}

static Task TextEntryTargetValidation()
{
    var tool = new BrowserSessionFillTextTool(new FakePublicBrowserSessions());
    var registry = new ToolRegistry([tool]);
    var good = new {sessionId="aabbccddeeff00112233445566778899",
        expectedPageUrl="https://www.example.com/",domIndex=2,
        expectedTag="input",expectedLabel="Keyword",text="hello world"};
    Assert(registry.Validate(tool.Descriptor.Name,JsonSerializer.Serialize(good),out _,out _),
        "Valid exact-target text entry should meet the tool schema");
    foreach (string args in new [] {
        "{\"sessionId\":\"aabbccddeeff00112233445566778899\",\"expectedPageUrl\":\"https://www.example.com/\",\"domIndex\":2,\"expectedTag\":\"input\",\"expectedLabel\":\"Keyword\",\"text\":\"hello\",\"extra\":true}",
        JsonSerializer.Serialize(new {good.sessionId,good.expectedPageUrl,domIndex=24,good.expectedTag,good.expectedLabel,good.text}),
        JsonSerializer.Serialize(new {good.sessionId,expectedPageUrl="http://www.example.com/",good.domIndex,good.expectedTag,good.expectedLabel,good.text}),
        JsonSerializer.Serialize(new {good.sessionId,good.expectedPageUrl,good.domIndex,expectedTag="button",good.expectedLabel,good.text}),
        JsonSerializer.Serialize(new {good.sessionId,good.expectedPageUrl,good.domIndex,good.expectedTag,expectedLabel="",good.text}),
    })
    {
        bool refused = !registry.Validate(tool.Descriptor.Name,args,out var values,out _);
        if (!refused) { try { tool.GetTarget(values); } catch (ArgumentException) { refused = true; } }
        Assert(refused,"Malformed field, private link or out-of-range index must be rejected before authorization");
    }
    string description = tool.GetTarget(JsonSerializer.SerializeToElement(good));
    Assert(description.Contains("hello world",StringComparison.Ordinal) &&
        description.Contains("DOM index 2",StringComparison.Ordinal) &&
        description.Contains("https://www.example.com/",StringComparison.Ordinal),
        "Approval must display exact supplied text, DOM index and destination page");
    return Task.CompletedTask;
}

static Task TextEntryDangerousRisk()
{
    var descriptor = new BrowserSessionFillTextTool(new FakePublicBrowserSessions()).Descriptor;
    Assert(descriptor.Risk == ToolRisk.Dangerous && descriptor.HasSideEffects,
        "Text entry can trigger scripts and must always require explicit approval and journaling");
    using var setup = new TestContext();
    Assert(!new WorkspaceAuthority(setup.Workspace).IsPreapproved(descriptor,"workspace"),
        "No text-entry action may be preapproved inside the workspace");
    return Task.CompletedTask;
}

static async Task TextEntryDenied()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions();
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("fill","browser.session_fill_text",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
            expectedTag="input",expectedLabel="Keyword",text="nonsecret test"})])
    ]),new ToolRegistry([new BrowserSessionFillTextTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approvals = new FakeApprovals(false);
    Guid id=Guid.NewGuid();
    var output=await engine.RunAsync(id,"Fill the search field",approvals);
    Assert(output.Status==ConversationStatus.Paused && approvals.Calls==1 && sessions.FillCalls==0,
        "Denied action must pause without sending a keystroke");
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Rejected &&
        state.Operations[0].ErrorCode=="NOT_AUTHORIZED", "Denial must be durably recorded");
}

static async Task TextEntryApprovedAndRedacted()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions();
    const string value="ordinary search phrase";
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("fill","browser.session_fill_text",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
            expectedTag="input",expectedLabel="Keyword",text=value})]),
        new ModelReply("Entered text into the public search input without submitting.",[])
    ]),new ToolRegistry([new BrowserSessionFillTextTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var output=await engine.RunAsync(id,"Fill a public search field",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(output.Status==ConversationStatus.Completed && sessions.FillCalls==1,
        "An approved fill should call the selected field once and finish with observed evidence");
    Assert(state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Succeeded &&
        state.Operations[0].ResultJson!.Contains("CharactersEntered",StringComparison.Ordinal) &&
        !state.Operations[0].ResultJson!.Contains(value,StringComparison.Ordinal) &&
        output.Message.Contains("browser.session_fill_text",StringComparison.Ordinal),
        "Receipt must contain field verification and character count, but not entered text");
}

static async Task TextEntryStaleLabel()
{
    var sessions = new FakePublicBrowserSessions { ChangedLabel=true };
    var tool = new BrowserSessionFillTextTool(sessions);
    var result=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
        expectedTag="input",expectedLabel="Keyword",text="safe" }),default);
    Assert(!result.Success && result.ErrorCode=="BROWSER_TEXT_CONTROL_REFUSED" &&
        sessions.SuccessfulEntries==0,"Changed label should be refused before any fill");
}

static async Task TextEntryPasswordAndDisabled()
{
    foreach (bool password in new [] {true,false})
    {
        var sessions = new FakePublicBrowserSessions { PasswordField=password, DisabledField=!password };
        var tool = new BrowserSessionFillTextTool(sessions);
        var result=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
            expectedTag="input",expectedLabel="Keyword",text="safe" }),default);
        Assert(!result.Success && sessions.SuccessfulEntries==0,
            "Password or disabled fields must reject text entry without modifying the page");
    }
}

static async Task TextEntryAfterClose()
{
    var sessions = new FakePublicBrowserSessions();
    _=await new BrowserSessionCloseTool(sessions).ExecuteAsync(
        JsonSerializer.SerializeToElement(new {sessionId=sessions.Id}),default);
    var result=await new BrowserSessionFillTextTool(sessions).ExecuteAsync(
        JsonSerializer.SerializeToElement(new {sessionId=sessions.Id,expectedPageUrl=sessions.Current,
            domIndex=2,expectedTag="input",expectedLabel="Keyword",text="safe"}),default);
    Assert(!result.Success && sessions.SuccessfulEntries==0,
        "Closed or expired sessions must not allow text entry");
}

static async Task TextEntrySecondFillRefused()
{
    var sessions = new FakePublicBrowserSessions();
    var tool = new BrowserSessionFillTextTool(sessions);
    var args=JsonSerializer.SerializeToElement(new {sessionId=sessions.Id,
        expectedPageUrl=sessions.Current,domIndex=2,expectedTag="input",expectedLabel="Keyword",text="safe"});
    var first=await tool.ExecuteAsync(args,default);
    var second=await tool.ExecuteAsync(args,default);
    Assert(first.Success && !second.Success && sessions.SuccessfulEntries==1,
        "Only one entry is allowed per temporary session; no silent second interaction");
}

static async Task TextEntryUncertainReconciliation()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions { ThrowUncertain=true };
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("fill","browser.session_fill_text",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
            expectedTag="input",expectedLabel="Keyword",text="safe" })])
    ]),new ToolRegistry([new BrowserSessionFillTextTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var answer=await engine.RunAsync(id,"Fill public text field",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(answer.Status==ConversationStatus.NeedsReconciliation &&
        state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Unknown,
        "Uncertain browser effect must not be reported as failed/no-change or silently retried");
}

static Task ButtonClickStrictSchema()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionClickButtonTool(sessions);
    var valid=ButtonArgs(sessions);
    Assert(JsonToolSchema.Validate(tool.Descriptor.Parameters,valid,out _),
        "Exact native button schema should be valid");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,expectedTag="button",
        expectedType="button",expectedLabel="Show more",expectedText="Expand details",script="alert(1)"}),out _),
        "Arbitrary selectors and script fields must not be allowed");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,expectedTag="button",
        expectedType="submit",expectedLabel="Show more",expectedText="Expand details"}),out _),
        "Submit button type must fail schema validation");
    return Task.CompletedTask;
}

static JsonElement ButtonArgs(FakePublicBrowserSessions sessions) =>
    JsonSerializer.SerializeToElement(new { sessionId=sessions.Id,expectedPageUrl=sessions.Current,
        domIndex=3,expectedTag="button",expectedType="button",
        expectedLabel="Show more",expectedText="Expand details" });

static Task ButtonClickTargetGuards()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionClickButtonTool(sessions);
    foreach(var args in new object[] {
        new {sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=99,
            expectedTag="button",expectedType="button",expectedLabel="Show more",expectedText="Expand details"},
        new {sessionId=sessions.Id,expectedPageUrl="http://www.example.com/",domIndex=3,
            expectedTag="button",expectedType="button",expectedLabel="Show more",expectedText="Expand details"},
        new {sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,
            expectedTag="input",expectedType="button",expectedLabel="Show more",expectedText="Expand details"},
        new {sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,
            expectedTag="button",expectedType="submit",expectedLabel="Show more",expectedText="Expand details"},
        new {sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,
            expectedTag="button",expectedType="button",expectedLabel="",expectedText=""} })
    {
        bool failed=false;
        try { _=tool.GetTarget(JsonSerializer.SerializeToElement(args)); }
        catch (ArgumentException) { failed=true; }
        Assert(failed,"Non-public URL, absent target, submit type and invalid labels must fail before approval");
    }
    Assert(sessions.ClickCalls==0,"Invalid targets must never click");
    return Task.CompletedTask;
}

static Task ButtonClickDangerousApproval()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionClickButtonTool(sessions);
    string preview=tool.GetTarget(ButtonArgs(sessions));
    Assert(tool.Descriptor.Risk==ToolRisk.Dangerous && tool.Descriptor.HasSideEffects,
        "DOM click requires Dangerous approval and a durable effect receipt");
    Assert(preview.Contains(sessions.Current,StringComparison.Ordinal) &&
        preview.Contains("DOM index 3",StringComparison.Ordinal) &&
        preview.Contains("Show more",StringComparison.Ordinal) &&
        preview.Contains("Expand details",StringComparison.Ordinal) &&
        preview.Contains("WARNING",StringComparison.Ordinal),
        "Approval must identify exact page/button, and explain script side effects");
    Assert(BrowserSessionClickButtonTool.IsAllowedControl(new BrowserElement(
        "button","","Show more","button","Expand details",false,3)),
        "Native type=button is permitted");
    Assert(!BrowserSessionClickButtonTool.IsAllowedControl(new BrowserElement(
        "button","","Delete","submit","Delete",false,3)),
        "Submit button is not permitted");
    return Task.CompletedTask;
}

static async Task ButtonClickDenied()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("click","browser.session_click_button",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,expectedTag="button",
            expectedType="button",expectedLabel="Show more",expectedText="Expand details"})])
    ]),new ToolRegistry([new BrowserSessionClickButtonTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approver=new FakeApprovals(false);
    Guid conversation=Guid.NewGuid();
    var response=await engine.RunAsync(conversation,"Click a native demo button",approver);
    var state=(await setup.Store.LoadAsync(conversation,default))!;
    Assert(response.Status==ConversationStatus.Paused && approver.Calls==1 &&
        sessions.ClickCalls==0 && state.Operations.Count==1 &&
        state.Operations[0].Status==OperationStatus.Rejected,
        "Denied click must not touch the DOM and must preserve the denial");
}

static async Task ButtonClickApproved()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("click","browser.session_click_button",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,expectedTag="button",
            expectedType="button",expectedLabel="Show more",expectedText="Expand details"})]),
        new ModelReply("Approved native demo button was clicked; no navigation observed.",[])
    ]),new ToolRegistry([new BrowserSessionClickButtonTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid conversation=Guid.NewGuid();
    var response=await engine.RunAsync(conversation,"Click a native demo button",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(conversation,default))!;
    Assert(response.Status==ConversationStatus.Completed && sessions.SuccessfulClicks==1 &&
        state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Succeeded &&
        state.Operations[0].ResultJson!.Contains("ElementText",StringComparison.Ordinal) &&
        response.Message.Contains("browser.session_click_button",StringComparison.Ordinal),
        "Approved click must produce a recorded page observation and host evidence");
}

static async Task ButtonClickStaleMetadata()
{
    var sessions=new FakePublicBrowserSessions { ChangedButtonText=true };
    var result=await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default);
    Assert(!result.Success && result.ErrorCode=="BROWSER_BUTTON_CLICK_REFUSED" &&
        sessions.SuccessfulClicks==0,
        "Changed text or stale metadata must be rejected before clicking");
}

static async Task ButtonClickSubmitAndDisabled()
{
    foreach (bool isSubmit in new[]{true,false})
    {
        var sessions=new FakePublicBrowserSessions { ButtonSubmit=isSubmit,ButtonDisabled=!isSubmit };
        var result=await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default);
        Assert(!result.Success && sessions.SuccessfulClicks==0,
            "Form submit/reset and disabled buttons are not permitted");
    }
}

static async Task ButtonClickClosedSession()
{
    var sessions=new FakePublicBrowserSessions();
    _=await new BrowserSessionCloseTool(sessions).ExecuteAsync(
        JsonSerializer.SerializeToElement(new{sessionId=sessions.Id}),default);
    var result=await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default);
    Assert(!result.Success && sessions.SuccessfulClicks==0,
        "Closed browser session must not be implicitly reopened");
}

static async Task ButtonClickOnceOnly()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionClickButtonTool(sessions);
    var first=await tool.ExecuteAsync(ButtonArgs(sessions),default);
    var second=await tool.ExecuteAsync(ButtonArgs(sessions),default);
    var fillAfterClick=await new BrowserSessionFillTextTool(sessions).ExecuteAsync(
        JsonSerializer.SerializeToElement(new{sessionId=sessions.Id,expectedPageUrl=sessions.Current,
            domIndex=2,expectedTag="input",expectedLabel="Keyword",text="safe"}),default);
    Assert(first.Success && !second.Success && !fillAfterClick.Success &&
        sessions.SuccessfulClicks==1 && sessions.SuccessfulEntries==0,
        "A click locks the session against any further click or fill");
    var secondSession=new FakePublicBrowserSessions();
    _=await new BrowserSessionFillTextTool(secondSession).ExecuteAsync(
        JsonSerializer.SerializeToElement(new{sessionId=secondSession.Id,expectedPageUrl=secondSession.Current,
            domIndex=2,expectedTag="input",expectedLabel="Keyword",text="safe"}),default);
    var clickAfterFill=await new BrowserSessionClickButtonTool(secondSession)
        .ExecuteAsync(ButtonArgs(secondSession),default);
    Assert(!clickAfterFill.Success && secondSession.SuccessfulClicks==0,
        "A prior text entry must also lock the session against later clicks");
}

static async Task ButtonClickUncertainReconciliation()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions { ThrowClickUncertain=true };
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("click","browser.session_click_button",new{
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,expectedTag="button",
            expectedType="button",expectedLabel="Show more",expectedText="Expand details"})])
    ]),new ToolRegistry([new BrowserSessionClickButtonTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid conversation=Guid.NewGuid();
    var output=await engine.RunAsync(conversation,"Click demo button",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(conversation,default))!;
    Assert(output.Status==ConversationStatus.NeedsReconciliation &&
        state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Unknown,
        "An uncertain click is not a clean failure and must never be retried silently");
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
    public int Calls { get; private set; }
    public Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(approved);
    }
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

internal sealed class WirePermissionGateHandler : HttpMessageHandler
{
    public int Rounds { get; private set; }
    public bool HostDenialObserved { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.ToString() != "Bearer wire-auth")
            throw new InvalidOperationException("Wire gate request lost model credential");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        JsonElement[] entries = body.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        int turn = Rounds++;
        JsonElement[] receipts = entries.Where(x => x.GetProperty("role").GetString() == "tool").ToArray();
        if (turn == 1)
        {
            HostDenialObserved = receipts.Length == 1 &&
                receipts[0].GetProperty("content").GetString()?.Contains(
                    "USE_HOST_TOOL_APPROVAL", StringComparison.Ordinal) == true;
            if (!HostDenialObserved) throw new InvalidOperationException("Permission rejection was not preserved");
        }
        if (turn == 2 && (receipts.Length != 2 ||
            !receipts[1].GetProperty("content").GetString()!.Contains("Current workspace", StringComparison.Ordinal)))
            throw new InvalidOperationException("Missing actual workspace observation");
        string response = turn switch
        {
            0 => ToolReply("ask-permission", "nira_ask_user",
                new { question = "May I inspect the workspace with the registered tool?" }),
            1 => ToolReply("inspect", "workspace.info", new { }),
            2 => JsonSerializer.Serialize(new { choices = new[] {
                new { message = new { role = "assistant", content = "Workspace inspected using the available tool." } }
            } }),
            _ => throw new InvalidOperationException("Unexpected provider round")
        };
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json")
        };
    }
    private static string ToolReply(string id, string name, object args) => JsonSerializer.Serialize(new {
        choices = new[] { new { message = new { role = "assistant", content = (string?)null,
            tool_calls = new[] { new { id, type = "function",
                function = new { name, arguments = JsonSerializer.Serialize(args) } } }
        } } }
    });
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

internal sealed class FakeBrowserReader : IBrowserPageReader
{
    public int Calls { get; private set; }
    public string FinalUrl { get; set; } = "https://www.example.com/";
    public Task<BrowserSnapshot> ReadAsync(Uri target, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(new BrowserSnapshot(target.AbsoluteUri, FinalUrl,
            "Example Page", 200, "Example body", false,
            new[] { new BrowserLink("About", "https://www.example.com/about") }));
    }
}

internal sealed class BrowserWireHandler : HttpMessageHandler
{
    public int Rounds { get; private set; }
    public bool ObservedReceipt { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.ToString() != "Bearer wire-browser-key")
            throw new InvalidOperationException("Browser wire tool lost model API authorization");
        using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        int step = Rounds++;
        JsonElement[] wire = document.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        if (step == 1)
        {
            JsonElement[] toolMessages = wire.Where(m => m.GetProperty("role").GetString() == "tool").ToArray();
            ObservedReceipt = toolMessages.Length == 1 &&
                toolMessages[0].GetProperty("content").GetString()!.Contains("Example Page", StringComparison.Ordinal);
            if (!ObservedReceipt) throw new InvalidOperationException("Page observation missing from wire transcript");
        }
        string body = step switch
        {
            0 => JsonSerializer.Serialize(new { choices = new[] { new { message = new {
                role = "assistant", content = (string?)null, tool_calls = new[] { new {
                    id = "browser-wire-0", type = "function", function = new {
                        name = "browser.inspect_public", arguments = "{\"url\":\"https://www.example.com/\"}" } } }
            } } } }),
            1 => JsonSerializer.Serialize(new { choices = new[] { new { message = new {
                role = "assistant", content = "The observed title was Example Page."
            } } } }),
            _ => throw new InvalidOperationException("Unexpected additional browser tool round")
        };
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class BrowserJourneyReader : IBrowserPageReader
{
    public int Calls { get; private set; }
    public string SourceLinkUrl { get; set; } = "https://www.example.com/about";
    public string SourceFinalUrl { get; set; } = "https://www.example.com/";
    public string DestinationFinalUrl { get; set; } = "https://www.example.com/about";
    public Task<BrowserSnapshot> ReadAsync(Uri target, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Calls == 1
            ? new BrowserSnapshot(target.AbsoluteUri, SourceFinalUrl, "Source Page", 200,
                "source content", false, [new BrowserLink("About", SourceLinkUrl)])
            : new BrowserSnapshot(target.AbsoluteUri, DestinationFinalUrl, "About Page", 200,
                "destination content", false, []));
    }
}

internal sealed class BrowserLinkWireHandler : HttpMessageHandler
{
    public int Rounds { get; private set; }
    public bool ObservedReceipt { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.ToString()!="Bearer wire-link-key")
            throw new InvalidOperationException("Link wire authorization header not forwarded");
        using var requestJson=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        JsonElement[] entries=requestJson.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        int round=Rounds++;
        if (round==1)
        {
            JsonElement[] messages=entries.Where(x=>x.GetProperty("role").GetString()=="tool").ToArray();
            ObservedReceipt=messages.Length==1 && messages[0].GetProperty("content").GetString()!
                .Contains("About Page",StringComparison.Ordinal);
            if (!ObservedReceipt) throw new InvalidOperationException("Model never received observed destination title");
        }
        string response=round switch
        {
            0 => JsonSerializer.Serialize(new { choices=new[] { new { message=new {
                role="assistant", content=(string?)null, tool_calls=new[] { new {
                    id="follow-wire", type="function", function=new {name="browser.follow_public_link",
                    arguments=JsonSerializer.Serialize(new {sourceUrl="https://www.example.com/",
                        linkIndex=0, expectedLinkUrl="https://www.example.com/about"})} }
                } } } } }),
            1 => JsonSerializer.Serialize(new { choices=new[] { new { message=new {
                role="assistant", content="Observed destination title: About Page." } } } }),
            _ => throw new InvalidOperationException("Unexpected wire link round")
        };
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content=new StringContent(response,System.Text.Encoding.UTF8,"application/json") };
    }
}

internal sealed class FakePublicBrowserSessions : IPublicBrowserSessions
{
    public string Id { get; } = "aabbccddeeff00112233445566778899";
    public int OpenCalls { get; private set; }
    public int InspectCalls { get; private set; }
    public int FollowCalls { get; private set; }
    public int CloseCalls { get; private set; }
    public int SuccessfulNavigations { get; private set; }
    public bool ChangedLink { get; set; }
    public bool ChangedLabel { get; set; }
    public bool PasswordField { get; set; }
    public bool DisabledField { get; set; }
    public bool ThrowUncertain { get; set; }
    public bool ChangedButtonText { get; set; }
    public bool ButtonSubmit { get; set; }
    public bool ButtonDisabled { get; set; }
    public bool ThrowClickUncertain { get; set; }
    public int ClickCalls { get; private set; }
    public int SuccessfulClicks { get; private set; }
    public int FillCalls { get; private set; }
    public int SuccessfulEntries { get; private set; }
    public bool Closed { get; private set; }
    public string Current { get; private set; } = "https://www.example.com/";

    private BrowserSnapshot Snapshot() => new(Current,Current,
        Current.EndsWith("/about",StringComparison.Ordinal) ? "About Page" : "Example",
        200,"Observed public text",false,
        Current.EndsWith("/about",StringComparison.Ordinal)
            ? Array.Empty<BrowserLink>()
            : new[] { new BrowserLink("About",ChangedLink ? "https://www.example.com/changed" :
                "https://www.example.com/about") },
        new [] {
            new BrowserElement("input", "", ChangedLabel ? "Altered" : "Keyword",
                PasswordField ? "password" : "search", "", DisabledField, 2),
            new BrowserElement("button", "", "Show more",
                ButtonSubmit ? "submit" : "button",
                ChangedButtonText ? "Different" : "Expand details", ButtonDisabled, 3)
        });

    public Task<BrowserSessionObservation> OpenAsync(Uri url,CancellationToken ct)
    {
        OpenCalls++;
        if(Closed) throw new InvalidOperationException("Closed");
        if(url.AbsoluteUri!="https://www.example.com/") throw new InvalidOperationException("Invalid URL");
        return Task.FromResult(new BrowserSessionObservation(Id,Snapshot()));
    }
    public Task<BrowserSnapshot> InspectAsync(string id,CancellationToken ct)
    {
        InspectCalls++;
        if(Closed || id!=Id) throw new InvalidOperationException("Session missing");
        return Task.FromResult(Snapshot());
    }
    public Task<BrowserSessionLinkObservation> FollowAsync(string id,Uri source,int index,
        Uri expected,CancellationToken ct)
    {
        FollowCalls++;
        if(Closed || id!=Id || source.AbsoluteUri!=Current || index!=0 ||
            expected.AbsoluteUri!="https://www.example.com/about" ||
            Snapshot().Links[index].Url!=expected.AbsoluteUri)
            throw new InvalidOperationException("Link changed or session unavailable");
        Current=expected.AbsoluteUri;
        SuccessfulNavigations++;
        return Task.FromResult(new BrowserSessionLinkObservation(Id,source.AbsoluteUri,"About",
            expected.AbsoluteUri,Snapshot()));
    }
    public Task<BrowserSessionTextObservation> FillTextAsync(string id,Uri page,int index,
        string tag,string label,string text,CancellationToken ct)
    {
        FillCalls++;
        if (ThrowUncertain)
            throw new BrowserInteractionUncertainException("Uncertain browser write",
                new InvalidOperationException("Test uncertainty"));
        if (Closed || id != Id || page.AbsoluteUri != Current || index != 2 ||
            tag != "input" || label != "Keyword" || ChangedLabel || PasswordField ||
            DisabledField || SuccessfulEntries > 0 || SuccessfulClicks > 0)
            throw new InvalidOperationException("Field absent or previously changed");
        SuccessfulEntries++;
        return Task.FromResult(new BrowserSessionTextObservation(id,Current,index,tag,label,
            text.Length,Snapshot()));
    }
    public Task<BrowserSessionButtonObservation> ClickButtonAsync(string id,Uri page,int index,
        string label,string text,CancellationToken ct)
    {
        ClickCalls++;
        if (ThrowClickUncertain)
            throw new BrowserInteractionUncertainException("Uncertain button click",
                new InvalidOperationException("Test uncertainty"));
        if (Closed || id != Id || page.AbsoluteUri != Current || index != 3 ||
            label != "Show more" || text != "Expand details" || ChangedButtonText ||
            ButtonSubmit || ButtonDisabled || SuccessfulEntries > 0 || SuccessfulClicks > 0)
            throw new InvalidOperationException("Button absent, changed or already interacted with");
        SuccessfulClicks++;
        return Task.FromResult(new BrowserSessionButtonObservation(
            id,Current,index,label,text,Snapshot()));
    }
    public Task<bool> CloseAsync(string id,CancellationToken ct)
    {
        CloseCalls++;
        if(Closed || id!=Id) return Task.FromResult(false);
        Closed=true;
        return Task.FromResult(true);
    }
}

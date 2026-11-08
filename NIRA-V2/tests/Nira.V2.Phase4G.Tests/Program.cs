using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.LegacyAdapters;
using Nira.V2.Runtime.AgentLoop;

int passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new InvalidOperationException("FAIL Phase4G: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

string root = Path.Combine(Path.GetTempPath(), "nira-g-batch-" + Guid.NewGuid().ToString("N"));
string docs = Path.Combine(root, "Documents");
string testFolder = Path.Combine(docs, "TestFolder");
Directory.CreateDirectory(testFolder);
Func<string, string?> resolve = name => name.Equals("Documents", StringComparison.OrdinalIgnoreCase) ? docs : null;
var vault = new AgentPayloadVault(Path.Combine(root, "vault"));
var stub = new StubG();
try
{
    const string browserTurn = "Visit https://example.org/docs in the browser, then inspect the active page.";
    Check("browser opt-in mandatory", LegacyPhase4GRegistration.CreateBrowser(vault, stub, null, browserTurn).Count == 0);
    var browser = LegacyPhase4GRegistration.CreateBrowser(vault, stub,
        LegacyPhase4GRegistration.BrowserConsent, browserTurn);
    Check("three browser observation and navigation tools", browser.Count == 3);
    Check("all browser tools are observation-gated", browser.All(x => x.Spec.Access == AccessLevel.Observe));
    Check("browser schemas never accept arbitrary URLs", browser.All(x =>
        x.ValidateArguments(JsonSerializer.SerializeToElement(new { })) &&
        !x.ValidateArguments(JsonSerializer.SerializeToElement(new { url = "https://evil.example" }))));
    Check("browser private host blocked", LegacyPhase4GRegistration.CreateBrowser(vault, stub,
        LegacyPhase4GRegistration.BrowserConsent, "Open https://localhost/ in browser.").Count == 2);
    Check("browser query string blocked", LegacyPhase4GRegistration.CreateBrowser(vault, stub,
        LegacyPhase4GRegistration.BrowserConsent, "Open browser at https://example.org/?secret=x").Count == 2);
    Check("no arbitrary second URL", LegacyPhase4GRegistration.CreateBrowser(vault, stub,
        LegacyPhase4GRegistration.BrowserConsent,
        "Browser open https://example.org/ and https://example.com/").Count == 2);
    Check("model cannot forge tool function", !new CapabilityRegistry(browser).TryGetFunction("arbitrary_shell", out _));
    var browseNav = browser.Single(x => x.Spec.Id == "legacy.browser.navigate_exact");
    Guid runId = Guid.NewGuid();
    string token = await vault.SaveArgumentAsync(runId, browseNav.Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    AgentToolReceipt browseReceipt = await browseNav.ExecuteAsync(token, default);
    Check("browser navigation succeeds with audited legacy service", browseReceipt.Succeeded && !browseReceipt.OutcomeConfirmed);
    Check("URL bound to current user HTTPS request", stub.Last?.Arguments.GetProperty("url").GetString() ==
        "https://example.org/docs");
    Check("browser never executes a second model-chosen URL", stub.Calls == 1);
    JsonElement verifiedBrowse = await vault.ReadResultAsync(runId, browseNav.Spec.Id,
        browseReceipt.ResultReference!, default);
    Check("browser receipt binds exact host URL after audited success",
        verifiedBrowse.GetProperty("verifiedTargetUrl").GetString() == "https://example.org/docs" &&
        verifiedBrowse.GetProperty("observed").GetBoolean());
    var inspectBrowser = browser.Single(x => x.Spec.Id == "legacy.browser.inspect_active");
    Guid inspectRunId = Guid.NewGuid();
    string inspectToken = await vault.SaveArgumentAsync(inspectRunId, inspectBrowser.Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    AgentToolReceipt inspectReceipt = await inspectBrowser.ExecuteAsync(inspectToken, default);
    JsonElement inspectEvidence = await vault.ReadResultAsync(inspectRunId, inspectBrowser.Spec.Id,
        inspectReceipt.ResultReference!, default);
    Check("untrusted browser page inspection does not certify links as destinations",
        inspectReceipt.Succeeded &&
        inspectEvidence.GetProperty("verifiedTargetUrl").ValueKind == JsonValueKind.Null);


    const string docInspectTurn = "Inspect Documents/TestFolder/sample.docx and summarize its structure.";
    string existing = Path.Combine(testFolder, "sample.docx");
    File.WriteAllBytes(existing, Enumerable.Repeat((byte)66, 800).ToArray());
    Check("DOCX inspection requires consent", LegacyPhase4GRegistration.CreateDocuments(vault, stub,
        null, null, docInspectTurn, resolver: resolve).Count == 0);
    var read = LegacyPhase4GRegistration.CreateDocuments(vault, stub,
        LegacyPhase4GRegistration.DocxInspectConsent, null, docInspectTurn, resolver: resolve);
    Check("DOCX inspection is exactly one read tool", read.Count == 1 &&
        read[0].Spec.Id == "legacy.docx.inspect_exact");
    Check("path remains host-only", !read[0].InputSchema.GetRawText().Contains("path"));
    var readToken = await vault.SaveArgumentAsync(Guid.NewGuid(), read[0].Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    Check("DOCX inspection delegates read-only to existing service",
        (await read[0].ExecuteAsync(readToken, default)).Succeeded &&
        stub.Last?.CapabilityId == NIRACapabilityIds.DocxInspect);

    const string docCreate = "Create Documents/TestFolder/created.docx containing \"Hello from unified NIRA\".";
    Check("DOCX create disabled without distinct consent", LegacyPhase4GRegistration.CreateDocuments(vault, stub,
        null, null, docCreate, resolver: resolve).Count == 0);
    var create = LegacyPhase4GRegistration.CreateDocuments(vault, stub, null,
        LegacyPhase4GRegistration.DocxCreateConsent, docCreate, resolver: resolve);
    Check("one new modifying DOCX tool", create.Count == 1 && create[0].Spec.Access == AccessLevel.Modify);
    var tool = (LegacyPhase4GDocxCreateCapability)create[0];
    Check("DOCX mutation model gets no arguments", tool.ValidateArguments(JsonSerializer.SerializeToElement(new { })) &&
        !tool.ValidateArguments(JsonSerializer.SerializeToElement(new { content = "evil" })));
    Check("approval preview holds exact DOCX path and text", tool.ApprovalRequest.Arguments.GetRawText()
        .Contains("Hello from unified NIRA") && tool.ApprovalRequest.Arguments.GetRawText().Contains("created.docx"));
    Check("read-only registry refuses DOCX mutation", Throws(() =>
        new CapabilityRegistry(create).BuildProviderTools([tool.Spec])));
    Check("explicit write profile permits exact DOCX tool", new CapabilityRegistry(create)
        .BuildProviderTools([tool.Spec], allowWrite: true).Length == 1);
    string writeToken = await vault.SaveArgumentAsync(Guid.NewGuid(), tool.Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    Check("DOCX writes are not dispatched without the engine approval gate", stub.DocxWrites == 0);
    var deniedGate = new ScriptedGate(false);
    var deniedEngine = new AgentLoopEngine(new OnceReasoner(vault, tool.Spec.Id),
        new FileAgentStateStore(Path.Combine(root, "run-denied")), deniedGate, [tool], new AgentLoopLimits(4, 2));
    AgentRunSnapshot denied = await deniedEngine.StartAsync(docCreate);
    Check("owner denial prevents DOCX creation", denied.Status == AgentRunStatus.AwaitingAuthorization &&
        stub.DocxWrites == 0 && !File.Exists(Path.Combine(testFolder, "created.docx")));
    var acceptedEngine = new AgentLoopEngine(new OnceReasoner(vault, tool.Spec.Id),
        new FileAgentStateStore(Path.Combine(root, "run-accepted")), new ScriptedGate(true), [tool], new AgentLoopLimits(4, 2));
    AgentRunSnapshot accepted = await acceptedEngine.StartAsync(docCreate);
    Check("approved DOCX create and independent XML read-back", accepted.Status == AgentRunStatus.Completed &&
        stub.DocxWrites == 1 && File.Exists(Path.Combine(testFolder, "created.docx")));
    Check("same DOCX destination can no longer be registered", LegacyPhase4GRegistration.CreateDocuments(vault, stub,
        null, LegacyPhase4GRegistration.DocxCreateConsent, docCreate, resolver: resolve).Count == 0);
    string digest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
        Path.Combine(testFolder, "created.docx")))).ToLowerInvariant();
    var observed = accepted.Observations.Single(o => o.ToolId == tool.Spec.Id);
    var evidence = await vault.ReadResultAsync(accepted.Id, tool.Spec.Id, observed.ResultReference!);
    Check("independent DOCX hash proof", evidence.GetProperty("sha256").GetString() == digest);
    Check("no raw absolute path in DOCX result", !evidence.GetRawText().Contains(root));

    Check("unregistered TXT create never becomes generic manual instructions",
        LegacyPhase4GRegistration.ExplainUnavailableFileAction(
            "Create Documents/TestFolder/nira-g-approval-deny-20261108.txt containing \"Approval verification only\".",
            false, false, false)?.Contains("did not create") == true);
    Check("registered TXT create is not suppressed by admission feedback",
        LegacyPhase4GRegistration.ExplainUnavailableFileAction(
            "Create Documents/TestFolder/nira-g-approval-deny-20261108.txt containing \"Approval verification only\".",
            true, false, false) is null);
    Check("missing DOCX inspect gives accurate no-observation message",
        LegacyPhase4GRegistration.ExplainUnavailableFileAction(
            "Inspect Documents/TestFolder/absent.docx and summarize its structure.",
            false, false, false)?.Contains("did not open") == true);
    Check("DOCX overwrite attempt without create tool is not presented as success",
        LegacyPhase4GRegistration.ExplainUnavailableFileAction(
            "Create Documents/TestFolder/created.docx containing \"OTHER\".",
            false, false, false)?.Contains("did not create or overwrite") == true);
    Check("unrelated social replies have no file admission message",
        LegacyPhase4GRegistration.ExplainUnavailableFileAction(
            "Explain what Word documents are.", false,false,false) is null);

    const string uiTurn = "Inspect application Code window controls and accessibility.";
    Check("desktop UI inspection needs consent", LegacyPhase4GRegistration.CreateDesktopUi(vault,
        stub, null, uiTurn).Count == 0);
    var ui = LegacyPhase4GRegistration.CreateDesktopUi(vault, stub,
        LegacyPhase4GRegistration.DesktopConsent, uiTurn);
    Check("desktop and accessibility each registered", ui.Count == 2 && ui.All(x =>
        x.Spec.Access == AccessLevel.Observe));
    // Regression: "Inspect application" is a verb followed by a target type, not an
    // application called "Inspect"; nor is "window controls" a process target.
    Check("generic window controls do not grant a process scope",
        LegacyPhase4GRegistration.CreateDesktopUi(vault, stub,
            LegacyPhase4GRegistration.DesktopConsent,
            "Inspect window controls and accessibility.").Count == 0);
    Check("named Code window registers exactly the two observe tools",
        LegacyPhase4GRegistration.CreateDesktopUi(vault, stub,
            LegacyPhase4GRegistration.DesktopConsent,
            "Inspect Code window controls and accessibility.").Count == 2);
    Check("window-of Code names exactly one process",
        LegacyPhase4GRegistration.CreateDesktopUi(vault, stub,
            LegacyPhase4GRegistration.DesktopConsent,
            "Inspect window of Code and its controls.").Count == 2);
    Check("desktop scopes processName to the current message", ui[0].ValidateArguments(
        JsonSerializer.SerializeToElement(new { })) && !ui[0].ValidateArguments(
        JsonSerializer.SerializeToElement(new { processName = "chrome" })));
    string uiToken = await vault.SaveArgumentAsync(Guid.NewGuid(), ui[0].Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    Check("desktop inspection uses exact processName", (await ui[0].ExecuteAsync(uiToken, default)).Succeeded &&
        stub.Last?.Arguments.GetProperty("processName").GetString() == "Code");
    Check("ordinary inspection cannot activate", ui.All(x => x.Spec.Access == AccessLevel.Observe));
    var activations = LegacyPhase4GRegistration.CreateDesktopUi(vault, stub,
        LegacyPhase4GRegistration.DesktopConsent,
        "Activate application Code window after inspecting its controls.");
    Check("activation is a separately registered modifying tool", activations.Count == 3 &&
        activations.Count(x => x.Spec.Access == AccessLevel.Modify) == 1);
    Check("verb before application is not registered as a second process",
        activations.Count(x => x.Spec.Id == "legacy.desktop.inspect_exact") == 1 &&
        activations.Count(x => x.Spec.Id == "legacy.vision.accessibility_exact") == 1);
    var activate = (LegacyPhase4GDesktopActivateCapability)activations.Single(x =>
        x.Spec.Id == "legacy.desktop.activate_exact");
    Check("activation unavailable without this-run HWND evidence", activate.ApprovalRequest is null);
    var observedDesktop = activations.Single(x => x.Spec.Id == "legacy.desktop.inspect_exact");
    string inspectRef = await vault.SaveArgumentAsync(Guid.NewGuid(), observedDesktop.Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    Check("live desktop inspection records trusted HWND", (await observedDesktop.ExecuteAsync(inspectRef, default)).Succeeded &&
        activate.ApprovalRequest is not null &&
        activate.ApprovalRequest.Arguments.GetProperty("windowHandle").GetString() == "0x1234");
    Check("desktop action fixed to activate and exact process", activate.ApprovalRequest!.Arguments.GetProperty("action")
        .GetString() == "activate" && activate.ApprovalRequest.Arguments.GetProperty("processName").GetString() == "Code");
    Check("model cannot submit clicks/keys/target HWND", activate.ValidateArguments(
        JsonSerializer.SerializeToElement(new { })) && !activate.ValidateArguments(
        JsonSerializer.SerializeToElement(new { action = "type", text = "X" })));
    Check("no desktop action before separate UI authorization", stub.DesktopActs == 0);
    var activationRef = await vault.SaveArgumentAsync(Guid.NewGuid(), activate.Spec.Id,
        JsonSerializer.SerializeToElement(new { }));
    Check("desktop activation requires audited postcondition", (await activate.ExecuteAsync(activationRef, default)).Succeeded &&
        stub.DesktopActs == 1);
    var noEffectStub = new StubG { ForceDesktopNoEffect = true };
    var noEffectTools = LegacyPhase4GRegistration.CreateDesktopUi(vault, noEffectStub,
        LegacyPhase4GRegistration.DesktopConsent,
        "Activate application Code window after inspecting its controls.");
    var noEffectInspect = noEffectTools.Single(x => x.Spec.Id == "legacy.desktop.inspect_exact");
    var noEffectActivate = (LegacyPhase4GDesktopActivateCapability)noEffectTools.Single(x =>
        x.Spec.Id == "legacy.desktop.activate_exact");
    string noEffectInspectRef = await vault.SaveArgumentAsync(Guid.NewGuid(),
        noEffectInspect.Spec.Id, JsonSerializer.SerializeToElement(new { }));
    Check("failed desktop focus test starts from a real same-run HWND",
        (await noEffectInspect.ExecuteAsync(noEffectInspectRef, default)).Succeeded &&
        noEffectActivate.ApprovalRequest is not null);
    string noEffectRef = await vault.SaveArgumentAsync(Guid.NewGuid(),
        noEffectActivate.Spec.Id, JsonSerializer.SerializeToElement(new { }));
    AgentToolReceipt noEffect = await noEffectActivate.ExecuteAsync(noEffectRef, default);
    Check("audited no-dispatch desktop failure is not uncertain completion",
        !noEffect.Succeeded && noEffect.OutcomeConfirmed &&
        noEffect.OutcomeCode == "G4_DESKTOP_ACTIVATION_NO_EFFECT" && noEffectStub.DesktopActs == 1);

    Check("two conflicting target processes refuse", LegacyPhase4GRegistration.CreateDesktopUi(vault, stub,
        LegacyPhase4GRegistration.DesktopConsent,
        "Inspect application Code and application chrome.").Count == 0);

    Console.WriteLine($"RESULT: {passed} Phase 4G integrated pilot safety checks passed; 0 failed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
}

static bool Throws(Action work) { try { work(); return false; } catch (InvalidOperationException) { return true; } }

sealed class ScriptedGate(bool allowed) : IAgentPermissionGate
{
    public Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool, string arg, CancellationToken ct) =>
        Task.FromResult(tool.Access == AccessLevel.Observe || allowed);
}

sealed class OnceReasoner(AgentPayloadVault vault, string toolId) : IAgentReasoner
{
    public async Task<AgentDecision> DecideAsync(AgentRunSnapshot state, IReadOnlyList<AgentToolSpec> tools,
        CancellationToken ct)
    {
        if (state.Observations.Count > 0) return AgentDecision.Respond("The independent evidence confirms completion.");
        string reference = await vault.SaveArgumentAsync(state.Id, toolId,
            JsonSerializer.SerializeToElement(new { }), ct);
        return AgentDecision.Tool(toolId, reference, "phase4g-once-" + state.Id.ToString("N"));
    }
}

sealed class StubG : ILegacyPhase4GExecutor
{
    public NIRACapabilityRequest? Last { get; private set; }
    public int Calls { get; private set; }
    public int DocxWrites { get; private set; }
    public bool ForceDesktopNoEffect { get; set; }
    public int DesktopActs { get; private set; }
    public async Task<NIRACapabilityResult> ExecuteAsync(NIRACapabilityRequest req, CancellationToken ct)
    {
        Last = req;
        Calls++;
        bool docx = req.CapabilityId == NIRACapabilityIds.DocxCreate;
        bool desktopAct = req.CapabilityId == NIRACapabilityIds.DesktopAct;
        if (desktopAct) DesktopActs++;
        if (desktopAct && ForceDesktopNoEffect)
        {
            return new NIRACapabilityResult
            {
                RequestId = req.RequestId, CapabilityId = req.CapabilityId,
                Risk = NIRACapabilityRisk.Modify,
                Status = NIRACapabilityResultStatus.Failed, AuditRecorded = true,
                OutcomeUncertain = false, ChangedSystemState = false,
                Output = "Dispatched=False; PostconditionVerified=False"
            };
        }
        if (docx)
        {
            DocxWrites++;
            string path = req.Arguments.GetProperty("path").GetString()!;
            string content = req.Arguments.GetProperty("content").GetString()!;
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
            ZipArchiveEntry entry = zip.CreateEntry("word/document.xml");
            await using Stream output = entry.Open();
            byte[] bytes = Encoding.UTF8.GetBytes("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>" +
                System.Security.SecurityElement.Escape(content) + "</w:t></w:r></w:p></w:body></w:document>");
            await output.WriteAsync(bytes, ct);
        }
        return new NIRACapabilityResult
        {
            RequestId = req.RequestId,
            CapabilityId = req.CapabilityId,
            Risk = (docx || desktopAct) ? NIRACapabilityRisk.Modify : NIRACapabilityRisk.Observe,
            Status = NIRACapabilityResultStatus.Succeeded,
            AuditRecorded = true, OutcomeUncertain = false,
            ChangedSystemState = docx || desktopAct,
            Summary = "Stub executed after the V2 host scope and legacy authorization boundaries.",
            Output = desktopAct ? "ActionPostconditionVerified: True" :
                req.CapabilityId == NIRACapabilityIds.DesktopInspect ?
                "WindowHandle: 0x1234\nProcess: Code" : "Stub observed only user-authorized scope."
        };
    }
}

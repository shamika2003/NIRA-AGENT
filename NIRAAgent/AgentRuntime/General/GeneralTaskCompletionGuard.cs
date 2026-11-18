using System.Text;
using System.Text.Json;
using NIRAAgent.Runtime.Contracts;
using NIRAAgent.Runtime.Runtime.AgentLoop;

namespace NIRAAgent.Runtime.General;

/// <summary>
/// Authoritative observed-effect completion gate for ANY current-run request.
/// A model's statement or plan never certifies a file, saved change, or readback.
/// Only successful, audited capability receipts in this run count.
/// This is intentionally independent of the particular model/tool sequence.
/// </summary>
public sealed class GeneralTaskCompletionGuard
{
    private readonly AgentPayloadVault _vault;
    public GeneralTaskCompletionGuard(AgentPayloadVault vault) =>
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    public async Task<AgentTaskCompletionAssessment> AssessAsync(
        AgentRunSnapshot run, CancellationToken ct)
    {
        AgentTaskContract? contract = run.TaskContract;
        if (contract is null) return new AgentTaskCompletionAssessment(true, "No task contract.");
        if (contract.SchemaVersion != 1)
            return new AgentTaskCompletionAssessment(false, "Task checkpoint uses an unsupported contract version.");
        var changed = new List<(string Name, string Hash, int Index)>();
        var reads = new List<(string Name, string Hash, int Index)>();
        bool sawAction = false, sawChange = false;
        int i = 0;
        foreach (AgentObservation observation in run.Observations)
        {
            int at = i++;
            if (!observation.Succeeded || string.IsNullOrWhiteSpace(observation.ResultReference)) continue;
            JsonElement receipt;
            try
            {
                receipt = await _vault.ReadResultAsync(run.Id,
                    observation.ToolId, observation.ResultReference, ct);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException) { continue; }
            if (receipt.ValueKind != JsonValueKind.Object) continue;
            if (observation.ToolId == "nira.lifecycle.goal.plan_self")
            {
                if (ReadText(receipt, "outcome") == "nira_owned_intent_verified" &&
                    ReadBool(receipt, "backgroundExecutionScheduled") == false)
                { sawAction = true; sawChange = true; }
                continue;
            }
            if (!ReadBool(receipt, "auditRecorded") ||
                ReadText(receipt, "status") != "Succeeded" ||
                ReadBool(receipt, "outcomeUncertain")) continue;
            string id = ReadText(receipt, "capabilityId");
            string output = ReadText(receipt, "output");
            if (id.Length == 0) continue;
            sawAction = true;
            if (ReadBool(receipt, "changedSystemState"))
            {
                sawChange = true;
                string name = NameFromOutput(output, "Destination", "Path");
                if (name.Length > 0)
                    changed.Add((name, ReadLine(output, "DestinationSHA256") is string digest &&
                        digest.Length > 0 ? digest : ReadLine(output, "SHA256"), at));
            }
            else if (id.EndsWith(".inspect", StringComparison.OrdinalIgnoreCase) ||
                     id.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
            {
                string name = NameFromOutput(output, "Path", "Destination", "File");
                if (name.Length > 0)
                    reads.Add((name, ReadLine(output, "SHA256"), at));
            }
        }
        if (contract.RequiresVerifiedAction && !sawAction)
            return new(false, "No successful authorized tool action has been observed for this request.");
        if (contract.RequiresChangedState && !sawChange)
            return new(false, "No audited, confirmed state change has fulfilled the requested action.");
        if (contract.RequiresSavedArtifact)
        {
            string[] names = contract.RequestedFileNames.Count == 0
                ? new[] { string.Empty } : contract.RequestedFileNames.ToArray();
            foreach (string name in names)
            {
                (string Name, string Hash, int Index)[] published = changed
                    .Where(x => name.Length == 0 || string.Equals(x.Name, name,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
                if (published.Length == 0)
                    return new(false, name.Length > 0
                        ? "No verified creation receipt exists for the requested file: " + name
                        : "No successful saved-artifact receipt exists for the requested output.");
                if (contract.RequiresReadBack && !published.Any(write => reads.Any(read =>
                    read.Index > write.Index &&
                    string.Equals(read.Name, write.Name, StringComparison.OrdinalIgnoreCase) &&
                    (write.Hash.Length == 0 || read.Hash.Length == 0 ||
                     string.Equals(read.Hash, write.Hash, StringComparison.OrdinalIgnoreCase)))))
                    return new(false, "The saved artifact " +
                        (name.Length > 0 ? name : published[^1].Name) +
                        " has not been verified by a later matching read-only inspection.");
            }
        }
        return new(true, "The recorded tool receipts satisfy the mandatory external-action " +
            "prerequisites; answer quality and claimed visual appearance still require honest review.");
    }

    public async Task<string> ForModelAsync(AgentRunSnapshot run, CancellationToken ct)
    {
        AgentTaskContract? contract = run.TaskContract;
        if (contract is null) return string.Empty;
        AgentTaskCompletionAssessment assessment = await AssessAsync(run, ct);
        var text = new StringBuilder();
        text.AppendLine("CURRENT RUN TASK CONTRACT (trusted host state, not a long-term Goal):");
        text.AppendLine("The original user request is immutable and remains the full objective.");
        foreach (string milestone in contract.Milestones.Take(7))
            text.AppendLine("Model-proposed milestone (advisory, NOT proof): " + milestone);
        if (contract.RequestedFileNames.Count > 0)
            text.AppendLine("Required named outputs: " + string.Join(", ", contract.RequestedFileNames));
        if (contract.RequiresSavedArtifact)
            text.AppendLine("A real saved artifact needs an audited success receipt, not narration.");
        if (contract.RequiresReadBack)
            text.AppendLine("The user also requires independent read-back verification AFTER publication.");
        text.AppendLine("Mandatory execution proof: " +
            (assessment.Accept ? "Observed required actions" : "UNFULFILLED — " + assessment.Reason));
        if (contract.RejectedPrematureAnswers > 0)
            text.AppendLine("Prior final answer rejected because work was unfinished. " +
                "Do not repeat it; choose the next valid action or explain a concrete blocker.");
        text.AppendLine("Research is supporting evidence, not completion of an uncreated deliverable. " +
            "Continue with real tool calls if authorized; no invented effects or silent write retries.");
        return text.ToString();
    }

    private static string ReadText(JsonElement item, string key) =>
        item.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
    private static bool ReadBool(JsonElement item, string key) =>
        item.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    private static string ReadLine(string output, string key)
    {
        foreach (string line in output.Split('\n'))
            if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                return line[(key.Length + 1)..].Trim().Trim('\r');
        return string.Empty;
    }
    private static string NameFromOutput(string output, params string[] fields)
    {
        foreach (string field in fields)
        {
            string value = ReadLine(output, field);
            if (value.Length == 0 || value.Length > 2048) continue;
            string name = Path.GetFileName(value.Replace('\\', '/'));
            if (name is { Length: > 2 and <= 160 } && name.Contains('.')) return name;
        }
        return string.Empty;
    }

    // Integration with actual encrypted vault receipts, no NIRA capability or
    // model endpoint is invoked. Checks creation, read-back, hash mismatch and
    // the important "research only, premature final answer" failure mode.
    public static async Task<int> RunSelfCheckAsync(AgentPayloadVault vault,
        CancellationToken ct = default)
    {
        var guard = new GeneralTaskCompletionGuard(vault);
        var run = new AgentRunSnapshot { Id = Guid.NewGuid(),
            UserRequest = "Research AC motors, make Tesla_Report.docx and verify the saved file." };
        run.TaskContract = AgentTaskContractPolicy.Create(run.UserRequest);
        if ((await guard.AssessAsync(run, ct)).Accept)
            throw new InvalidOperationException("A research-only state satisfied an uncreated report.");
        var research = JsonSerializer.SerializeToElement(new {
            capabilityId = "web.search", status = "Succeeded", auditRecorded = true,
            changedSystemState = false, output = "3 sources" });
        string sr = await vault.SaveResultAsync(run.Id, "nira.capabilities.observe", research, ct);
        run.Observations.Add(new AgentObservation("nira.capabilities.observe", sr, true,
            "OK", DateTimeOffset.UtcNow));
        if ((await guard.AssessAsync(run, ct)).Accept)
            throw new InvalidOperationException("Research falsely completed artifact creation.");
        var written = JsonSerializer.SerializeToElement(new {
            capabilityId = "filesystem.docx.studio.create", status = "Succeeded",
            auditRecorded = true, changedSystemState = true, outcomeUncertain = false,
            output = "Destination=C:\\Docs\\Tesla_Report.docx\nDestinationSHA256=abc" });
        string sw = await vault.SaveResultAsync(run.Id, "nira.capabilities.execute", written, ct);
        run.Observations.Add(new AgentObservation("nira.capabilities.execute", sw, true,
            "OK", DateTimeOffset.UtcNow));
        if ((await guard.AssessAsync(run, ct)).Accept)
            throw new InvalidOperationException("Uninspected document considered verified.");
        var wrong = JsonSerializer.SerializeToElement(new {
            capabilityId = "filesystem.docx.inspect", status = "Succeeded",
            auditRecorded = true, changedSystemState = false, outcomeUncertain = false,
            output = "Path=C:\\Docs\\Tesla_Report.docx\nSHA256=wrong" });
        string sb = await vault.SaveResultAsync(run.Id, "nira.capabilities.observe", wrong, ct);
        run.Observations.Add(new AgentObservation("nira.capabilities.observe", sb, true,
            "OK", DateTimeOffset.UtcNow));
        if ((await guard.AssessAsync(run, ct)).Accept)
            throw new InvalidOperationException("Hash-mismatching document inspection accepted.");
        var inspected = JsonSerializer.SerializeToElement(new {
            capabilityId = "filesystem.docx.inspect", status = "Succeeded",
            auditRecorded = true, changedSystemState = false, outcomeUncertain = false,
            output = "Path=C:\\Docs\\Tesla_Report.docx\nSHA256=abc\nParagraphs=12" });
        string si = await vault.SaveResultAsync(run.Id, "nira.capabilities.observe", inspected, ct);
        run.Observations.Add(new AgentObservation("nira.capabilities.observe", si, true,
            "OK", DateTimeOffset.UtcNow));
        if (!(await guard.AssessAsync(run, ct)).Accept)
            throw new InvalidOperationException("Valid saved and inspected artifact rejected.");
        return 5;
    }
}

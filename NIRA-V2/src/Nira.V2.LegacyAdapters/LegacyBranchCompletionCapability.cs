using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Authorization;
using NIRAAgent.Branches;
using NIRAAgent.Capabilities;
using NIRAAgent.Goals;
using NIRAAgent.Mind;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.LegacyAdapters;

// Phase 4H.6: exactly one explicitly requested branch completion. The existing
// BranchService is the ONLY writer. These snapshots contain no private work payloads.
public sealed record ExactBranchCompletionRequest(Guid BranchId, string Result, string Evidence);

public sealed record BranchCompletionSnapshot(
    Guid Id, Guid GoalId, string Status, int EvidenceCount,
    string? LastEvidenceSummary, string? ResultSummary,
    DateTimeOffset UpdatedAt, DateTimeOffset? ResolvedAt,
    bool ParentGoalOpen, int IncompleteDependencies, int IncompleteRequiredChildren,
    int AssignedWorkCount, int NonSucceededWorkCount, string RelatedStateFingerprint);

public interface ILegacyBranchCompletionExecutor
{
    BranchCompletionSnapshot? Observe(ExactBranchCompletionRequest request);
    Task<BranchCompletionSnapshot?> ReadPersistedAsync(ExactBranchCompletionRequest request, CancellationToken ct);
    Task<bool> CompleteAsync(NIRAMindEvent source, ExactBranchCompletionRequest request, CancellationToken ct);
}

public sealed class NIRALegacyBranchCompletionExecutor : ILegacyBranchCompletionExecutor
{
    private readonly NIRABranchService _branches;
    private readonly NIRABranchStore _branchStore;
    private readonly NIRABranchWorkService _work;
    private readonly NIRABranchWorkStore _workStore;
    private readonly NIRAGoalService _goals;
    private readonly NIRAGoalStore _goalStore;

    public NIRALegacyBranchCompletionExecutor(NIRABranchService branches, NIRABranchStore branchStore,
        NIRABranchWorkService work, NIRABranchWorkStore workStore,
        NIRAGoalService goals, NIRAGoalStore goalStore)
    {
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _branchStore = branchStore ?? throw new ArgumentNullException(nameof(branchStore));
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _workStore = workStore ?? throw new ArgumentNullException(nameof(workStore));
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
        _goalStore = goalStore ?? throw new ArgumentNullException(nameof(goalStore));
    }

    public BranchCompletionSnapshot? Observe(ExactBranchCompletionRequest request) =>
        Build(request.BranchId, _branches.CurrentBranches, _work.CurrentWork, _goals.CurrentGoals);

    public async Task<BranchCompletionSnapshot?> ReadPersistedAsync(
        ExactBranchCompletionRequest request, CancellationToken ct) => Build(request.BranchId,
            await _branchStore.ReadBranchesAsync(ct), await _workStore.ReadAllAsync(ct),
            await _goalStore.ReadGoalsAsync(ct));

    private static BranchCompletionSnapshot? Build(Guid id,
        IReadOnlyList<NIRABranchState> branches, IReadOnlyList<NIRABranchWorkItem> work,
        IReadOnlyList<NIRAGoalState> goals)
    {
        NIRABranchState? branch = branches.SingleOrDefault(b => b.Id == id);
        if (branch is null) return null;
        NIRAGoalState? goal = goals.SingleOrDefault(g => g.Id == branch.GoalId);
        var byId = branches.ToDictionary(b => b.Id);
        NIRABranchState[] requiredChildren = branches.Where(b => b.ParentBranchId == id &&
            b.JoinPolicy == NIRABranchJoinPolicy.Required).ToArray();
        NIRABranchWorkItem[] assigned = work.Where(w => w.BranchId == id).ToArray();
        int dependenciesOpen = branch.DependsOnBranchIds.Count(dependency =>
            !byId.TryGetValue(dependency, out NIRABranchState? d) ||
            d.Status != NIRABranchStatus.Completed);
        int requiredChildrenOpen = requiredChildren.Count(child => child.Status != NIRABranchStatus.Completed);
        int workNotSucceeded = assigned.Count(w => w.Status != NIRABranchWorkStatus.Succeeded);
        var sign = new StringBuilder();
        sign.Append(branch.Id).Append('|').Append(branch.GoalId).Append('|')
            .Append(goal?.Status.ToString() ?? "MISSING");
        foreach (Guid dependency in branch.DependsOnBranchIds.OrderBy(v => v))
            sign.Append("|dep:").Append(dependency).Append(':')
                .Append(byId.TryGetValue(dependency, out NIRABranchState? d) ? d.Status.ToString() : "MISSING");
        foreach (NIRABranchState child in requiredChildren.OrderBy(c => c.Id))
            sign.Append("|child:").Append(child.Id).Append(':').Append(child.Status);
        foreach (NIRABranchWorkItem item in assigned.OrderBy(w => w.Id))
            sign.Append("|work:").Append(item.Id).Append(':').Append(item.Status);
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sign.ToString())));
        return new(branch.Id, branch.GoalId, branch.Status.ToString(), branch.EvidenceCount,
            branch.LastEvidenceSummary, branch.ResultSummary, branch.UpdatedAt, branch.ResolvedAt,
            goal is not null && !goal.IsResolved, dependenciesOpen, requiredChildrenOpen,
            assigned.Length, workNotSucceeded, fingerprint);
    }

    public async Task<bool> CompleteAsync(NIRAMindEvent source,
        ExactBranchCompletionRequest request, CancellationToken ct)
    {
        if (source.Source != NIRAMindEventSource.User ||
            !LegacyBranchCompletionRegistration.TryParse(source.Content, out var parsed) ||
            parsed != request) throw new InvalidDataException("BRANCH_COMPLETION_SOURCE_NOT_CURRENT_USER");
        var result = await _branches.ApplyProposalsAsync(source, string.Empty, string.Empty,
            [new NIRABranchProposal
            {
                Action = NIRABranchProposalAction.Complete,
                BranchId = request.BranchId.ToString("D"),
                ResultSummary = request.Result,
                EvidenceSource = NIRABranchEvidenceSource.CurrentEvent,
                EvidenceQuote = request.Evidence,
                EvidenceSummary = request.Evidence,
                Reason = "Explicit owner-approved final branch result; not an automated verification of the claimed work.",
                Confidence = 1.0
            }], ct);
        return result.Count == 1 && result[0].Action == NIRABranchApplyAction.Transitioned &&
            result[0].Changed && result[0].Branch?.Status == NIRABranchStatus.Completed;
    }
}

public static class LegacyBranchCompletionRegistration
{
    public const string ConsentValue = "ALLOW_EXACT_BRANCH_COMPLETION";
    private const string GuidPattern = @"(?<id>[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})";
    private const string QuotedResult = "\"(?<result>[^\"\\r\\n]{12,240})\"";
    private const string QuotedEvidence = "\"(?<evidence>[^\"\\r\\n]{12,300})\"";
    private static readonly Regex Pattern = new(@"^\s*Complete\s+branch\s+" + GuidPattern +
        @"\s+with\s+result\s+" + QuotedResult + @"\s+and\s+evidence\s+" + QuotedEvidence +
        @"\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex Intent = new(@"^\s*Complete\s+branch\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));

    public static bool TryParse(string? text, out ExactBranchCompletionRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 700 || text.Any(char.IsControl)) return false;
        Match match = Pattern.Match(text);
        if (!match.Success || !Guid.TryParseExact(match.Groups["id"].Value, "D", out Guid id) ||
            id == Guid.Empty) return false;
        string result = match.Groups["result"].Value.Trim();
        string evidence = match.Groups["evidence"].Value.Trim();
        if (result.Length is < 12 or > 240 || evidence.Length is < 12 or > 300 ||
            result.Equals(evidence, StringComparison.OrdinalIgnoreCase)) return false;
        request = new(id, result, evidence);
        return true;
    }

    public static bool CanComplete(ExactBranchCompletionRequest request, BranchCompletionSnapshot? state) =>
        state is not null && state.Id == request.BranchId &&
        (state.Status is "Waiting" or "Active") && state.ResolvedAt is null &&
        state.ParentGoalOpen && state.IncompleteDependencies == 0 &&
        state.IncompleteRequiredChildren == 0 && state.NonSucceededWorkCount == 0 &&
        state.EvidenceCount >= 0;

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        ILegacyBranchCompletionExecutor executor, string? consent, bool lifecycleConsented,
        string request, NIRAMindEvent source)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(source);
        if (consent != ConsentValue || !lifecycleConsented || source.Source != NIRAMindEventSource.User ||
            !string.Equals(request.Trim(), source.Content.Trim(), StringComparison.Ordinal) ||
            !TryParse(request, out ExactBranchCompletionRequest? exact) || exact is null) return [];
        BranchCompletionSnapshot? original = executor.Observe(exact);
        if (!CanComplete(exact, original)) return [];
        return [new LegacyBranchCompletionCapability(vault, executor, source, exact, original!)];
    }

    public static string? ExplainUnavailable(string text, string? consent,
        bool lifecycleConsented, bool registered)
    {
        if (registered || string.IsNullOrWhiteSpace(text) || text.Length > 700 ||
            !Intent.IsMatch(text)) return null;
        if (consent != ConsentValue || !lifecycleConsented)
            return "Branch completion is disabled. Relaunch with -AllowPersonalContext " +
                "-AllowLifecycleContext -AllowBranchCompletion. No branch was completed.";
        return "No branch was completed. Use: Complete branch <GUID> with result " +
            "\"concrete result\" and evidence \"independently checked or user-observed evidence\". " +
            "The parent goal must be open; prerequisites and required children must be completed; " +
            "no assigned work may be pending, running, failed, blocked, or interrupted.";
    }
}

public sealed class LegacyBranchCompletionCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly ILegacyBranchCompletionExecutor _executor;
    private readonly NIRAMindEvent _source;
    private readonly ExactBranchCompletionRequest _request;
    private readonly BranchCompletionSnapshot _original;

    public LegacyBranchCompletionCapability(AgentPayloadVault vault,
        ILegacyBranchCompletionExecutor executor, NIRAMindEvent source,
        ExactBranchCompletionRequest request, BranchCompletionSnapshot original)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _original = original ?? throw new ArgumentNullException(nameof(original));
        if (source.Source != NIRAMindEventSource.User ||
            !LegacyBranchCompletionRegistration.TryParse(source.Content, out var parsed) ||
            parsed != request || !LegacyBranchCompletionRegistration.CanComplete(request, original))
            throw new ArgumentException("Completion must bind to direct user evidence and open branch.");
        ApprovalRequest = new NIRACapabilityRequest
        {
            RequestId = Guid.NewGuid(),
            CapabilityId = "lifecycle.branch.complete_exact",
            Arguments = JsonSerializer.SerializeToElement(new
            {
                branchId = request.BranchId.ToString("D"),
                goalId = original.GoalId.ToString("D"),
                originalStatus = original.Status,
                originalEvidenceCount = original.EvidenceCount,
                assignedWorkCount = original.AssignedWorkCount,
                result = request.Result,
                evidence = request.Evidence,
                action = "Complete",
                scope = "OneExistingBranch",
                owner = "ExistingNIRABranchService"
            }),
            Reason = "Explicit owner-requested terminal branch completion. Evidence text is an assertion, not automatic proof."
        };
    }

    public NIRACapabilityRequest ApprovalRequest { get; }
    public AgentToolSpec Spec { get; } = new("legacy.lifecycle.branch.complete_exact",
        "Complete ONE exact current-turn branch only after fresh owner Allow Once, " +
        "no outstanding/failed work, completed dependencies, and persisted-state reconciliation. " +
        "Use {} only. Never run work, complete goals, or infer success from model prose.", AccessLevel.Modify);
    public string FunctionName => "nira_complete_exact_saved_branch";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { }, required = Array.Empty<string>(),
        additionalProperties = false
    });
    public bool ValidateArguments(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object && !args.EnumerateObject().Any();

    public bool IsStillAllowed()
    {
        BranchCompletionSnapshot? current = _executor.Observe(_request);
        return LegacyBranchCompletionRegistration.CanComplete(_request, current) &&
            current == _original;
    }

    public async Task<bool> CanApproveAsync(CancellationToken ct)
    {
        if (!IsStillAllowed()) return false;
        BranchCompletionSnapshot? stored = await _executor.ReadPersistedAsync(_request, ct);
        return stored == _original;
    }

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            JsonElement args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
            if (!ValidateArguments(args))
                return AgentToolReceipt.ConfirmedNoChange("BRANCH_COMPLETE_ARGUMENTS_INVALID");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException)
        { return AgentToolReceipt.ConfirmedNoChange("BRANCH_COMPLETE_REFERENCE_INVALID"); }

        try
        {
            if (!await CanApproveAsync(ct))
                return AgentToolReceipt.ConfirmedNoChange("BRANCH_COMPLETE_PRECOMMIT_STATE_CHANGED");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return AgentToolReceipt.ConfirmedNoChange("BRANCH_COMPLETE_SQLITE_PREFLIGHT_FAILED"); }

        bool applied;
        try { applied = await _executor.CompleteAsync(_source, _request, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return AgentToolReceipt.Uncertain("BRANCH_COMPLETE_COMMIT_OUTCOME_UNKNOWN"); }
        if (!applied) return AgentToolReceipt.Uncertain("BRANCH_COMPLETE_OWNER_REJECTED_OR_CONFLICT");
        try
        {
            BranchCompletionSnapshot? saved = await _executor.ReadPersistedAsync(_request, ct);
            BranchCompletionSnapshot? live = _executor.Observe(_request);
            if (saved is null || live != saved || saved.Id != _request.BranchId ||
                saved.GoalId != _original.GoalId || saved.Status != "Completed" ||
                saved.ResultSummary != _request.Result || saved.LastEvidenceSummary != _request.Evidence ||
                saved.EvidenceCount != _original.EvidenceCount + 1 || saved.ResolvedAt is null ||
                saved.UpdatedAt < _original.UpdatedAt || saved.NonSucceededWorkCount != 0 ||
                saved.IncompleteDependencies != 0 || saved.IncompleteRequiredChildren != 0)
                return AgentToolReceipt.Uncertain("BRANCH_COMPLETE_POSTCOMMIT_READBACK_MISMATCH");
            JsonElement receipt = JsonSerializer.SerializeToElement(new
            {
                outcome = "authoritative_sqlite_and_live_verified",
                branchId = saved.Id.ToString("D"),
                goalId = saved.GoalId.ToString("D"),
                status = saved.Status,
                result = _request.Result,
                evidence = _request.Evidence,
                evidenceCount = saved.EvidenceCount,
                resolvedUtc = saved.ResolvedAt?.ToUniversalTime(),
                verification = "Recorded user-provided evidence. No independent real-world proof implied.",
                owner = "ExistingNIRABranchService",
                verifiedUtc = DateTimeOffset.UtcNow
            });
            string token = await _vault.SaveResultAsync(runId, Spec.Id, receipt, ct);
            return AgentToolReceipt.Verified(token);
        }
        catch (Exception) { return AgentToolReceipt.Uncertain("BRANCH_COMPLETE_POSTCOMMIT_READBACK_UNKNOWN"); }
    }
}

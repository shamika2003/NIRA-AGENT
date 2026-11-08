using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Authorization;
using NIRAAgent.Capabilities;
using NIRAAgent.Branches;
using NIRAAgent.Goals;
using NIRAAgent.Mind;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.LegacyAdapters;

// Phase 4H.4: non-dispatching mutations ONLY. No activation, completion,
// cancellation, time-wakes, state-owner duplication, or model-selected IDs.
public enum LifecycleItemKind { Goal, Branch }
public enum LifecycleMutationKind { Progress, Waiting, Blocked }
public sealed record ExactLifecycleMutationRequest(
    LifecycleItemKind ItemKind, LifecycleMutationKind Mutation, Guid Id, string Detail);
public sealed record LifecycleMutationSnapshot(
    Guid Id, LifecycleItemKind ItemKind, string Status, bool IsResolved,
    int EvidenceCount, DateTimeOffset UpdatedAt, string? LastEvidenceSummary,
    string? WaitingFor, string? Blocker);

public interface ILegacyLifecycleMutationExecutor
{
    LifecycleMutationSnapshot? Observe(ExactLifecycleMutationRequest request);
    Task<bool> ApplyAsync(NIRAMindEvent source, ExactLifecycleMutationRequest request, CancellationToken ct);
    Task<LifecycleMutationSnapshot?> ReadPersistedAsync(ExactLifecycleMutationRequest request, CancellationToken ct);
}

public sealed class NIRALegacyLifecycleMutationExecutor : ILegacyLifecycleMutationExecutor
{
    private readonly NIRAGoalService _goals;
    private readonly NIRABranchService _branches;
    private readonly NIRAGoalStore _goalStore;
    private readonly NIRABranchStore _branchStore;

    public NIRALegacyLifecycleMutationExecutor(NIRAGoalService goals,
        NIRABranchService branches, NIRAGoalStore goalStore, NIRABranchStore branchStore)
    {
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _goalStore = goalStore ?? throw new ArgumentNullException(nameof(goalStore));
        _branchStore = branchStore ?? throw new ArgumentNullException(nameof(branchStore));
    }

    private static LifecycleMutationSnapshot Snap(NIRAGoalState goal) =>
        new(goal.Id, LifecycleItemKind.Goal, goal.Status.ToString(), goal.IsResolved,
            goal.EvidenceCount, goal.UpdatedAt, goal.LastEvidenceSummary, goal.WaitingFor, goal.Blocker);
    private static LifecycleMutationSnapshot Snap(NIRABranchState branch) =>
        new(branch.Id, LifecycleItemKind.Branch, branch.Status.ToString(), branch.IsResolved,
            branch.EvidenceCount, branch.UpdatedAt, branch.LastEvidenceSummary, branch.WaitingFor, branch.Blocker);

    public LifecycleMutationSnapshot? Observe(ExactLifecycleMutationRequest request)
    {
        if (request.ItemKind == LifecycleItemKind.Goal)
            return _goals.TryGetGoal(request.Id, out NIRAGoalState? goal) && goal is not null
                ? Snap(goal) : null;
        return _branches.TryGetBranch(request.Id, out NIRABranchState? branch) && branch is not null
            ? Snap(branch) : null;
    }

    public async Task<LifecycleMutationSnapshot?> ReadPersistedAsync(
        ExactLifecycleMutationRequest request, CancellationToken ct)
    {
        if (request.ItemKind == LifecycleItemKind.Goal)
            return (await _goalStore.ReadGoalsAsync(ct)).Where(g => g.Id == request.Id)
                .Select(Snap).SingleOrDefault();
        return (await _branchStore.ReadBranchesAsync(ct)).Where(b => b.Id == request.Id)
            .Select(Snap).SingleOrDefault();
    }

    public async Task<bool> ApplyAsync(NIRAMindEvent source,
        ExactLifecycleMutationRequest request, CancellationToken ct)
    {
        if (source.Source != NIRAMindEventSource.User ||
            !LegacyLifecycleMutationRegistration.TryParse(source.Content, out var parsed) ||
            parsed != request)
            throw new InvalidDataException("LIFECYCLE_SOURCE_NOT_DIRECT_USER_EVENT");

        if (request.ItemKind == LifecycleItemKind.Goal)
        {
            var proposal = new NIRAGoalProposal
            {
                Action = request.Mutation switch
                {
                    LifecycleMutationKind.Progress => NIRAGoalProposalAction.RecordProgress,
                    LifecycleMutationKind.Waiting => NIRAGoalProposalAction.SetWaiting,
                    _ => NIRAGoalProposalAction.SetBlocked
                },
                GoalId = request.Id.ToString("D"),
                WaitingFor = request.Mutation == LifecycleMutationKind.Waiting ? request.Detail : null,
                Blocker = request.Mutation == LifecycleMutationKind.Blocked ? request.Detail : null,
                EvidenceSource = NIRAGoalEvidenceSource.CurrentEvent,
                EvidenceQuote = request.Detail,
                EvidenceSummary = request.Detail,
                Reason = "Explicit owner-requested non-dispatching lifecycle mutation.",
                Confidence = 1.0
            };
            var results = await _goals.ApplyProposalsAsync(source, string.Empty,
                string.Empty, [proposal], ct);
            return results.Count == 1 && results[0].Changed && results[0].Goal is not null &&
                results[0].Action == (request.Mutation == LifecycleMutationKind.Progress
                    ? NIRAGoalApplyAction.EvidenceRecorded : NIRAGoalApplyAction.Transitioned);
        }
        else
        {
            var proposal = new NIRABranchProposal
            {
                Action = request.Mutation switch
                {
                    LifecycleMutationKind.Progress => NIRABranchProposalAction.RecordProgress,
                    LifecycleMutationKind.Waiting => NIRABranchProposalAction.SetWaiting,
                    _ => NIRABranchProposalAction.SetBlocked
                },
                BranchId = request.Id.ToString("D"),
                WaitingFor = request.Mutation == LifecycleMutationKind.Waiting ? request.Detail : null,
                Blocker = request.Mutation == LifecycleMutationKind.Blocked ? request.Detail : null,
                EvidenceSource = NIRABranchEvidenceSource.CurrentEvent,
                EvidenceQuote = request.Detail,
                EvidenceSummary = request.Detail,
                Reason = "Explicit owner-requested non-dispatching lifecycle mutation.",
                Confidence = 1.0
            };
            var results = await _branches.ApplyProposalsAsync(source, string.Empty,
                string.Empty, [proposal], ct);
            return results.Count == 1 && results[0].Changed && results[0].Branch is not null &&
                results[0].Action == (request.Mutation == LifecycleMutationKind.Progress
                    ? NIRABranchApplyAction.EvidenceRecorded : NIRABranchApplyAction.Transitioned);
        }
    }
}

public static class LegacyLifecycleMutationRegistration
{
    public const string ConsentValue = "ALLOW_EXPLICIT_LIFECYCLE_MUTATION";
    private const string Id = @"(?<id>[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})";
    private const string Detail = "\"(?<detail>[^\"\r\n]{8,240})\"";
    private static readonly Regex Progress = new(@"^\s*Record\s+progress\s+on\s+(?<type>goal|branch)\s+" + Id +
        @"\s+saying\s+" + Detail + @"\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex Waiting = new(@"^\s*Set\s+(?<type>goal|branch)\s+" + Id +
        @"\s+to\s+Waiting\s+because\s+" + Detail + @"\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex Blocked = new(@"^\s*Block\s+(?<type>goal|branch)\s+" + Id +
        @"\s+because\s+" + Detail + @"\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex Intent = new(@"^\s*(?:Record\s+progress\s+on|Set\s+(?:goal|branch)\b|Block\s+(?:goal|branch)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));

    public static bool TryParse(string? text, out ExactLifecycleMutationRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 550 || text.Any(char.IsControl)) return false;
        var progress = Progress.Match(text);
        var waiting = Waiting.Match(text);
        var blocked = Blocked.Match(text);
        Match match = progress.Success ? progress : waiting.Success ? waiting : blocked;
        if (!match.Success || !Guid.TryParseExact(match.Groups["id"].Value, "D", out Guid id) ||
            id == Guid.Empty) return false;
        string detail = match.Groups["detail"].Value.Trim();
        if (detail.Length is < 8 or > 240) return false;
        request = new ExactLifecycleMutationRequest(
            match.Groups["type"].Value.Equals("goal", StringComparison.OrdinalIgnoreCase)
                ? LifecycleItemKind.Goal : LifecycleItemKind.Branch,
            progress.Success ? LifecycleMutationKind.Progress :
                waiting.Success ? LifecycleMutationKind.Waiting : LifecycleMutationKind.Blocked,
            id, detail);
        return true;
    }

    public static bool CanApply(ExactLifecycleMutationRequest request, LifecycleMutationSnapshot? current)
    {
        if (current is null || current.Id != request.Id || current.ItemKind != request.ItemKind ||
            current.IsResolved) return false;
        return request.Mutation switch
        {
            LifecycleMutationKind.Progress => !string.Equals(current.LastEvidenceSummary,
                request.Detail, StringComparison.Ordinal),
            LifecycleMutationKind.Waiting => current.Status is not ("Waiting" or "Blocked"),
            LifecycleMutationKind.Blocked => current.Status != "Blocked",
            _ => false
        };
    }

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        ILegacyLifecycleMutationExecutor executor, string? consent,
        bool lifecycleConsent, string text, NIRAMindEvent source)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(source);
        if (source.Source != NIRAMindEventSource.User ||
            !string.Equals(source.Content.Trim(), text.Trim(), StringComparison.Ordinal) ||
            consent != ConsentValue || !lifecycleConsent ||
            !TryParse(text, out var parsed) || parsed is null) return [];
        var original = executor.Observe(parsed);
        if (!CanApply(parsed, original)) return [];
        return [new LegacyLifecycleMutationCapability(vault, executor, source, parsed, original!)];
    }

    public static string? ExplainUnavailable(string text, string? consent,
        bool lifecycleConsent, bool registered)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 550 || !Intent.IsMatch(text) || registered)
            return null;
        if (consent != ConsentValue || !lifecycleConsent)
            return "Lifecycle updates are disabled. Launch with -AllowPersonalContext " +
                "-AllowLifecycleContext -AllowLifecycleUpdate. No goal or branch was modified.";
        return "No lifecycle update was made. Use: Record progress on goal|branch <GUID> saying " +
            "\"observed progress\"; Set goal|branch <GUID> to Waiting because \"condition\"; " +
            "or Block goal|branch <GUID> because \"reason\". Existing item must be open; " +
            "blocked items cannot be resumed by this pilot.";
    }
}

public sealed class LegacyLifecycleMutationCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly ILegacyLifecycleMutationExecutor _executor;
    private readonly NIRAMindEvent _source;
    private readonly ExactLifecycleMutationRequest _request;
    private readonly LifecycleMutationSnapshot _original;
    public NIRACapabilityRequest ApprovalRequest { get; }

    public LegacyLifecycleMutationCapability(AgentPayloadVault vault,
        ILegacyLifecycleMutationExecutor executor, NIRAMindEvent source,
        ExactLifecycleMutationRequest request, LifecycleMutationSnapshot original)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _original = original ?? throw new ArgumentNullException(nameof(original));
        if (!LegacyLifecycleMutationRegistration.TryParse(source.Content, out var parsed) ||
            parsed != request || source.Source != NIRAMindEventSource.User ||
            !LegacyLifecycleMutationRegistration.CanApply(request, original))
            throw new ArgumentException("Mutation is not bound to the current direct user event.");
        ApprovalRequest = new NIRACapabilityRequest
        {
            RequestId = Guid.NewGuid(),
            CapabilityId = "lifecycle.item.mutate",
            Arguments = JsonSerializer.SerializeToElement(new
            {
                itemType = request.ItemKind.ToString(),
                itemId = request.Id.ToString("D"),
                operation = request.Mutation.ToString(),
                exactEvidence = request.Detail,
                originalStatus = original.Status,
                originalEvidenceCount = original.EvidenceCount,
                dispatch = "None",
                owner = "ExistingNIRAGoalServiceOrBranchService"
            }),
            Reason = "Direct user-requested non-dispatching lifecycle state change."
        };
    }

    public AgentToolSpec Spec { get; } = new(
        "legacy.lifecycle.item.mutate_exact",
        "Record one owner-approved, exact-user-evidenced progress/waiting/blocker update " +
        "on one existing NIRA goal OR branch. No activation, completion or autonomous dispatch. " +
        "Model arguments MUST be {}.", AccessLevel.Modify);
    public string FunctionName => "nira_update_exact_lifecycle_item";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { }, required = Array.Empty<string>(),
        additionalProperties = false
    });
    public bool ValidateArguments(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object && !args.EnumerateObject().Any();

    public bool IsStillAllowed()
    {
        LifecycleMutationSnapshot? current = _executor.Observe(_request);
        return LegacyLifecycleMutationRegistration.CanApply(_request, current) && current == _original;
    }

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            var args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
            if (!ValidateArguments(args))
                return AgentToolReceipt.ConfirmedNoChange("LIFECYCLE_ARGUMENTS_INVALID");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException)
        { return AgentToolReceipt.ConfirmedNoChange("LIFECYCLE_REFERENCE_INVALID"); }
        if (!IsStillAllowed())
            return AgentToolReceipt.ConfirmedNoChange("LIFECYCLE_ITEM_CHANGED_SINCE_APPROVAL");
        bool changed;
        try { changed = await _executor.ApplyAsync(_source, _request, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return AgentToolReceipt.Uncertain("LIFECYCLE_COMMIT_OUTCOME_UNKNOWN"); }
        if (!changed) return AgentToolReceipt.Uncertain("LIFECYCLE_OWNER_REJECTED_OR_CONFLICT");
        try
        {
            var saved = await _executor.ReadPersistedAsync(_request, ct);
            bool evidenceMatches = saved?.LastEvidenceSummary == _request.Detail;
            bool statusMatches = _request.Mutation switch
            {
                LifecycleMutationKind.Progress => saved?.Status == _original.Status,
                LifecycleMutationKind.Waiting => saved?.Status == "Waiting" && saved.WaitingFor == _request.Detail,
                LifecycleMutationKind.Blocked => saved?.Status == "Blocked" && saved.Blocker == _request.Detail,
                _ => false
            };
            if (saved is null || saved.IsResolved || saved.ItemKind != _request.ItemKind ||
                saved.Id != _request.Id || saved.EvidenceCount != _original.EvidenceCount + 1 ||
                !evidenceMatches || !statusMatches || saved.UpdatedAt < _original.UpdatedAt)
                return AgentToolReceipt.Uncertain("LIFECYCLE_SQLITE_READBACK_MISMATCH");
            var receipt = JsonSerializer.SerializeToElement(new
            {
                outcome = "sqlite_readback_verified",
                itemType = saved.ItemKind.ToString(),
                itemId = saved.Id.ToString("D"),
                operation = _request.Mutation.ToString(),
                status = saved.Status,
                evidence = _request.Detail,
                evidenceCount = saved.EvidenceCount,
                executionDispatched = false,
                owner = "ExistingNIRAGoalServiceOrBranchService",
                verifiedUtc = DateTimeOffset.UtcNow
            });
            string token = await _vault.SaveResultAsync(runId, Spec.Id, receipt, ct);
            return AgentToolReceipt.Verified(token);
        }
        catch (Exception) { return AgentToolReceipt.Uncertain("LIFECYCLE_SQLITE_READBACK_UNKNOWN"); }
    }
}

using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Branches;
using NIRAAgent.Goals;
using NIRAAgent.Mind;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.LegacyAdapters;

/// <summary>
/// Phase 4H.5 — one host-selected, read-only SQLite evidence inspection.
/// Does not use NIRA's mutation/authorization path and cannot dispatch work.
/// </summary>
public enum LifecycleInspectionKind { Goal, Branch }
public sealed record ExactLifecycleInspectRequest(LifecycleInspectionKind Kind, Guid Id);
public sealed record LifecycleInspectResult(
    Guid Id, LifecycleInspectionKind Kind, string Objective, string Status,
    int EvidenceCount, string? LastEvidenceSummary, string? WaitingFor,
    string? Blocker, string? ResultSummary, Guid? GoalId, DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt);

public interface ILegacyLifecycleInspectExecutor
{
    Task<LifecycleInspectResult?> ReadVerifiedAsync(ExactLifecycleInspectRequest request,
        CancellationToken cancellationToken);
}

public sealed class NIRALegacyLifecycleInspectExecutor : ILegacyLifecycleInspectExecutor
{
    private readonly NIRAGoalService _goals;
    private readonly NIRABranchService _branches;
    private readonly NIRAGoalStore _goalStore;
    private readonly NIRABranchStore _branchStore;

    public NIRALegacyLifecycleInspectExecutor(NIRAGoalService goals,
        NIRABranchService branches, NIRAGoalStore goalStore, NIRABranchStore branchStore)
    {
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _goalStore = goalStore ?? throw new ArgumentNullException(nameof(goalStore));
        _branchStore = branchStore ?? throw new ArgumentNullException(nameof(branchStore));
    }

    public async Task<LifecycleInspectResult?> ReadVerifiedAsync(
        ExactLifecycleInspectRequest request, CancellationToken ct)
    {
        if (request.Kind == LifecycleInspectionKind.Goal)
        {
            // The existing goal store owns durable state. A stale or divergent live
            // snapshot must NOT be misrepresented as confirmed SQLite evidence.
            NIRAGoalState? stored = (await _goalStore.ReadGoalsAsync(ct))
                .SingleOrDefault(g => g.Id == request.Id);
            if (stored is null || !_goals.TryGetGoal(request.Id, out NIRAGoalState? live) ||
                live is null || !SameGoal(stored, live)) return null;
            return new(request.Id, request.Kind, stored.Objective,
                stored.Status.ToString(), stored.EvidenceCount, stored.LastEvidenceSummary,
                stored.WaitingFor, stored.Blocker, null, null, stored.UpdatedAt, stored.ResolvedAt);
        }

        NIRABranchState? persisted = (await _branchStore.ReadBranchesAsync(ct))
            .SingleOrDefault(b => b.Id == request.Id);
        if (persisted is null || !_branches.TryGetBranch(request.Id, out NIRABranchState? current) ||
            current is null || !SameBranch(persisted, current)) return null;
        return new(request.Id, request.Kind, persisted.Objective,
            persisted.Status.ToString(), persisted.EvidenceCount, persisted.LastEvidenceSummary,
            persisted.WaitingFor, persisted.Blocker, persisted.ResultSummary,
            persisted.GoalId, persisted.UpdatedAt, persisted.ResolvedAt);
    }

    private static bool SameGoal(NIRAGoalState a, NIRAGoalState b) =>
        a.Id == b.Id && a.Status == b.Status && a.EvidenceCount == b.EvidenceCount &&
        a.LastEvidenceSummary == b.LastEvidenceSummary && a.WaitingFor == b.WaitingFor &&
        a.Blocker == b.Blocker && a.UpdatedAt == b.UpdatedAt;

    private static bool SameBranch(NIRABranchState a, NIRABranchState b) =>
        a.Id == b.Id && a.GoalId == b.GoalId && a.Status == b.Status &&
        a.EvidenceCount == b.EvidenceCount && a.LastEvidenceSummary == b.LastEvidenceSummary &&
        a.WaitingFor == b.WaitingFor && a.Blocker == b.Blocker &&
        a.ResultSummary == b.ResultSummary && a.UpdatedAt == b.UpdatedAt;
}

public static class LegacyLifecycleInspectRegistration
{
    public const string ConsentValue = "ALLOW_EXACT_LIFECYCLE_READBACK";
    private const string Id = @"(?<id>[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})";
    private static readonly Regex Pattern = new(
        @"^\s*Inspect\s+saved\s+(?<type>goal|branch)\s+" + Id +
        @"\s+including\s+status,\s+last\s+progress\s+and\s+evidence\s+count\s*\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex Intent = new(@"^\s*Inspect\s+saved\s+(goal|branch)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));

    public static bool TryParse(string? turn, out ExactLifecycleInspectRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(turn) || turn.Length > 300 ||
            turn.Any(char.IsControl)) return false;
        var match = Pattern.Match(turn);
        if (!match.Success || !Guid.TryParseExact(match.Groups["id"].Value, "D", out Guid id) ||
            id == Guid.Empty) return false;
        request = new(match.Groups["type"].Value.Equals("goal", StringComparison.OrdinalIgnoreCase)
            ? LifecycleInspectionKind.Goal : LifecycleInspectionKind.Branch, id);
        return true;
    }

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        ILegacyLifecycleInspectExecutor executor, string? consent, bool lifecycleConsented,
        string request, NIRAMindEvent mindEvent)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(mindEvent);
        if (consent != ConsentValue || !lifecycleConsented ||
            mindEvent.Source != NIRAMindEventSource.User ||
            !string.Equals(request.Trim(), mindEvent.Content.Trim(), StringComparison.Ordinal) ||
            !TryParse(request, out ExactLifecycleInspectRequest? exact) || exact is null) return [];
        return [new LegacyLifecycleInspectCapability(vault, executor, exact)];
    }

    public static string? ExplainUnavailable(string text, bool registered,
        string? consent, bool lifecycleConsented)
    {
        if (registered || string.IsNullOrWhiteSpace(text) || text.Length > 300 ||
            !Intent.IsMatch(text)) return null;
        if (consent != ConsentValue || !lifecycleConsented)
            return "Saved lifecycle inspection is disabled. Launch with -AllowPersonalContext " +
                "-AllowLifecycleContext -AllowLifecycleInspect. No state was changed.";
        return "No state was changed. Use exactly: Inspect saved branch <GUID> including " +
            "status, last progress and evidence count. The same syntax accepts goal.";
    }
}

public sealed class LegacyLifecycleInspectCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly ILegacyLifecycleInspectExecutor _executor;
    private readonly ExactLifecycleInspectRequest _request;

    public LegacyLifecycleInspectCapability(AgentPayloadVault vault,
        ILegacyLifecycleInspectExecutor executor, ExactLifecycleInspectRequest request)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _request = request ?? throw new ArgumentNullException(nameof(request));
        if (request.Id == Guid.Empty) throw new ArgumentException("Exact ID is required.");
    }

    public AgentToolSpec Spec { get; } = new("legacy.lifecycle.inspect_exact",
        "Read the existing NIRA SQLite lifecycle store for the single exact goal/branch GUID " +
        "provided in this CURRENT user request. Compare live state with stored state. " +
        "Return only bounded status, last evidence, count, and linkage. Read-only: " +
        "never mutate, schedule, activate or complete work. Arguments MUST be {}.", AccessLevel.Observe);
    public string FunctionName => "nira_inspect_exact_saved_lifecycle_item";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { }, required = Array.Empty<string>(),
        additionalProperties = false
    });
    public bool ValidateArguments(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object && !args.EnumerateObject().Any();

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            JsonElement args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
            if (!ValidateArguments(args))
                return AgentToolReceipt.ConfirmedNoChange("LIFECYCLE_INSPECT_ARGUMENTS_INVALID");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException)
        { return AgentToolReceipt.ConfirmedNoChange("LIFECYCLE_INSPECT_REFERENCE_INVALID"); }

        LifecycleInspectResult? saved;
        try { saved = await _executor.ReadVerifiedAsync(_request, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return AgentToolReceipt.ConfirmedNoChange("LIFECYCLE_SQLITE_INSPECTION_UNAVAILABLE"); }
        if (saved is null || saved.Id != _request.Id || saved.Kind != _request.Kind ||
            saved.EvidenceCount < 0 || saved.EvidenceCount > 1_000_000 ||
            saved.UpdatedAt == default)
            return AgentToolReceipt.ConfirmedNoChange("LIFECYCLE_NOT_FOUND_OR_SNAPSHOT_MISMATCH");
        try
        {
            var receipt = JsonSerializer.SerializeToElement(new
            {
                source = "authoritative_existing_nira_sqlite_store",
                verifiedAgainstLiveState = true,
                inspectionOnly = true,
                itemType = saved.Kind.ToString(),
                itemId = saved.Id.ToString("D"),
                goalId = saved.GoalId?.ToString("D"),
                objective = Clean(saved.Objective, 180),
                status = saved.Status,
                evidenceCount = saved.EvidenceCount,
                lastEvidenceSummary = Clean(saved.LastEvidenceSummary, 280),
                waitingFor = Clean(saved.WaitingFor, 200),
                blocker = Clean(saved.Blocker, 200),
                resultSummary = Clean(saved.ResultSummary, 220),
                updatedUtc = saved.UpdatedAt.ToUniversalTime(),
                resolvedUtc = saved.ResolvedAt?.ToUniversalTime(),
                observedUtc = DateTimeOffset.UtcNow,
                conclusion = "Stored metadata observed; last evidence text is user-attested, " +
                    "not independent proof that the task was performed."
            });
            string token = await _vault.SaveResultAsync(runId, Spec.Id, receipt, ct);
            return AgentToolReceipt.ReadOnlyResult(token);
        }
        catch (Exception) { return AgentToolReceipt.ConfirmedNoChange("LIFECYCLE_READBACK_RECEIPT_UNAVAILABLE"); }
    }

    private static string Clean(string? source, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;
        var chars = new List<char>();
        bool space = false;
        foreach (char ch in source)
        {
            if (chars.Count >= maxChars) break;
            if (char.IsControl(ch) || char.IsWhiteSpace(ch)) { space = chars.Count > 0; continue; }
            if (space && chars.Count < maxChars) chars.Add(' ');
            space = false;
            if (chars.Count < maxChars) chars.Add(ch);
        }
        return new string(chars.ToArray()).Trim();
    }
}

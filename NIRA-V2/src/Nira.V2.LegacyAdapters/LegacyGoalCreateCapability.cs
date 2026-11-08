using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Capabilities;
using NIRAAgent.Goals;
using NIRAAgent.Mind;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.LegacyAdapters;

/// <summary>One production state owner: the existing NIRAGoalService.
/// V2 does not open a second goal store or assign persisted goal identities.</summary>
public interface ILegacyGoalCreateExecutor
{
    bool HasOpenGoal(string objective);
    Task<NIRAGoalApplyResult> CreateAsync(NIRAMindEvent sourceEvent,
        string objective, string criterion, CancellationToken cancellationToken);
    Task<NIRAGoalState?> ReadPersistedGoalAsync(Guid goalId, CancellationToken cancellationToken);
}

public sealed class NIRALegacyGoalCreateExecutor : ILegacyGoalCreateExecutor
{
    private readonly NIRAGoalService _goals;
    private readonly NIRAGoalStore _store;
    public NIRALegacyGoalCreateExecutor(NIRAGoalService goals, NIRAGoalStore store)
    {
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public bool HasOpenGoal(string objective) => _goals.CurrentGoals.Any(g =>
        g.IsOpen && string.Equals(g.Objective, objective, StringComparison.OrdinalIgnoreCase));

    public async Task<NIRAGoalState?> ReadPersistedGoalAsync(Guid goalId,
        CancellationToken cancellationToken) =>
        (await _store.ReadGoalsAsync(cancellationToken))
            .SingleOrDefault(goal => goal.Id == goalId);

    public async Task<NIRAGoalApplyResult> CreateAsync(NIRAMindEvent sourceEvent,
        string objective, string criterion, CancellationToken cancellationToken)
    {
        // The quoted objective is literally in the direct user event; no model
        // summary, previous conversation, or tool result becomes write evidence.
        var proposal = new NIRAGoalProposal
        {
            Action = NIRAGoalProposalAction.Create,
            Objective = objective,
            CompletionCriteria = [criterion],
            EvidenceSource = NIRAGoalEvidenceSource.CurrentEvent,
            EvidenceQuote = objective,
            EvidenceSummary = "The user explicitly requested this exact persistent goal.",
            Reason = "Exact user-created goal, awaiting separately authorized activation.",
            WaitingFor = "Awaiting explicit user activation; no autonomous work is authorized.",
            Confidence = 1.0
        };
        IReadOnlyList<NIRAGoalApplyResult> results = await _goals.ApplyProposalsAsync(
            sourceEvent, string.Empty, string.Empty, [proposal], cancellationToken);
        if (results.Count != 1) throw new InvalidDataException("GOAL_OWNER_RECEIPT_COUNT_INVALID");
        return results[0];
    }
}

public sealed record ExactGoalCreateRequest(string Objective, string Criterion);

/// <summary>Exact current-turn operation. No regex guess of an implied user intent.
/// This is deliberately stricter than ordinary NIRA planning until lifecycle
/// authorization, recovery, and event ownership pass live acceptance.</summary>
public static class LegacyGoalCreateRegistration
{
    public const string ConsentValue = "ALLOW_EXPLICIT_GOAL_CREATE";
    private static readonly Regex Pattern = new(
        "^\\s*Create\\s+(?:a\\s+)?goal\\s+\\\"(?<goal>[^\\\"\\r\\n]{1,180})\\\"\\s+with\\s+(?:success\\s+)?criterion\\s+\\\"(?<criterion>[^\\\"\\r\\n]{1,180})\\\"\\s*\\.?\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex Intent = new("^\\s*Create\\s+(?:a\\s+)?goal\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));

    public static bool TryParse(string? request, out ExactGoalCreateRequest? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(request) || request.Length > 550 ||
            request.Any(c => char.IsControl(c))) return false;
        Match match = Pattern.Match(request);
        if (!match.Success) return false;
        string objective = match.Groups["goal"].Value.Trim();
        string criterion = match.Groups["criterion"].Value.Trim();
        if (objective.Length is < 5 or > 180 || criterion.Length is < 5 or > 180 ||
            objective.StartsWith("do not ", StringComparison.OrdinalIgnoreCase) ||
            objective.Equals(criterion, StringComparison.OrdinalIgnoreCase) ||
            objective.Any(char.IsControl) || criterion.Any(char.IsControl)) return false;
        parsed = new ExactGoalCreateRequest(objective, criterion);
        return true;
    }

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        ILegacyGoalCreateExecutor executor, string? consent,
        bool lifecycleReadConsented, string request, NIRAMindEvent mindEvent)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(mindEvent);
        if (mindEvent.Source != NIRAMindEventSource.User ||
            !string.Equals(consent, ConsentValue, StringComparison.Ordinal) ||
            !lifecycleReadConsented ||
            !string.Equals(request.Trim(), mindEvent.Content.Trim(), StringComparison.Ordinal) ||
            !TryParse(request, out ExactGoalCreateRequest? parsed) || parsed is null ||
            executor.HasOpenGoal(parsed.Objective)) return [];
        return [new LegacyGoalCreateCapability(vault, executor, mindEvent, parsed)];
    }

    public static string? ExplainUnavailable(string request, string? consent,
        bool lifecycleReadConsented, bool registered, ILegacyGoalCreateExecutor executor)
    {
        if (string.IsNullOrWhiteSpace(request) || request.Length > 550 ||
            !Intent.IsMatch(request)) return null;
        if (registered) return null;
        if (!string.Equals(consent, ConsentValue, StringComparison.Ordinal) ||
            !lifecycleReadConsented)
            return "Goal creation is disabled for this launch. Enable -AllowPersonalContext, " +
                "-AllowLifecycleContext and -AllowGoalCreate to use exact, owner-approved goal creation.";
        if (!TryParse(request, out ExactGoalCreateRequest? parsed) || parsed is null)
            return "For this guarded stage use: Create goal \"exact objective\" with " +
                "success criterion \"specific evidence of completion\". No goal was changed.";
        return executor.HasOpenGoal(parsed.Objective)
            ? "An open goal with this exact objective already exists in the authoritative " +
              "goal state. No duplicate goal was created. Review the Goals tab."
            : "The current user event did not qualify for exact goal creation. No goal was changed.";
    }
}

public sealed class LegacyGoalCreateCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly ILegacyGoalCreateExecutor _executor;
    private readonly NIRAMindEvent _sourceEvent;
    private readonly ExactGoalCreateRequest _request;
    public NIRACapabilityRequest ApprovalRequest { get; }

    public LegacyGoalCreateCapability(AgentPayloadVault vault,
        ILegacyGoalCreateExecutor executor, NIRAMindEvent sourceEvent,
        ExactGoalCreateRequest request)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _sourceEvent = sourceEvent ?? throw new ArgumentNullException(nameof(sourceEvent));
        _request = request ?? throw new ArgumentNullException(nameof(request));
        if (_sourceEvent.Source != NIRAMindEventSource.User ||
            !LegacyGoalCreateRegistration.TryParse(_sourceEvent.Content, out ExactGoalCreateRequest? parsed) ||
            parsed != _request) throw new ArgumentException("Untrusted goal source event.");
        ApprovalRequest = new NIRACapabilityRequest
        {
            RequestId = Guid.NewGuid(),
            CapabilityId = "lifecycle.goal.create",
            Arguments = JsonSerializer.SerializeToElement(new
            {
                operation = "create_persistent_goal",
                objective = _request.Objective,
                successCriterion = _request.Criterion,
                initialStatus = "Waiting",
                execution = "Not activated; no background work scheduled",
                owner = "Existing NIRAGoalService"
            }),
            Reason = "User explicitly requested this exact goal with a completion criterion."
        };
    }

    public AgentToolSpec Spec { get; } = new(
        "legacy.lifecycle.goal.create_exact",
        "Create ONE exact current-user-request persistent goal with its stated success " +
        "criterion using the existing NIRAGoalService. New goal starts WAITING and does " +
        "not run automatically. Requires fresh host Allow Once. Empty arguments only.",
        AccessLevel.Modify);
    public string FunctionName => "nira_create_exact_goal";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { }, required = Array.Empty<string>(),
        additionalProperties = false
    });
    public bool ValidateArguments(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object &&
        !arguments.EnumerateObject().Any();

    public bool IsSafeToCreate() => !_executor.HasOpenGoal(_request.Objective);

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            JsonElement args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
            if (!ValidateArguments(args)) return AgentToolReceipt.ConfirmedNoChange("GOAL_ARGUMENTS_INVALID");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException)
        { return AgentToolReceipt.ConfirmedNoChange("GOAL_REFERENCE_INVALID"); }
        if (!IsSafeToCreate())
            return AgentToolReceipt.ConfirmedNoChange("GOAL_ALREADY_EXISTS_OR_CHANGED");

        NIRAGoalApplyResult result;
        try
        {
            result = await _executor.CreateAsync(_sourceEvent,
                _request.Objective, _request.Criterion, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return AgentToolReceipt.Uncertain("GOAL_COMMIT_OUTCOME_UNKNOWN"); }

        if (result.Action == NIRAGoalApplyAction.Rejected ||
            result.Action == NIRAGoalApplyAction.NoChange)
            return AgentToolReceipt.ConfirmedNoChange("GOAL_OWNER_REJECTED");
        if (result.Action is not (NIRAGoalApplyAction.Created or NIRAGoalApplyAction.Duplicate) ||
            result.Goal is null)
            return AgentToolReceipt.Uncertain("GOAL_OWNER_RECEIPT_UNEXPECTED");

        // Independent lookup from the authoritative current state, not a model
        // assertion, a locally constructed object, or a second V2 persistence lane.
        try
        {
            NIRAGoalState? stored = await _executor.ReadPersistedGoalAsync(
                result.Goal.Id, ct);
            if (stored is null || stored.Id == Guid.Empty || !stored.IsOpen ||
                !string.Equals(stored.Objective, _request.Objective, StringComparison.Ordinal) ||
                !stored.CompletionCriteria.Contains(_request.Criterion, StringComparer.Ordinal) ||
                (result.Action == NIRAGoalApplyAction.Created &&
                 (stored.SourceEventId != _sourceEvent.Id ||
                  stored.Status != NIRAGoalStatus.Waiting ||
                  stored.NextWakeAtUtc is not null ||
                  string.IsNullOrWhiteSpace(stored.WaitingFor))))
                return AgentToolReceipt.Uncertain("GOAL_READBACK_MISMATCH");

            JsonElement evidence = JsonSerializer.SerializeToElement(new
            {
                outcome = result.Action == NIRAGoalApplyAction.Created
                    ? "created_and_readback_verified" : "equivalent_goal_already_exists",
                goalId = stored.Id.ToString("D"),
                objective = stored.Objective,
                status = stored.Status.ToString(),
                completionCriterion = _request.Criterion,
                changed = result.Action == NIRAGoalApplyAction.Created,
                owner = "Existing NIRAGoalService",
                evidence = "authoritative_postcommit_readback",
                observedUtc = DateTimeOffset.UtcNow
            });
            string token = await _vault.SaveResultAsync(runId, Spec.Id, evidence, ct);
            return AgentToolReceipt.Verified(token);
        }
        catch (Exception)
        { return AgentToolReceipt.Uncertain("GOAL_READBACK_REQUIRES_RECONCILIATION"); }
    }
}

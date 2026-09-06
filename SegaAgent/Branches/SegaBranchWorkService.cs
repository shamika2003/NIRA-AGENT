/*
 * filename: SegaBranchWorkService.cs
 */

using System.Diagnostics;
using System.Text;

using Microsoft.Extensions.Hosting;

using SegaAgent.Capabilities;
using SegaAgent.Goals;
using SegaAgent.Mind;

namespace SegaAgent.Branches;

public sealed class SegaBranchWorkService
    : IHostedService
{
    private const double MinimumAssignmentConfidence =
        0.72;

    private readonly SegaBranchWorkStore
        _store;

    private readonly SegaBranchService
        _branches;

    private readonly SegaGoalService
        _goals;

    private readonly SegaCapabilityRegistry
        _capabilityRegistry;

    private readonly SemaphoreSlim
        _mutationLock =
            new(
                1,
                1);

    private readonly object
        _stateSync =
            new();

    private Dictionary<Guid, SegaBranchWorkItem>
        _work =
            new();


    // Read-only observers can project current branch activity without polling
    // the SQLite store. The branch runtime remains the only owner of work state.
    public event Action?
        StateChanged;


    public SegaBranchWorkService(
        SegaBranchWorkStore store,
        SegaBranchService branches,
        SegaGoalService goals,
        SegaCapabilityRegistry capabilityRegistry)
    {
        _store =
            store
            ?? throw new ArgumentNullException(
                nameof(store));

        _branches =
            branches
            ?? throw new ArgumentNullException(
                nameof(branches));

        _goals =
            goals
            ?? throw new ArgumentNullException(
                nameof(goals));

        _capabilityRegistry =
            capabilityRegistry
            ?? throw new ArgumentNullException(
                nameof(capabilityRegistry));
    }

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(
            cancellationToken);

        IReadOnlyList<SegaBranchWorkItem> loaded =
            await _store.ReadAllAsync(
                cancellationToken);

        Dictionary<Guid, SegaBranchWorkItem> state =
            loaded.ToDictionary(
                item => item.Id,
                item => item);

        // A process restart cannot honestly resume an in-process capability
        // or dynamic-tool invocation from the exact instruction pointer.
        // Mark it interrupted and let Sega inspect/reason before retrying.
        SegaBranchWorkItem[] interrupted =
            state.Values
                .Where(
                    item =>
                        item.Status ==
                        SegaBranchWorkStatus.Running)
                .ToArray();

        foreach (SegaBranchWorkItem item in interrupted)
        {
            SegaBranchWorkItem recovered =
                item with
                {
                    Status =
                        SegaBranchWorkStatus.Interrupted,

                    ResultSummary =
                        "Assigned branch work was interrupted because Sega restarted before the runtime recorded a terminal result.",

                    ResultEvidence =
                        "The previous work item was Running when the Sega process restarted. Do not assume whether its external side effects completed; inspect authoritative PC state before retrying.",

                    FinishedAtUtc =
                        DateTimeOffset.UtcNow,

                    ResultNotifiedAtUtc =
                        null
                };

            recovered =
                recovered.Normalize();

            await _store.UpsertAsync(
                recovered,
                cancellationToken);

            state[recovered.Id] =
                recovered;
        }

        lock (_stateSync)
        {
            _work =
                state;
        }

        Debug.WriteLine(
            $"[BranchWork] READY | " +
            $"Open={state.Values.Count(item => item.IsOpen)} | " +
            $"Pending={state.Values.Count(item => item.Status == SegaBranchWorkStatus.Pending)} | " +
            $"Running={state.Values.Count(item => item.Status == SegaBranchWorkStatus.Running)} | " +
            $"InterruptedRecovered={interrupted.Length} | " +
            $"Database='{_store.DatabasePath}'");
    }

    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public IReadOnlyList<SegaBranchWorkItem> CurrentWork
    {
        get
        {
            lock (_stateSync)
            {
                return _work.Values
                    .OrderByDescending(
                        item => item.IsOpen)
                    .ThenByDescending(
                        item => item.CreatedAtUtc)
                    .ToArray();
            }
        }
    }

    public bool TryGetWork(
        Guid workId,
        out SegaBranchWorkItem? work)
    {
        lock (_stateSync)
        {
            return _work.TryGetValue(
                workId,
                out work);
        }
    }


    public string BuildCognitionContext()
    {
        SegaBranchWorkItem[] open;
        SegaBranchWorkItem[] recent;

        lock (_stateSync)
        {
            open =
                _work.Values
                    .Where(
                        item => item.IsOpen)
                    .OrderBy(
                        item => item.CreatedAtUtc)
                    .Take(
                        32)
                    .ToArray();

            recent =
                _work.Values
                    .Where(
                        item => item.IsTerminal)
                    .OrderByDescending(
                        item => item.FinishedAtUtc
                            ?? item.CreatedAtUtc)
                    .Take(
                        24)
                    .ToArray();
        }

        StringBuilder builder =
            new();

        builder.AppendLine(
            "SEGA BRANCH-OWNED ASSIGNED WORK");
        builder.AppendLine();
        builder.AppendLine(
            "A branch is a responsibility/work basket. Sega cognition assigns bounded capability or dynamic-tool work to it. The branch itself does not reason, choose tools, or decide the next step.");
        builder.AppendLine();

        builder.AppendLine(
            "OPEN ASSIGNED WORK");

        if (open.Length == 0)
        {
            builder.AppendLine(
                "- No branch currently has assigned runtime work in flight.");
        }
        else
        {
            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            foreach (SegaBranchWorkItem item in open)
            {
                string elapsed =
                    item.Status == SegaBranchWorkStatus.Running &&
                    item.StartedAtUtc.HasValue
                        ? FormatElapsed(
                            now - item.StartedAtUtc.Value)
                        : "-";

                builder.AppendLine(
                    $"- work={item.Id:D} | branch={item.BranchId:D} | goal={item.GoalId:D} | " +
                    $"kind={item.Kind} | status={item.Status} | elapsed={elapsed} | reason={item.Reason}");

                AppendPayloadSummary(
                    builder,
                    item);
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "RECENT ASSIGNED WORK RESULTS");

        if (recent.Length == 0)
        {
            builder.AppendLine(
                "- No resolved branch-owned work yet.");
        }
        else
        {
            foreach (SegaBranchWorkItem item in recent)
            {
                builder.AppendLine(
                    $"- work={item.Id:D} | branch={item.BranchId:D} | goal={item.GoalId:D} | " +
                    $"kind={item.Kind} | status={item.Status}");

                if (!string.IsNullOrWhiteSpace(
                        item.ResultSummary))
                {
                    builder.AppendLine(
                        $"  result: {item.ResultSummary}");
                }
            }
        }

        return builder
            .ToString()
            .Trim();
    }

    public SegaBranchWorkReconsiderationSnapshot? BuildReconsiderationSnapshot(
        Guid goalId,
        int sequence,
        DateTimeOffset observedAtUtc)
    {
        if (goalId == Guid.Empty)
        {
            return null;
        }

        if (!_goals.TryGetGoal(
                goalId,
                out SegaGoalState? goal)
            ||
            goal == null
            ||
            goal.IsResolved)
        {
            return null;
        }

        SegaBranchWorkItem[] running;

        lock (_stateSync)
        {
            running =
                _work.Values
                    .Where(
                        item =>
                            item.GoalId == goalId
                            &&
                            item.Status == SegaBranchWorkStatus.Running
                            &&
                            item.StartedAtUtc.HasValue)
                    .OrderBy(
                        item =>
                            item.StartedAtUtc)
                    .Take(
                        32)
                    .ToArray();
        }

        if (running.Length == 0)
        {
            return null;
        }

        List<SegaBranchWorkReconsiderationEntry> entries =
            new();

        foreach (SegaBranchWorkItem item in running)
        {
            _branches.TryGetBranch(
                item.BranchId,
                out SegaBranchState? branch);

            entries.Add(
                new SegaBranchWorkReconsiderationEntry
                {
                    WorkId =
                        item.Id,

                    BranchId =
                        item.BranchId,

                    GoalId =
                        item.GoalId,

                    Kind =
                        item.Kind,

                    BranchObjective =
                        branch?.Objective
                        ?? "Unknown branch responsibility",

                    WorkReason =
                        item.Reason,

                    StartedAtUtc =
                        item.StartedAtUtc!.Value
                });
        }

        return new SegaBranchWorkReconsiderationSnapshot
        {
            GoalId =
                goalId,

            Sequence =
                Math.Max(
                    1,
                    sequence),

            ObservedAtUtc =
                observedAtUtc == default
                    ? DateTimeOffset.UtcNow
                    : observedAtUtc,

            RunningWork =
                entries
        };
    }


    public async Task<IReadOnlyList<SegaBranchWorkApplyResult>>
        ApplyProposalsAsync(
            SegaMindEvent mindEvent,
            IReadOnlyList<SegaBranchWorkProposal> proposals,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);

        if (proposals == null ||
            proposals.Count == 0)
        {
            return Array.Empty<SegaBranchWorkApplyResult>();
        }

        List<SegaBranchWorkApplyResult> results =
            new();

        await _mutationLock.WaitAsync(
            cancellationToken);

        try
        {
            foreach (SegaBranchWorkProposal raw in proposals)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SegaBranchWorkProposal proposal;

                try
                {
                    proposal =
                        raw.Normalize();
                }
                catch (Exception ex)
                {
                    results.Add(
                        Reject(
                            $"Malformed branch work proposal: {ex.Message}"));
                    continue;
                }

                SegaBranchWorkApplyResult result =
                    await ApplyOneAsync(
                        mindEvent,
                        proposal,
                        cancellationToken);

                results.Add(
                    result);

                Debug.WriteLine(
                    $"[BranchWork] {result.Action.ToString().ToUpperInvariant()} | " +
                    $"Work={result.Work?.Id.ToString("D") ?? "-"} | " +
                    $"Branch={result.Work?.BranchId.ToString("D") ?? proposal.BranchId} | " +
                    $"Kind={result.Work?.Kind.ToString() ?? proposal.Kind.ToString()} | " +
                    $"Reason='{TrimLog(result.Reason)}'");
            }
        }
        finally
        {
            _mutationLock.Release();
        }

        return results;
    }

    public IReadOnlyList<SegaBranchWorkItem> GetRunnableWork(
        int maximumItems = 8)
    {
        maximumItems =
            Math.Clamp(
                maximumItems,
                1,
                32);

        SegaBranchWorkItem[] candidates;

        lock (_stateSync)
        {
            candidates =
                _work.Values
                    .Where(
                        item =>
                            item.Status ==
                            SegaBranchWorkStatus.Pending)
                    .OrderBy(
                        item => item.CreatedAtUtc)
                    .Take(
                        maximumItems * 2)
                    .ToArray();
        }

        return candidates
            .Where(
                item =>
                    IsBranchReadyForWork(
                        item.BranchId))
            .Take(
                maximumItems)
            .ToArray();
    }

    public IReadOnlyList<SegaBranchWorkItem> GetUnnotifiedTerminalWork(
        int maximumItems = 32)
    {
        maximumItems =
            Math.Clamp(
                maximumItems,
                1,
                128);

        lock (_stateSync)
        {
            return _work.Values
                .Where(
                    item =>
                        item.IsTerminal
                        &&
                        !item.ResultNotifiedAtUtc.HasValue)
                .OrderBy(
                    item => item.FinishedAtUtc
                        ?? item.CreatedAtUtc)
                .Take(
                    maximumItems)
                .ToArray();
        }
    }

    public async Task<SegaBranchWorkItem?> MarkRunningAsync(
        Guid workId,
        CancellationToken cancellationToken = default)
    {
        await _mutationLock.WaitAsync(
            cancellationToken);

        try
        {
            SegaBranchWorkItem? existing =
                ReadUnsafe(
                    workId);

            if (existing == null ||
                existing.Status != SegaBranchWorkStatus.Pending)
            {
                return null;
            }

            if (!IsBranchReadyForWork(
                    existing.BranchId))
            {
                return null;
            }

            SegaBranchWorkItem updated =
                (existing with
                {
                    Status =
                        SegaBranchWorkStatus.Running,

                    StartedAtUtc =
                        DateTimeOffset.UtcNow
                })
                .Normalize();

            await PersistAsync(
                updated,
                cancellationToken);

            return updated;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<SegaBranchWorkItem?> CompleteAsync(
        Guid workId,
        SegaBranchWorkStatus status,
        string summary,
        string evidence,
        CancellationToken cancellationToken = default)
    {
        if (status is
            SegaBranchWorkStatus.Pending
            or SegaBranchWorkStatus.Running)
        {
            throw new InvalidOperationException(
                "CompleteAsync requires a terminal branch-work status.");
        }

        await _mutationLock.WaitAsync(
            cancellationToken);

        try
        {
            SegaBranchWorkItem? existing =
                ReadUnsafe(
                    workId);

            if (existing == null ||
                existing.IsTerminal)
            {
                return existing;
            }

            SegaBranchWorkItem updated =
                (existing with
                {
                    Status =
                        status,

                    ResultSummary =
                        summary,

                    ResultEvidence =
                        evidence,

                    FinishedAtUtc =
                        DateTimeOffset.UtcNow,

                    ResultNotifiedAtUtc =
                        null
                })
                .Normalize();

            await PersistAsync(
                updated,
                cancellationToken);

            return updated;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task MarkResultNotifiedAsync(
        Guid workId,
        CancellationToken cancellationToken = default)
    {
        await _mutationLock.WaitAsync(
            cancellationToken);

        try
        {
            SegaBranchWorkItem? existing =
                ReadUnsafe(
                    workId);

            if (existing == null ||
                !existing.IsTerminal ||
                existing.ResultNotifiedAtUtc.HasValue)
            {
                return;
            }

            SegaBranchWorkItem updated =
                (existing with
                {
                    ResultNotifiedAtUtc =
                        DateTimeOffset.UtcNow
                })
                .Normalize();

            await PersistAsync(
                updated,
                cancellationToken);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task CancelPendingForBranchAsync(
        Guid branchId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await _mutationLock.WaitAsync(
            cancellationToken);

        try
        {
            SegaBranchWorkItem[] pending;

            lock (_stateSync)
            {
                pending =
                    _work.Values
                        .Where(
                            item =>
                                item.BranchId == branchId
                                &&
                                item.Status == SegaBranchWorkStatus.Pending)
                        .ToArray();
            }

            foreach (SegaBranchWorkItem item in pending)
            {
                SegaBranchWorkItem updated =
                    (item with
                    {
                        Status =
                            SegaBranchWorkStatus.Cancelled,

                        ResultSummary =
                            string.IsNullOrWhiteSpace(reason)
                                ? "Branch resolved before this assigned work started."
                                : reason.Trim(),

                        ResultEvidence =
                            "The work item was cancelled before dispatch because its owning branch was resolved.",

                        FinishedAtUtc =
                            DateTimeOffset.UtcNow,

                        // Branch resolution already causes its own parent event.
                        // Avoid an unnecessary second wake for never-started work.
                        ResultNotifiedAtUtc =
                            DateTimeOffset.UtcNow
                    })
                    .Normalize();

                await PersistAsync(
                    updated,
                    cancellationToken);
            }
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public SegaBranchWorkResultEvent BuildResultEvent(
        SegaBranchWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(
            item);

        _branches.TryGetBranch(
            item.BranchId,
            out SegaBranchState? branch);

        return new SegaBranchWorkResultEvent
        {
            WorkId =
                item.Id,

            BranchId =
                item.BranchId,

            GoalId =
                item.GoalId,

            Kind =
                item.Kind,

            Status =
                item.Status,

            BranchObjective =
                branch?.Objective
                ?? "Unknown branch responsibility",

            WorkReason =
                item.Reason,

            ResultSummary =
                item.ResultSummary
                ?? string.Empty,

            ResultEvidence =
                item.ResultEvidence
                ?? string.Empty,

            FinishedAtUtc =
                item.FinishedAtUtc
                ?? DateTimeOffset.UtcNow
        };
    }

    private async Task<SegaBranchWorkApplyResult> ApplyOneAsync(
        SegaMindEvent mindEvent,
        SegaBranchWorkProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence <
            MinimumAssignmentConfidence)
        {
            return Reject(
                "Branch work assignment confidence is below the runtime threshold.");
        }

        if (!Guid.TryParse(
                proposal.BranchId,
                out Guid branchId)
            ||
            branchId == Guid.Empty)
        {
            return Reject(
                "Branch work requires an exact authoritative branch GUID.");
        }

        if (!_branches.TryGetBranch(
                branchId,
                out SegaBranchState? branch)
            ||
            branch == null)
        {
            return Reject(
                "The requested branch does not exist in authoritative branch state.");
        }

        if (branch.IsResolved)
        {
            return Reject(
                "Resolved branches cannot receive new work.");
        }

        if (!_goals.TryGetGoal(
                branch.GoalId,
                out SegaGoalState? goal)
            ||
            goal == null
            ||
            goal.IsResolved)
        {
            return Reject(
                "The owning persistent goal is no longer open, so this branch cannot receive executable work.");
        }

        if (branch.Status is
            SegaBranchStatus.Waiting
            or SegaBranchStatus.Blocked)
        {
            return Reject(
                $"Branch is {branch.Status}; Sega must explicitly reactivate/revise it before assigning new work.");
        }

        if (!AreDependenciesComplete(
                branch))
        {
            return Reject(
                "Branch prerequisites are not complete, so assigned work cannot start yet.");
        }

        lock (_stateSync)
        {
            SegaBranchWorkItem? open =
                _work.Values
                    .FirstOrDefault(
                        item =>
                            item.BranchId == branchId
                            &&
                            item.IsOpen);

            if (open != null)
            {
                return new SegaBranchWorkApplyResult
                {
                    Action =
                        SegaBranchWorkApplyAction.Duplicate,

                    Work =
                        open,

                    Reason =
                        "This branch already has assigned work in flight. Wait for its authoritative result before choosing the next branch step."
                };
            }

            // The latest terminal assignment is the authoritative answer to
            // the immediately preceding branch step. Internal result/timer
            // cognition may not turn the same executable payload into new
            // work simply by changing its prose reason. A later user event
            // may intentionally request a retry, and a materially different
            // intervening branch step naturally changes what is latest.
            SegaBranchWorkItem? latest =
                _work.Values
                    .Where(
                        item =>
                            item.BranchId == branchId)
                    .OrderByDescending(
                        item =>
                            item.CreatedAtUtc)
                    .ThenByDescending(
                        item =>
                            item.FinishedAtUtc
                            ?? item.CreatedAtUtc)
                    .FirstOrDefault();

            if (latest != null
                &&
                latest.IsTerminal
                &&
                mindEvent.Source != SegaMindEventSource.User
                &&
                string.Equals(
                    latest.BuildExecutionSignature(),
                    proposal.BuildExecutionSignature(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return new SegaBranchWorkApplyResult
                {
                    Action =
                        SegaBranchWorkApplyAction.Rejected,

                    Work =
                        latest,

                    Reason =
                        $"The same executable branch step already ended as {latest.Status}. Internal cognition must reason from that authoritative result instead of recreating it unchanged."
                };
            }
        }

        if (proposal.Kind == SegaBranchWorkKind.Capability)
        {
            string capabilityId =
                proposal.CapabilityRequest!
                    .CapabilityId;

            if (!_capabilityRegistry.TryResolve(
                    capabilityId,
                    out _))
            {
                return Reject(
                    $"Unknown trusted primitive capability '{capabilityId}'.");
            }
        }
        else
        {
            if (!Guid.TryParse(
                    proposal.DynamicToolInvocation!
                        .ToolId,
                    out Guid toolId)
                ||
                toolId == Guid.Empty)
            {
                return Reject(
                    "Dynamic-tool branch work requires an exact authoritative dynamic tool GUID.");
            }
        }

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        SegaBranchWorkItem item =
            new SegaBranchWorkItem
            {
                Id =
                    Guid.NewGuid(),

                BranchId =
                    branch.Id,

                GoalId =
                    branch.GoalId,

                Kind =
                    proposal.Kind,

                Status =
                    SegaBranchWorkStatus.Pending,

                CapabilityRequest =
                    proposal.CapabilityRequest,

                DynamicToolInvocation =
                    proposal.DynamicToolInvocation,

                Reason =
                    proposal.Reason,

                CreatedAtUtc =
                    now
            }
            .Normalize();

        await PersistAsync(
            item,
            cancellationToken);

        return new SegaBranchWorkApplyResult
        {
            Action =
                SegaBranchWorkApplyAction.Queued,

            Work =
                item,

            Reason =
                "Bounded work was assigned to the branch. The branch runner will execute it without inventing the next step."
        };
    }

    private bool IsBranchReadyForWork(
        Guid branchId)
    {
        if (!_branches.TryGetBranch(
                branchId,
                out SegaBranchState? branch)
            ||
            branch == null
            ||
            branch.IsResolved)
        {
            return false;
        }

        if (!_goals.TryGetGoal(
                branch.GoalId,
                out SegaGoalState? goal)
            ||
            goal == null
            ||
            goal.IsResolved)
        {
            return false;
        }

        if (branch.Status is
            SegaBranchStatus.Waiting
            or SegaBranchStatus.Blocked)
        {
            return false;
        }

        return AreDependenciesComplete(
            branch);
    }

    private bool AreDependenciesComplete(
        SegaBranchState branch)
    {
        foreach (Guid dependencyId in branch.DependsOnBranchIds)
        {
            if (!_branches.TryGetBranch(
                    dependencyId,
                    out SegaBranchState? dependency)
                ||
                dependency?.Status != SegaBranchStatus.Completed)
            {
                return false;
            }
        }

        return true;
    }

    private SegaBranchWorkItem? ReadUnsafe(
        Guid workId)
    {
        lock (_stateSync)
        {
            _work.TryGetValue(
                workId,
                out SegaBranchWorkItem? value);

            return value;
        }
    }

    private async Task PersistAsync(
        SegaBranchWorkItem item,
        CancellationToken cancellationToken)
    {
        SegaBranchWorkItem normalized =
            item.Normalize();

        await _store.UpsertAsync(
            normalized,
            cancellationToken);

        lock (_stateSync)
        {
            _work[normalized.Id] =
                normalized;
        }


        StateChanged?.Invoke();
    }

    private static void AppendPayloadSummary(
        StringBuilder builder,
        SegaBranchWorkItem item)
    {
        switch (item.Kind)
        {
            case SegaBranchWorkKind.Capability:
                builder.AppendLine(
                    $"  capability: {item.CapabilityRequest?.CapabilityId ?? "-"}");
                break;

            case SegaBranchWorkKind.DynamicTool:
                builder.AppendLine(
                    $"  dynamic tool id: {item.DynamicToolInvocation?.ToolId ?? "-"}");
                break;
        }
    }

    private static SegaBranchWorkApplyResult Reject(
        string reason)
    {
        return new SegaBranchWorkApplyResult
        {
            Action =
                SegaBranchWorkApplyAction.Rejected,

            Reason =
                reason
        };
    }

    private static string FormatElapsed(
        TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed =
                TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes <
            1.0)
        {
            return $"{Math.Max(0, (int)elapsed.TotalSeconds)}s";
        }

        if (elapsed.TotalHours <
            1.0)
        {
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        }

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
    }


    private static string TrimLog(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        string clean =
            value.Trim();

        const int maximumLength =
            180;

        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }
}
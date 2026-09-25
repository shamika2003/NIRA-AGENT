/*
 * filename: NIRABranchRunnerService.cs
 */

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Hosting;

using NIRAAgent.Capabilities;
using NIRAAgent.Authorization;
using NIRAAgent.Mind;
using NIRAAgent.Tools;

namespace NIRAAgent.Branches;

// =============================================================
// BRANCH WORK RUNNER
//
// A branch does not run its own reasoning loop. NIRA cognition
// assigns a bounded work item to a branch. This service executes
// only that exact assigned capability/dynamic-tool invocation and
// emits the authoritative result back to NIRA cognition.
// =============================================================

public sealed class NIRABranchRunnerService
    : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(
            500);

    private const int MaximumConcurrentBranchWork =
        4;

    private readonly NIRABranchService
        _branches;

    private readonly NIRABranchWorkService
        _work;

    private readonly NIRACapabilityService
        _capabilities;

    private readonly NIRACapabilityRegistry _capabilityRegistry;

    private readonly NIRADynamicToolService
        _dynamicTools;

    private readonly NIRABackgroundProcessor
        _background;

    private readonly NIRAAuthorityExecutionContextAccessor
        _authorityContext;

    private readonly ConcurrentDictionary<Guid, BranchWorkExecutionHandle>
        _running =
            new();

    private readonly ConcurrentDictionary<Guid, byte>
        _queuedWorkResults =
            new();

    // One failed fan-in never re-batches the same durable result forever.
    // Its next wake is delivered alone and can receive its own outcome review.
    private readonly ConcurrentDictionary<Guid, byte> _fanInAttempted = new();

    // Result events are durable through NIRABranchWorkService until cognition
    // successfully consumes them. If cognition is temporarily unavailable
    // (rate limit/network/provider outage), retry with bounded backoff instead
    // of hot-looping the same result every poll tick.
    private readonly ConcurrentDictionary<Guid, WorkResultDeliveryRetryState>
        _workResultDeliveryRetries =
            new();

    private readonly Channel<NIRABranchWorkResultEvent>
        _workResultEvents =
            Channel.CreateUnbounded<NIRABranchWorkResultEvent>(
                new UnboundedChannelOptions
                {
                    SingleReader =
                        true,

                    SingleWriter =
                        false,

                    AllowSynchronousContinuations =
                        false
                });

    private readonly Channel<NIRABranchResultEvent>
        _branchResultEvents =
            Channel.CreateUnbounded<NIRABranchResultEvent>(
                new UnboundedChannelOptions
                {
                    SingleReader =
                        true,

                    SingleWriter =
                        false,

                    AllowSynchronousContinuations =
                        false
                });

    public NIRABranchRunnerService(
        NIRABranchService branches,
        NIRABranchWorkService work,
        NIRACapabilityService capabilities,
        NIRACapabilityRegistry capabilityRegistry,
        NIRADynamicToolService dynamicTools,
        NIRABackgroundProcessor background,
        NIRAAuthorityExecutionContextAccessor authorityContext)
    {
        _branches =
            branches
            ?? throw new ArgumentNullException(
                nameof(branches));

        _work =
            work
            ?? throw new ArgumentNullException(
                nameof(work));

        _capabilities =
            capabilities
            ?? throw new ArgumentNullException(
                nameof(capabilities));

        _capabilityRegistry = capabilityRegistry
            ?? throw new ArgumentNullException(nameof(capabilityRegistry));

        _dynamicTools =
            dynamicTools
            ?? throw new ArgumentNullException(
                nameof(dynamicTools));

        _background =
            background
            ?? throw new ArgumentNullException(
                nameof(background));

        _authorityContext =
            authorityContext
            ?? throw new ArgumentNullException(
                nameof(authorityContext));
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _branches.BranchResolved +=
            Branches_BranchResolved;

        Debug.WriteLine(
            $"[BranchRunner] STARTED | " +
            $"Mode=AssignedWork | " +
            $"MaxConcurrent={MaximumConcurrentBranchWork}");

        Task workResultPump =
            ProcessWorkResultEventsAsync(
                stoppingToken);

        Task branchResultPump =
            ProcessBranchResultEventsAsync(
                stoppingToken);

        using PeriodicTimer timer =
            new(
                PollInterval);

        try
        {
            QueueUnnotifiedWorkResults();
            ScheduleRunnableWork(
                stoppingToken);

            while (await timer.WaitForNextTickAsync(
                       stoppingToken))
            {
                QueueUnnotifiedWorkResults();
                ScheduleRunnableWork(
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _branches.BranchResolved -=
                Branches_BranchResolved;

            _workResultEvents.Writer.TryComplete();
            _branchResultEvents.Writer.TryComplete();

            foreach (BranchWorkExecutionHandle handle in _running.Values)
            {
                try
                {
                    handle.Cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            Task[] activeTasks =
                _running.Values
                    .Select(handle => handle.Task)
                    .Where(task => task != null)
                    .Cast<Task>()
                    .ToArray();

            if (activeTasks.Length > 0)
            {
                try
                {
                    await Task.WhenAll(
                        activeTasks);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[BranchRunner] SHUTDOWN WAIT ERROR | {ex}");
                }
            }

            await AwaitPumpAsync(
                workResultPump,
                stoppingToken);

            await AwaitPumpAsync(
                branchResultPump,
                stoppingToken);

            Debug.WriteLine(
                "[BranchRunner] STOPPED");
        }
    }

    private void ScheduleRunnableWork(
        CancellationToken stoppingToken)
    {
        int available =
            MaximumConcurrentBranchWork -
            _running.Count;

        if (available <= 0)
        {
            return;
        }

        IReadOnlyList<NIRABranchWorkItem> candidates =
            _work.GetRunnableWork(
                MaximumConcurrentBranchWork * 4);

        // Distinct branches may overlap, but a single persistent responsibility
        // must NOT dispatch two steps against the same browser/app state at once.
        // Work in one branch stays ordered: result -> NIRA decision -> next step.
        HashSet<Guid> busyBranchIds = _running.Values
            .Select(handle => handle.BranchId)
            .ToHashSet();

        foreach (NIRABranchWorkItem item in candidates)
        {
            if (busyBranchIds.Contains(item.BranchId))
            {
                continue;
            }

            if (available <= 0)
            {
                break;
            }

            if (_running.ContainsKey(
                    item.Id))
            {
                continue;
            }

            CancellationTokenSource cancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken);

            BranchWorkExecutionHandle handle =
                new(
                    item.BranchId,
                    cancellation);

            if (!_running.TryAdd(
                    item.Id,
                    handle))
            {
                cancellation.Dispose();
                continue;
            }

            // Reserve only after the work item was accepted. Separate branches
            // remain concurrent up to the existing MaxConcurrent limit.
            busyBranchIds.Add(item.BranchId);

            handle.Task =
                RunAssignedWorkAsync(
                    item,
                    handle,
                    stoppingToken);

            available--;
        }
    }

    private async Task RunAssignedWorkAsync(
        NIRABranchWorkItem queued,
        BranchWorkExecutionHandle handle,
        CancellationToken stoppingToken)
    {
        NIRABranchWorkItem? running =
            null;

        Debug.WriteLine(
            $"[BranchRunner] WORK START | " +
            $"Work={queued.Id:D} | " +
            $"Branch={queued.BranchId:D} | " +
            $"Goal={queued.GoalId:D} | " +
            $"Kind={queued.Kind} | " +
            $"Running={_running.Count} | " +
            $"Reason='{TrimLog(queued.Reason)}'");

        try
        {
            running =
                await _work.MarkRunningAsync(
                    queued.Id,
                    handle.Cancellation.Token);

            if (running == null)
            {
                return;
            }

            NIRABranchWorkCompletion completion =
                running.Kind switch
                {
                    NIRABranchWorkKind.Capability =>
                        await ExecuteCapabilityWorkAsync(
                            running,
                            handle.Cancellation.Token),

                    NIRABranchWorkKind.DynamicTool =>
                        await ExecuteDynamicToolWorkAsync(
                            running,
                            handle.Cancellation.Token),

                    _ =>
                        throw new ArgumentOutOfRangeException(
                            nameof(running.Kind))
                };

            NIRABranchWorkItem? completed =
                await _work.CompleteAsync(
                    running.Id,
                    completion.Status,
                    completion.Summary,
                    completion.Evidence,
                    handle.Cancellation.Token);

            if (completed != null &&
                completed.IsTerminal)
            {
                QueueWorkResult(
                    completed);
            }
        }
        catch (OperationCanceledException)
            when (handle.Cancellation.IsCancellationRequested)
        {
            if (!stoppingToken.IsCancellationRequested &&
                running != null)
            {
                try
                {
                    NIRABranchWorkItem? cancelled =
                        await _work.CompleteAsync(
                            running.Id,
                            NIRABranchWorkStatus.Cancelled,
                            "Assigned work was cancelled because its owning branch was resolved or cancelled.",
                            "The branch runtime cancelled this in-flight work item. No success should be inferred from the interrupted operation.",
                            CancellationToken.None);

                    if (cancelled != null)
                    {
                        // The branch resolution itself already wakes NIRA/parent.
                        await _work.MarkResultNotifiedAsync(
                            cancelled.Id,
                            CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[BranchRunner] CANCEL RECORD ERROR | Work={queued.Id:D} | {ex}");
                }
            }

            Debug.WriteLine(
                $"[BranchRunner] WORK CANCELLED | " +
                $"Work={queued.Id:D} | " +
                $"Branch={queued.BranchId:D} | " +
                $"Shutdown={stoppingToken.IsCancellationRequested}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[BranchRunner] WORK ERROR | " +
                $"Work={queued.Id:D} | " +
                $"Branch={queued.BranchId:D} | {ex}");

            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    NIRABranchWorkItem? failed =
                        await _work.CompleteAsync(
                            queued.Id,
                            NIRABranchWorkStatus.Failed,
                            $"Assigned branch work failed in the runtime: {ex.Message}",
                            ex.ToString(),
                            CancellationToken.None);

                    if (failed != null)
                    {
                        QueueWorkResult(
                            failed);
                    }
                }
                catch (Exception recordError)
                {
                    Debug.WriteLine(
                        $"[BranchRunner] WORK FAILURE RECORD ERROR | " +
                        $"Work={queued.Id:D} | {recordError}");
                }
            }
        }
        finally
        {
            _running.TryRemove(
                queued.Id,
                out _);

            handle.Cancellation.Dispose();

            Debug.WriteLine(
                $"[BranchRunner] WORK END | " +
                $"Work={queued.Id:D} | " +
                $"Branch={queued.BranchId:D} | " +
                $"Running={_running.Count}");
        }
    }

    private async Task<NIRABranchWorkCompletion> ExecuteCapabilityWorkAsync(
        NIRABranchWorkItem work,
        CancellationToken cancellationToken)
    {
        NIRACapabilityRequest plan =
            work.CapabilityRequest
            ?? throw new InvalidOperationException(
                "Capability branch work is missing its capability request.");

        using IDisposable authorityLease = _authorityContext.Push(
            new NIRAAuthorityExecutionContext
            {
                GoalId = work.GoalId,
                BranchId = work.BranchId,
                WorkId = work.Id,
                TaskLabel = work.Reason
            });

        // Validation happens BEFORE any step dispatch. No planned continuation
        // can introduce another login, click, form submit, file mutation or
        // shell action after the LLM has stopped seeing fresh evidence.
        NIRACapabilityRequest[] continuation =
            (plan.ContinuationRequests ?? Array.Empty<NIRACapabilityRequest>())
                .ToArray();
        foreach (NIRACapabilityRequest next in continuation)
        {
            if (!IsAllowedGroundedContinuation(next.CapabilityId))
                return new NIRABranchWorkCompletion(
                    NIRABranchWorkStatus.Rejected,
                    "A continuation includes an operation needing a fresh model decision. Nothing was dispatched.",
                    $"Rejected continuation capability: {next.CapabilityId}. No actions were dispatched.");
        }

        NIRACapabilityRequest[] steps =
            new[] { plan with
            {
                ContinuationRequests = Array.Empty<NIRACapabilityRequest>()
            }}.Concat(continuation).ToArray();

        if (steps.Length > 1)
            Debug.WriteLine($"[BranchRunner] BOUNDED CONTINUATION | Work={work.Id:D} | Steps={steps.Length}");

        List<string> evidence = new();
        int completedCount = 0;
        foreach (NIRACapabilityRequest step in steps)
        {
            // The branch-resolution cancellation is linked by the runner;
            // no subsequent operation may execute after interruption.
            cancellationToken.ThrowIfCancellationRequested();
            if (!_branches.TryGetBranch(work.BranchId, out NIRABranchState? owner) ||
                owner == null || owner.IsResolved || owner.GoalId != work.GoalId)
                return new NIRABranchWorkCompletion(
                    NIRABranchWorkStatus.Cancelled,
                    "The owning branch is no longer active; the remaining steps were not dispatched.",
                    string.Join("\n", evidence));

            IReadOnlyList<NIRACapabilityResult> results =
                await _capabilities.ExecuteAsync(new[] { step }, cancellationToken);
            NIRACapabilityResult? result = results.FirstOrDefault();
            if (result == null)
                return new NIRABranchWorkCompletion(
                    NIRABranchWorkStatus.Failed,
                    "The capability runtime returned no result; remaining steps were not dispatched.",
                    string.Join("\n", evidence));

            evidence.Add($"PLAN STEP {completedCount + 1}/{steps.Length}\n" +
                FormatCapabilityEvidence(result));

            NIRABranchWorkStatus status = result.Status switch
            {
                NIRACapabilityResultStatus.Succeeded when !result.OutcomeUncertain =>
                    NIRABranchWorkStatus.Succeeded,
                NIRACapabilityResultStatus.AuthorizationRequired =>
                    NIRABranchWorkStatus.AuthorizationRequired,
                NIRACapabilityResultStatus.Rejected =>
                    NIRABranchWorkStatus.Rejected,
                _ => NIRABranchWorkStatus.Failed
            };
            if (status != NIRABranchWorkStatus.Succeeded)
                return new NIRABranchWorkCompletion(
                    status,
                    $"Step {completedCount + 1}/{steps.Length} stopped: {result.Summary} " +
                    "Later steps were not dispatched; inspect the evidence before replanning.",
                    BoundEvidence(string.Join("\n", evidence)));
            completedCount++;
        }

        return new NIRABranchWorkCompletion(
            NIRABranchWorkStatus.Succeeded,
            $"Completed {completedCount} pre-grounded capability step(s); evaluate the combined evidence against the original goal.",
            BoundEvidence(string.Join("\n", evidence)));
    }

    // Only explicit observations can run without another cognitive decision.
    // In particular: NOT browser.authenticate, browser.click, browser.follow,
    // browser.navigate, download, upload, shell or any write/mutation. Site
    // responses can invalidate the model's earlier assumptions and refs.
    private static bool IsAllowedGroundedContinuation(string capabilityId) =>
        capabilityId is
            NIRACapabilityIds.FileRead or
            NIRACapabilityIds.DirectoryList or
            NIRACapabilityIds.FileMetadata or
            NIRACapabilityIds.ProcessList or
            NIRACapabilityIds.ApplicationResolve or
            NIRACapabilityIds.BrowserCurrent or
            NIRACapabilityIds.BrowserInspect;

    private async Task<NIRABranchWorkCompletion> ExecuteDynamicToolWorkAsync(
        NIRABranchWorkItem work,
        CancellationToken cancellationToken)
    {
        NIRADynamicToolInvocation invocation =
            work.DynamicToolInvocation
            ?? throw new InvalidOperationException(
                "Dynamic-tool branch work is missing its invocation.");

        using IDisposable authorityLease =
            _authorityContext.Push(
                new NIRAAuthorityExecutionContext
                {
                    GoalId = work.GoalId,
                    BranchId = work.BranchId,
                    WorkId = work.Id,
                    TaskLabel = work.Reason
                });

        IReadOnlyList<NIRADynamicToolExecutionResult> results =
            await _dynamicTools.ExecuteAsync(
                new[]
                {
                    invocation
                },
                cancellationToken);

        NIRADynamicToolExecutionResult? result =
            results.FirstOrDefault();

        if (result == null)
        {
            return new NIRABranchWorkCompletion(
                NIRABranchWorkStatus.Failed,
                "The dynamic-tool runtime returned no result for the assigned branch work.",
                "No authoritative dynamic-tool result was produced.");
        }

        NIRABranchWorkStatus status;

        if (result.Succeeded)
        {
            status =
                NIRABranchWorkStatus.Succeeded;
        }
        else if (result.CapabilityResults.Any(
                     value =>
                         value.Status ==
                         NIRACapabilityResultStatus.AuthorizationRequired))
        {
            status =
                NIRABranchWorkStatus.AuthorizationRequired;
        }
        else if (result.CapabilityResults.Any(
                     value =>
                         value.Status ==
                         NIRACapabilityResultStatus.Rejected))
        {
            status =
                NIRABranchWorkStatus.Rejected;
        }
        else
        {
            status =
                NIRABranchWorkStatus.Failed;
        }

        string summary =
            string.IsNullOrWhiteSpace(
                result.Summary)
                ? status == NIRABranchWorkStatus.Succeeded
                    ? $"Dynamic tool {result.ToolName} succeeded."
                    : $"Dynamic tool {result.ToolName} did not complete successfully."
                : result.Summary;

        return new NIRABranchWorkCompletion(
            status,
            summary,
            FormatDynamicToolEvidence(
                result));
    }

    private void QueueUnnotifiedWorkResults()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        foreach (NIRABranchWorkItem item in _work.GetUnnotifiedTerminalWork())
        {
            if (_workResultDeliveryRetries.TryGetValue(
                    item.Id,
                    out WorkResultDeliveryRetryState? retry)
                &&
                retry.RetryAfterUtc >
                    now)
            {
                continue;
            }

            QueueWorkResult(
                item);
        }
    }

    private void QueueWorkResult(
        NIRABranchWorkItem item)
    {
        if (!_queuedWorkResults.TryAdd(
                item.Id,
                0))
        {
            return;
        }

        NIRABranchWorkResultEvent result =
            _work.BuildResultEvent(
                item);

        if (!_workResultEvents.Writer.TryWrite(
                result))
        {
            _queuedWorkResults.TryRemove(
                item.Id,
                out _);
        }
    }

    // Fan-in is deliberately short and restricted to read-only results from
    // DIFFERENT branches of the SAME goal. Large read-only page snapshots are
    // compacted only in the mind event; their full durable evidence remains in
    // branch work storage. Mutations, failures and unrelated goals stay separate.
    // Never wait long for a slower branch: this is a small scheduling window only.
    private static readonly TimeSpan WorkResultFanInWindow =
        TimeSpan.FromMilliseconds(1800);

    private const int MaximumReadOnlyResultBatch = 4;
    // Full authoritative browser evidence is persisted separately. The mind
    // event compacts each batched result, so large page snapshots can still
    // share one cognition turn without overflowing the event budget.
    private const int MaximumResultEvidenceForBatch = 24000;
    private const int FanInEvidenceBudgetPerResult = 3200;
    private const int MaximumFanInEvidenceBudget =
        MaximumReadOnlyResultBatch * FanInEvidenceBudgetPerResult;

    private bool IsBatchableObservation(NIRABranchWorkResultEvent result)
    {
        if (!result.Succeeded ||
            result.Kind != NIRABranchWorkKind.Capability ||
            result.ResultEvidence.Length > MaximumResultEvidenceForBatch ||
            !_work.TryGetWork(result.WorkId, out NIRABranchWorkItem? work) ||
            work?.CapabilityRequest == null)
            return false;

        // Trust the registered capability descriptors, never a string inside
        // a website's untrusted response claiming to be a read-only action.
        NIRACapabilityRequest[] steps = new[] { work.CapabilityRequest }
            .Concat(work.CapabilityRequest.ContinuationRequests ??
                Array.Empty<NIRACapabilityRequest>()).ToArray();
        try
        {
            return steps.All(step =>
                _capabilityRegistry.TryResolve(step.CapabilityId,
                    out INIRACapabilityHandler? handler) &&
                handler != null &&
                handler.ResolveRisk(step) == NIRACapabilityRisk.Observe);
        }
        catch (Exception)
        {
            // An unclassifiable action must be delivered on its own.
            return false;
        }
    }

    private async Task ProcessWorkResultEventsAsync(
        CancellationToken stoppingToken)
    {
        List<NIRABranchWorkResultEvent> pending = new();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (pending.Count == 0)
                {
                    if (!await _workResultEvents.Reader.WaitToReadAsync(stoppingToken))
                        break;
                }

                while (_workResultEvents.Reader.TryRead(out NIRABranchWorkResultEvent drained))
                    pending.Add(drained);
                if (pending.Count == 0)
                    continue;

                NIRABranchWorkResultEvent first = pending[0];
                pending.RemoveAt(0);

                if (IsBatchableObservation(first) &&
                    !_fanInAttempted.ContainsKey(first.WorkId) &&
                    pending.Count == 0 &&
                    _running.Values.Any(handle =>
                        handle.BranchId != first.BranchId &&
                        _branches.TryGetBranch(handle.BranchId,
                            out NIRABranchState? peer) &&
                        peer?.GoalId == first.GoalId))
                {
                    // A still-running peer may finish just after this result.
                    // A peer that takes longer NEVER blocks the first result.
                    await Task.Delay(WorkResultFanInWindow, stoppingToken);
                    while (_workResultEvents.Reader.TryRead(out NIRABranchWorkResultEvent late))
                        pending.Add(late);
                }

                List<NIRABranchWorkResultEvent> batch = new() { first };
                if (IsBatchableObservation(first) &&
                    !_fanInAttempted.ContainsKey(first.WorkId))
                {
                    for (int i = 0; i < pending.Count &&
                         batch.Count < MaximumReadOnlyResultBatch;)
                    {
                        NIRABranchWorkResultEvent candidate = pending[i];
                        if (candidate.GoalId == first.GoalId &&
                            candidate.BranchId != first.BranchId &&
                            !batch.Any(item => item.BranchId == candidate.BranchId) &&
                            !_fanInAttempted.ContainsKey(candidate.WorkId) &&
                            batch.Sum(item => Math.Min(
                                item.ResultEvidence.Length, FanInEvidenceBudgetPerResult)) +
                                Math.Min(candidate.ResultEvidence.Length,
                                    FanInEvidenceBudgetPerResult) <= MaximumFanInEvidenceBudget &&
                            IsBatchableObservation(candidate))
                        {
                            batch.Add(candidate);
                            pending.RemoveAt(i);
                        }
                        else
                        {
                            i++;
                        }
                    }
                }

                try
                {
                    Debug.WriteLine($"[BranchRunner] WORK RESULT FAN-IN | " +
                        $"Goal={first.GoalId:D} | Batch={batch.Count} | " +
                        $"Work={string.Join(",", batch.Select(item => item.WorkId.ToString("D")))}");

                    await _background.ProcessInternalAsync(
                        NIRAMindEvent.BranchWorkResultBatch(batch),
                        stoppingToken);

                    if (batch.Count > 1)
                        foreach (NIRABranchWorkResultEvent result in batch)
                            _fanInAttempted.TryAdd(result.WorkId, 0);

                    foreach (NIRABranchWorkResultEvent result in batch)
                    {
                        // Every member of a multi-result cognition turn needs
                        // its OWN committed continuation or verified completion.
                        // Otherwise deliver it alone on the next durable poll.
                        if (batch.Count > 1 && !WasSiblingHandled(result))
                        {
                            Debug.WriteLine($"[BranchRunner] FAN-IN SIBLING DEFERRED | " +
                                $"Work={result.WorkId:D} | " +
                                "Reason=NoCommittedNextStepOrReviewedCompletion");
                            continue;
                        }
                        await _work.MarkResultNotifiedAsync(
                            result.WorkId, stoppingToken);
                        _workResultDeliveryRetries.TryRemove(result.WorkId, out _);
                        _fanInAttempted.TryRemove(result.WorkId, out _);
                    }
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (batch.Count > 1)
                        foreach (NIRABranchWorkResultEvent result in batch)
                            _fanInAttempted.TryAdd(result.WorkId, 0);
                    foreach (NIRABranchWorkResultEvent result in batch)
                    {
                        WorkResultDeliveryRetryState retry =
                            ScheduleWorkResultRetry(result.WorkId);
                        Debug.WriteLine($"[BranchRunner] WORK RESULT DELIVERY ERROR | " +
                            $"Work={result.WorkId:D} | " +
                            $"RetryAttempt={retry.Attempt} | " +
                            $"RetryAfter={retry.RetryAfterUtc:O} | {ex}");
                    }
                }
                finally
                {
                    foreach (NIRABranchWorkResultEvent result in batch)
                        _queuedWorkResults.TryRemove(result.WorkId, out _);
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private bool WasSiblingHandled(NIRABranchWorkResultEvent result)
    {
        if (!_branches.TryGetBranch(result.BranchId, out NIRABranchState? branch) ||
            branch == null || branch.GoalId != result.GoalId)
            return false;

        if (branch.IsResolved || branch.Status is
            NIRABranchStatus.Waiting or NIRABranchStatus.Blocked)
            return true;

        // Another step is committed under the SAME branch; an unrelated
        // branch's success or a model's progress prose is never sufficient.
        return _work.CurrentWork.Any(next =>
            next.BranchId == result.BranchId &&
            next.Id != result.WorkId &&
            next.CreatedAtUtc >= result.FinishedAtUtc);
    }

    private void Branches_BranchResolved(
        NIRABranchResultEvent result)
    {
        _branchResultEvents.Writer.TryWrite(
            result);

        foreach (KeyValuePair<Guid, BranchWorkExecutionHandle> pair in _running)
        {
            if (pair.Value.BranchId != result.BranchId)
            {
                continue;
            }

            try
            {
                pair.Value.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _ = CancelPendingBranchWorkAsync(
            result);
    }

    private async Task CancelPendingBranchWorkAsync(
        NIRABranchResultEvent result)
    {
        try
        {
            await _work.CancelPendingForBranchAsync(
                result.BranchId,
                $"Owning branch resolved as {result.Status} before queued work started.",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[BranchRunner] PENDING CANCEL ERROR | " +
                $"Branch={result.BranchId:D} | {ex}");
        }
    }

    private async Task ProcessBranchResultEventsAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await foreach (NIRABranchResultEvent result
                           in _branchResultEvents.Reader.ReadAllAsync(
                               stoppingToken))
            {
                try
                {
                    Debug.WriteLine(
                        $"[BranchRunner] BRANCH RESULT EVENT | " +
                        $"Branch={result.BranchId:D} | " +
                        $"Goal={result.GoalId:D} | " +
                        $"Status={result.Status} | " +
                        $"Join={result.JoinPolicy}");

                    await _background.ProcessInternalAsync(
                        NIRAMindEvent.BranchResult(
                            result),
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[BranchRunner] BRANCH RESULT EVENT ERROR | " +
                        $"Branch={result.BranchId:D} | {ex}");
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static string FormatCapabilityEvidence(
        NIRACapabilityResult result)
    {
        StringBuilder builder =
            new();

        builder.AppendLine(
            "AUTHORITATIVE BRANCH CAPABILITY RESULT");
        builder.AppendLine(
            $"RequestId: {result.RequestId:D}");
        builder.AppendLine(
            $"CapabilityId: {result.CapabilityId}");
        builder.AppendLine(
            $"Risk: {result.Risk}");
        builder.AppendLine(
            $"Status: {result.Status}");
        builder.AppendLine(
            $"ChangedSystemState: {result.ChangedSystemState}");
        builder.AppendLine(
            $"OutcomeUncertain: {result.OutcomeUncertain}");
        builder.AppendLine(
            $"ExitCode: {result.ExitCode?.ToString() ?? "-"}");
        builder.AppendLine(
            $"HttpStatusCode: {result.HttpStatusCode?.ToString() ?? "-"}");
        builder.AppendLine(
            $"AuditAttemptId: {result.AuditAttemptId?.ToString("D") ?? "-"}");
        builder.AppendLine(
            $"Summary: {result.Summary}");

        if (!string.IsNullOrWhiteSpace(
                result.Output))
        {
            builder.AppendLine(
                "Output:");
            builder.AppendLine(
                result.Output);
        }

        return BoundEvidence(
            builder.ToString());
    }

    private static string FormatDynamicToolEvidence(
        NIRADynamicToolExecutionResult result)
    {
        StringBuilder builder =
            new();

        builder.AppendLine(
            "AUTHORITATIVE BRANCH DYNAMIC TOOL RESULT");
        builder.AppendLine(
            $"ToolId: {result.ToolId:D}");
        builder.AppendLine(
            $"ToolName: {result.ToolName}");
        builder.AppendLine(
            $"ToolVersion: {result.ToolVersion}");
        builder.AppendLine(
            $"Succeeded: {result.Succeeded}");
        builder.AppendLine(
            $"Aborted: {result.Aborted}");

        if (!string.IsNullOrWhiteSpace(
                result.FailureReason))
        {
            builder.AppendLine(
                $"FailureReason: {result.FailureReason}");
        }

        builder.AppendLine(
            $"Summary: {result.Summary}");

        foreach (NIRADynamicToolStepExecutionResult step in result.StepResults)
        {
            builder.AppendLine();
            builder.AppendLine(
                $"Step: {step.StepId}");
            builder.AppendLine(
                $"StepStatus: {step.Status}");
            builder.AppendLine(
                $"StepSummary: {step.Summary}");

            if (step.CapabilityResult != null)
            {
                builder.AppendLine(
                    $"CapabilityId: {step.CapabilityResult.CapabilityId}");
                builder.AppendLine(
                    $"CapabilityStatus: {step.CapabilityResult.Status}");
                builder.AppendLine(
                    $"ExitCode: {step.CapabilityResult.ExitCode?.ToString() ?? "-"}");
                builder.AppendLine(
                    $"HttpStatusCode: {step.CapabilityResult.HttpStatusCode?.ToString() ?? "-"}");
                builder.AppendLine(
                    $"OutcomeUncertain: {step.CapabilityResult.OutcomeUncertain}");

                if (!string.IsNullOrWhiteSpace(
                        step.CapabilityResult.Output))
                {
                    builder.AppendLine(
                        "CapabilityOutput:");
                    builder.AppendLine(
                        step.CapabilityResult.Output);
                }
            }
        }

        return BoundEvidence(
            builder.ToString());
    }

    private WorkResultDeliveryRetryState ScheduleWorkResultRetry(
        Guid workId)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        return _workResultDeliveryRetries.AddOrUpdate(
            workId,
            _ =>
            {
                int attempt =
                    1;

                return new WorkResultDeliveryRetryState(
                    attempt,
                    now + ResolveWorkResultRetryDelay(
                        attempt));
            },
            (_, previous) =>
            {
                int attempt =
                    Math.Min(
                        previous.Attempt + 1,
                        32);

                return new WorkResultDeliveryRetryState(
                    attempt,
                    now + ResolveWorkResultRetryDelay(
                        attempt));
            });
    }

    private static TimeSpan ResolveWorkResultRetryDelay(
        int attempt)
    {
        // Fast enough to recover from a transient request failure, but bounded
        // so a provider quota/outage cannot consume CPU or repeatedly hammer
        // the reasoning endpoint. The terminal work item remains unnotified
        // and therefore durable until a later delivery succeeds.
        return attempt switch
        {
            <= 1 => TimeSpan.FromSeconds(15),
            2 => TimeSpan.FromSeconds(30),
            3 => TimeSpan.FromMinutes(1),
            4 => TimeSpan.FromMinutes(2),
            _ => TimeSpan.FromMinutes(5)
        };
    }

    private static string FormatRetryDelay(
        TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            delay =
                TimeSpan.Zero;
        }

        if (delay.TotalMinutes >= 1.0)
        {
            return $"{Math.Ceiling(delay.TotalMinutes):0}m";
        }

        return $"{Math.Ceiling(delay.TotalSeconds):0}s";
    }

    private static string BoundEvidence(
        string value)
    {
        string clean =
            value.Trim();

        const int maximumLength =
            22000;

        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] +
              Environment.NewLine +
              "[Branch work evidence truncated by runtime bound.]";
    }

    private static async Task AwaitPumpAsync(
        Task pump,
        CancellationToken stoppingToken)
    {
        try
        {
            await pump;
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
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
            160;

        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }

    private sealed record WorkResultDeliveryRetryState(
        int Attempt,
        DateTimeOffset RetryAfterUtc);

    private sealed record NIRABranchWorkCompletion(
        NIRABranchWorkStatus Status,
        string Summary,
        string Evidence);

    private sealed class BranchWorkExecutionHandle
    {
        public Guid BranchId { get; }

        public CancellationTokenSource Cancellation { get; }

        public Task? Task { get; set; }

        public BranchWorkExecutionHandle(
            Guid branchId,
            CancellationTokenSource cancellation)
        {
            BranchId =
                branchId;

            Cancellation =
                cancellation;
        }
    }
}

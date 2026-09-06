/*
 * filename: SegaBranchRunnerService.cs
 */

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Hosting;

using SegaAgent.Capabilities;
using SegaAgent.Mind;
using SegaAgent.Tools;

namespace SegaAgent.Branches;

// =============================================================
// BRANCH WORK RUNNER
//
// A branch does not run its own reasoning loop. Sega cognition
// assigns a bounded work item to a branch. This service executes
// only that exact assigned capability/dynamic-tool invocation and
// emits the authoritative result back to Sega cognition.
// =============================================================

public sealed class SegaBranchRunnerService
    : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(
            500);

    private const int MaximumConcurrentBranchWork =
        4;

    private readonly SegaBranchService
        _branches;

    private readonly SegaBranchWorkService
        _work;

    private readonly SegaCapabilityService
        _capabilities;

    private readonly SegaDynamicToolService
        _dynamicTools;

    private readonly SegaBackgroundProcessor
        _background;

    private readonly ConcurrentDictionary<Guid, BranchWorkExecutionHandle>
        _running =
            new();

    private readonly ConcurrentDictionary<Guid, byte>
        _queuedWorkResults =
            new();

    // Result events are durable through SegaBranchWorkService until cognition
    // successfully consumes them. If cognition is temporarily unavailable
    // (rate limit/network/provider outage), retry with bounded backoff instead
    // of hot-looping the same result every poll tick.
    private readonly ConcurrentDictionary<Guid, WorkResultDeliveryRetryState>
        _workResultDeliveryRetries =
            new();

    private readonly Channel<SegaBranchWorkResultEvent>
        _workResultEvents =
            Channel.CreateUnbounded<SegaBranchWorkResultEvent>(
                new UnboundedChannelOptions
                {
                    SingleReader =
                        true,

                    SingleWriter =
                        false,

                    AllowSynchronousContinuations =
                        false
                });

    private readonly Channel<SegaBranchResultEvent>
        _branchResultEvents =
            Channel.CreateUnbounded<SegaBranchResultEvent>(
                new UnboundedChannelOptions
                {
                    SingleReader =
                        true,

                    SingleWriter =
                        false,

                    AllowSynchronousContinuations =
                        false
                });

    public SegaBranchRunnerService(
        SegaBranchService branches,
        SegaBranchWorkService work,
        SegaCapabilityService capabilities,
        SegaDynamicToolService dynamicTools,
        SegaBackgroundProcessor background)
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

        _dynamicTools =
            dynamicTools
            ?? throw new ArgumentNullException(
                nameof(dynamicTools));

        _background =
            background
            ?? throw new ArgumentNullException(
                nameof(background));
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

        IReadOnlyList<SegaBranchWorkItem> candidates =
            _work.GetRunnableWork(
                MaximumConcurrentBranchWork * 4);

        foreach (SegaBranchWorkItem item in candidates)
        {
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

            handle.Task =
                RunAssignedWorkAsync(
                    item,
                    handle,
                    stoppingToken);

            available--;
        }
    }

    private async Task RunAssignedWorkAsync(
        SegaBranchWorkItem queued,
        BranchWorkExecutionHandle handle,
        CancellationToken stoppingToken)
    {
        SegaBranchWorkItem? running =
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

            SegaBranchWorkCompletion completion =
                running.Kind switch
                {
                    SegaBranchWorkKind.Capability =>
                        await ExecuteCapabilityWorkAsync(
                            running,
                            handle.Cancellation.Token),

                    SegaBranchWorkKind.DynamicTool =>
                        await ExecuteDynamicToolWorkAsync(
                            running,
                            handle.Cancellation.Token),

                    _ =>
                        throw new ArgumentOutOfRangeException(
                            nameof(running.Kind))
                };

            SegaBranchWorkItem? completed =
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
                    SegaBranchWorkItem? cancelled =
                        await _work.CompleteAsync(
                            running.Id,
                            SegaBranchWorkStatus.Cancelled,
                            "Assigned work was cancelled because its owning branch was resolved or cancelled.",
                            "The branch runtime cancelled this in-flight work item. No success should be inferred from the interrupted operation.",
                            CancellationToken.None);

                    if (cancelled != null)
                    {
                        // The branch resolution itself already wakes Sega/parent.
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
                    SegaBranchWorkItem? failed =
                        await _work.CompleteAsync(
                            queued.Id,
                            SegaBranchWorkStatus.Failed,
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

    private async Task<SegaBranchWorkCompletion> ExecuteCapabilityWorkAsync(
        SegaBranchWorkItem work,
        CancellationToken cancellationToken)
    {
        SegaCapabilityRequest request =
            work.CapabilityRequest
            ?? throw new InvalidOperationException(
                "Capability branch work is missing its capability request.");

        IReadOnlyList<SegaCapabilityResult> results =
            await _capabilities.ExecuteAsync(
                new[]
                {
                    request
                },
                cancellationToken);

        SegaCapabilityResult? result =
            results.FirstOrDefault();

        if (result == null)
        {
            return new SegaBranchWorkCompletion(
                SegaBranchWorkStatus.Failed,
                "The capability runtime returned no result for the assigned branch work.",
                "No authoritative capability result was produced.");
        }

        SegaBranchWorkStatus status =
            result.Status switch
            {
                SegaCapabilityResultStatus.Succeeded
                    when !result.OutcomeUncertain =>
                    SegaBranchWorkStatus.Succeeded,

                SegaCapabilityResultStatus.AuthorizationRequired =>
                    SegaBranchWorkStatus.AuthorizationRequired,

                SegaCapabilityResultStatus.Rejected =>
                    SegaBranchWorkStatus.Rejected,

                _ =>
                    SegaBranchWorkStatus.Failed
            };

        string summary =
            status == SegaBranchWorkStatus.Succeeded
                ? string.IsNullOrWhiteSpace(result.Summary)
                    ? $"{result.CapabilityId} succeeded."
                    : result.Summary
                : string.IsNullOrWhiteSpace(result.Summary)
                    ? $"{result.CapabilityId} did not complete successfully."
                    : result.Summary;

        return new SegaBranchWorkCompletion(
            status,
            summary,
            FormatCapabilityEvidence(
                result));
    }

    private async Task<SegaBranchWorkCompletion> ExecuteDynamicToolWorkAsync(
        SegaBranchWorkItem work,
        CancellationToken cancellationToken)
    {
        SegaDynamicToolInvocation invocation =
            work.DynamicToolInvocation
            ?? throw new InvalidOperationException(
                "Dynamic-tool branch work is missing its invocation.");

        IReadOnlyList<SegaDynamicToolExecutionResult> results =
            await _dynamicTools.ExecuteAsync(
                new[]
                {
                    invocation
                },
                cancellationToken);

        SegaDynamicToolExecutionResult? result =
            results.FirstOrDefault();

        if (result == null)
        {
            return new SegaBranchWorkCompletion(
                SegaBranchWorkStatus.Failed,
                "The dynamic-tool runtime returned no result for the assigned branch work.",
                "No authoritative dynamic-tool result was produced.");
        }

        SegaBranchWorkStatus status;

        if (result.Succeeded)
        {
            status =
                SegaBranchWorkStatus.Succeeded;
        }
        else if (result.CapabilityResults.Any(
                     value =>
                         value.Status ==
                         SegaCapabilityResultStatus.AuthorizationRequired))
        {
            status =
                SegaBranchWorkStatus.AuthorizationRequired;
        }
        else if (result.CapabilityResults.Any(
                     value =>
                         value.Status ==
                         SegaCapabilityResultStatus.Rejected))
        {
            status =
                SegaBranchWorkStatus.Rejected;
        }
        else
        {
            status =
                SegaBranchWorkStatus.Failed;
        }

        string summary =
            string.IsNullOrWhiteSpace(
                result.Summary)
                ? status == SegaBranchWorkStatus.Succeeded
                    ? $"Dynamic tool {result.ToolName} succeeded."
                    : $"Dynamic tool {result.ToolName} did not complete successfully."
                : result.Summary;

        return new SegaBranchWorkCompletion(
            status,
            summary,
            FormatDynamicToolEvidence(
                result));
    }

    private void QueueUnnotifiedWorkResults()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        foreach (SegaBranchWorkItem item in _work.GetUnnotifiedTerminalWork())
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
        SegaBranchWorkItem item)
    {
        if (!_queuedWorkResults.TryAdd(
                item.Id,
                0))
        {
            return;
        }

        SegaBranchWorkResultEvent result =
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

    private async Task ProcessWorkResultEventsAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await foreach (SegaBranchWorkResultEvent result
                           in _workResultEvents.Reader.ReadAllAsync(
                               stoppingToken))
            {
                try
                {
                    Debug.WriteLine(
                        $"[BranchRunner] WORK RESULT EVENT | " +
                        $"Work={result.WorkId:D} | " +
                        $"Branch={result.BranchId:D} | " +
                        $"Goal={result.GoalId:D} | " +
                        $"Kind={result.Kind} | " +
                        $"Status={result.Status}");

                    await _background.ProcessInternalAsync(
                        SegaMindEvent.BranchWorkResult(
                            result),
                        stoppingToken);

                    await _work.MarkResultNotifiedAsync(
                        result.WorkId,
                        stoppingToken);

                    _workResultDeliveryRetries.TryRemove(
                        result.WorkId,
                        out _);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    WorkResultDeliveryRetryState retry =
                        ScheduleWorkResultRetry(
                            result.WorkId);

                    Debug.WriteLine(
                        $"[BranchRunner] WORK RESULT EVENT ERROR | " +
                        $"Work={result.WorkId:D} | " +
                        $"RetryAttempt={retry.Attempt} | " +
                        $"RetryAfter={retry.RetryAfterUtc:O} | {ex}");

                    Debug.WriteLine(
                        $"[BranchRunner] WORK RESULT RETRY SCHEDULED | " +
                        $"Work={result.WorkId:D} | " +
                        $"Attempt={retry.Attempt} | " +
                        $"Delay={FormatRetryDelay(retry.RetryAfterUtc - DateTimeOffset.UtcNow)}");
                }
                finally
                {
                    _queuedWorkResults.TryRemove(
                        result.WorkId,
                        out _);
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void Branches_BranchResolved(
        SegaBranchResultEvent result)
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
        SegaBranchResultEvent result)
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
            await foreach (SegaBranchResultEvent result
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
                        SegaMindEvent.BranchResult(
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
        SegaCapabilityResult result)
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
        SegaDynamicToolExecutionResult result)
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

        foreach (SegaDynamicToolStepExecutionResult step in result.StepResults)
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

    private sealed record SegaBranchWorkCompletion(
        SegaBranchWorkStatus Status,
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

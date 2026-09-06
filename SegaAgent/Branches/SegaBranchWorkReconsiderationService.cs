/*
 * filename: SegaBranchWorkReconsiderationService.cs
 */

using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using SegaAgent.Mind;

namespace SegaAgent.Branches;

// =============================================================
// LONG-RUNNING BRANCH WORK RECONSIDERATION
//
// This service does NOT plan branch work and it never emits a
// scripted progress message. It only gives Sega another cognition
// opportunity when authoritative branch-owned work has remained
// Running for a meaningful amount of time.
//
// Sega then decides whether to start other independent work, wait,
// speak naturally to the user, or do nothing at all.
// =============================================================

public sealed class SegaBranchWorkReconsiderationService
    : BackgroundService
{
    private static readonly TimeSpan ScanInterval =
        TimeSpan.FromSeconds(
            5);

    private static readonly TimeSpan InitialLongRunningThreshold =
        TimeSpan.FromSeconds(
            45);

    private static readonly TimeSpan FirstFollowUpInterval =
        TimeSpan.FromSeconds(
            90);

    private static readonly TimeSpan SecondFollowUpInterval =
        TimeSpan.FromMinutes(
            3);

    private static readonly TimeSpan SteadyFollowUpInterval =
        TimeSpan.FromMinutes(
            5);

    private readonly SegaBranchWorkService
        _work;

    private readonly SegaBackgroundProcessor
        _background;

    private readonly ConcurrentDictionary<Guid, GoalReconsiderationState>
        _states =
            new();

    public SegaBranchWorkReconsiderationService(
        SegaBranchWorkService work,
        SegaBackgroundProcessor background)
    {
        _work =
            work
            ?? throw new ArgumentNullException(
                nameof(work));

        _background =
            background
            ?? throw new ArgumentNullException(
                nameof(background));
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        Debug.WriteLine(
            $"[BranchReconsideration] STARTED | " +
            $"Initial={InitialLongRunningThreshold.TotalSeconds:F0}s | " +
            $"FollowUp={FirstFollowUpInterval.TotalSeconds:F0}s");

        using PeriodicTimer timer =
            new(
                ScanInterval);

        try
        {
            await ScanAsync(
                stoppingToken);

            while (await timer.WaitForNextTickAsync(
                       stoppingToken))
            {
                await ScanAsync(
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            Debug.WriteLine(
                "[BranchReconsideration] STOPPED");
        }
    }

    private Task ScanAsync(
        CancellationToken stoppingToken)
    {
        stoppingToken.ThrowIfCancellationRequested();

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        SegaBranchWorkItem[] running =
            _work.CurrentWork
                .Where(
                    item =>
                        item.Status ==
                            SegaBranchWorkStatus.Running
                        &&
                        item.StartedAtUtc.HasValue)
                .ToArray();

        Guid[] activeGoalIds =
            running
                .Select(
                    item =>
                        item.GoalId)
                .Distinct()
                .ToArray();

        HashSet<Guid> activeGoalSet =
            activeGoalIds.ToHashSet();

        foreach (Guid goalId in _states.Keys)
        {
            if (!activeGoalSet.Contains(
                    goalId))
            {
                _states.TryRemove(
                    goalId,
                    out _);
            }
        }

        foreach (IGrouping<Guid, SegaBranchWorkItem> group in
                 running.GroupBy(
                     item =>
                         item.GoalId))
        {
            SegaBranchWorkItem[] items =
                group
                    .OrderBy(
                        item =>
                            item.StartedAtUtc)
                    .ToArray();

            DateTimeOffset oldestStart =
                items
                    .Where(
                        item =>
                            item.StartedAtUtc.HasValue)
                    .Min(
                        item =>
                            item.StartedAtUtc!.Value);

            Guid[] activeWorkIds =
                items
                    .Select(
                        item =>
                            item.Id)
                    .ToArray();

            GoalReconsiderationState state =
                _states.GetOrAdd(
                    group.Key,
                    _ =>
                        new GoalReconsiderationState(
                            activeWorkIds));

            state.UpdateActiveWork(
                activeWorkIds);

            if (state.IsInFlight)
            {
                continue;
            }

            if (!IsDue(
                    state,
                    oldestStart,
                    now))
            {
                continue;
            }

            if (!state.TryBegin(
                    now,
                    out int sequence))
            {
                continue;
            }

            SegaBranchWorkReconsiderationSnapshot? snapshot =
                _work.BuildReconsiderationSnapshot(
                    group.Key,
                    sequence,
                    now);

            if (snapshot == null)
            {
                state.End();
                continue;
            }

            _ = DispatchAsync(
                snapshot,
                state,
                stoppingToken);
        }

        return Task.CompletedTask;
    }

    private static bool IsDue(
        GoalReconsiderationState state,
        DateTimeOffset oldestStart,
        DateTimeOffset now)
    {
        if (state.WakeCount ==
            0)
        {
            return now -
                oldestStart >=
                InitialLongRunningThreshold;
        }

        TimeSpan interval =
            state.WakeCount switch
            {
                1 =>
                    FirstFollowUpInterval,

                2 =>
                    SecondFollowUpInterval,

                _ =>
                    SteadyFollowUpInterval
            };

        return now -
            state.LastWakeAtUtc >=
            interval;
    }

    private async Task DispatchAsync(
        SegaBranchWorkReconsiderationSnapshot snapshot,
        GoalReconsiderationState state,
        CancellationToken stoppingToken)
    {
        try
        {
            Debug.WriteLine(
                $"[BranchReconsideration] WAKE | " +
                $"Goal={snapshot.GoalId:D} | " +
                $"Sequence={snapshot.Sequence} | " +
                $"RunningWork={snapshot.RunningWork.Count}");

            await _background.ProcessInternalAsync(
                SegaMindEvent.BranchWorkReconsideration(
                    snapshot),
                stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[BranchReconsideration] ERROR | " +
                $"Goal={snapshot.GoalId:D} | {ex}");
        }
        finally
        {
            state.End();
        }
    }

    private sealed class GoalReconsiderationState
    {
        private readonly object
            _sync =
                new();

        private int
            _inFlight;

        private HashSet<Guid>
            _activeWorkIds;

        public int WakeCount
        {
            get;
            private set;
        }

        public DateTimeOffset LastWakeAtUtc
        {
            get;
            private set;
        }

        public bool IsInFlight =>
            Volatile.Read(
                ref _inFlight) ==
            1;

        public GoalReconsiderationState(
            IEnumerable<Guid> activeWorkIds)
        {
            _activeWorkIds =
                activeWorkIds.ToHashSet();
        }

        public void UpdateActiveWork(
            IEnumerable<Guid> activeWorkIds)
        {
            HashSet<Guid> next =
                activeWorkIds.ToHashSet();

            lock (_sync)
            {
                bool continuous =
                    _activeWorkIds.Overlaps(
                        next);

                // If the previous running set ended and an entirely new set
                // started before the next 5-second scan, begin a fresh long-work
                // cadence instead of inheriting an old five-minute follow-up.
                if (!continuous)
                {
                    WakeCount =
                        0;

                    LastWakeAtUtc =
                        default;
                }

                _activeWorkIds =
                    next;
            }
        }

        public bool TryBegin(
            DateTimeOffset now,
            out int sequence)
        {
            sequence =
                0;

            if (Interlocked.CompareExchange(
                    ref _inFlight,
                    1,
                    0) !=
                0)
            {
                return false;
            }

            lock (_sync)
            {
                WakeCount++;

                sequence =
                    WakeCount;

                LastWakeAtUtc =
                    now;
            }

            return true;
        }

        public void End()
        {
            Interlocked.Exchange(
                ref _inFlight,
                0);
        }
    }
}

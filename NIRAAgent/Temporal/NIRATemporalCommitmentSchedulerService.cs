/*
 * filename: NIRATemporalCommitmentSchedulerService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using NIRAAgent.Mind;
using NIRAAgent.PC.Awareness;
using NIRAAgent.Self.Model;

namespace NIRAAgent.Temporal;

// =============================================================
// TEMPORAL COMMITMENT SCHEDULER
//
// This is a wake mechanism, not a dialogue generator.
//
// It notices that authoritative time has reached a persisted NIRA
// obligation and injects that fact back into NIRA's normal cognition.
// The main cognition decides whether/how to speak, wait, reschedule,
// create work, or remain silent. If a one-time reminder is actually
// delivered, the normal post-experience commitment formation pass can
// complete it from NIRA's final reply. Recurring commitments remain
// active and are advanced to their next occurrence.
// =============================================================

public sealed class NIRATemporalCommitmentSchedulerService
    : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan WakeLease =
        TimeSpan.FromMinutes(3);

    private readonly NIRASelfModelService _selfModel;
    private readonly NIRABackgroundProcessor _background;
    private readonly NIRATemporalContextService _temporal;
    private readonly PcWorldStateService _worldState;

    public NIRATemporalCommitmentSchedulerService(
        NIRASelfModelService selfModel,
        NIRABackgroundProcessor background,
        NIRATemporalContextService temporal,
        PcWorldStateService worldState)
    {
        _selfModel = selfModel ?? throw new ArgumentNullException(nameof(selfModel));
        _background = background ?? throw new ArgumentNullException(nameof(background));
        _temporal = temporal ?? throw new ArgumentNullException(nameof(temporal));
        _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        Debug.WriteLine("[TemporalCommitments] SCHEDULER STARTED");

        // Run once immediately so an obligation that became due while NIRA
        // was closed is noticed on startup instead of waiting a full period.
        await RunDuePassSafelyAsync(stoppingToken);

        using PeriodicTimer timer = new(PollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunDuePassSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }


    private async Task RunDuePassSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await ProcessDueAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A transient database/world-state/provider problem must not kill
            // the long-lived temporal scheduler. The next poll retries.
            Debug.WriteLine(
                $"[TemporalCommitments] SCHEDULER PASS ERROR | {ex}");
        }
    }

    private async Task ProcessDueAsync(
        CancellationToken cancellationToken)
    {
        bool userRecentlyActive =
            _worldState.Current.User.IdleTime < TimeSpan.FromMinutes(2);

        IReadOnlyList<NIRACommitmentState> due =
            await _selfModel.ClaimDueTemporalCommitmentsAsync(
                DateTimeOffset.UtcNow,
                WakeLease,
                maximumCommitments: 4,
                allowFlexibleWake: userRecentlyActive,
                cancellationToken: cancellationToken);

        foreach (NIRACommitmentState commitment in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Debug.WriteLine(
                $"[TemporalCommitments] WAKE | " +
                $"Id={commitment.Id:D} | " +
                $"Summary='{TrimLog(commitment.Summary)}' | " +
                $"Schedule='{TrimLog(_temporal.DescribeForCognition(commitment.Temporal))}'");

            try
            {
                await _background.ProcessInternalAsync(
                    NIRAMindEvent.TemporalCommitmentWake(
                        commitment,
                        _temporal.BuildCognitionContext(),
                        _temporal.DescribeForCognition(commitment.Temporal)),
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[TemporalCommitments] WAKE ERROR | " +
                    $"Id={commitment.Id:D} | {ex}");
            }
            finally
            {
                // The internal run includes post-experience commitment
                // formation before it returns. If the reminder completed the
                // obligation, this is a no-op. Otherwise we safely schedule
                // another reconsideration or the next recurring occurrence.
                try
                {
                    if (commitment.Temporal != null)
                    {
                        await _selfModel.RearmTemporalCommitmentAfterWakeAsync(
                            commitment.Id,
                            commitment.Temporal.Revision,
                            commitment.Temporal.WakeCount,
                            DateTimeOffset.UtcNow,
                            cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[TemporalCommitments] REARM ERROR | " +
                        $"Id={commitment.Id:D} | {ex}");
                }
            }
        }
    }

    private static string TrimLog(string value)
    {
        const int maximumLength = 180;
        string clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }
}


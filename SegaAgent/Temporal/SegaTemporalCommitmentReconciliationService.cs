/*
 * filename: SegaTemporalCommitmentReconciliationService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using SegaAgent.Self.Model;

namespace SegaAgent.Temporal;

public sealed class SegaTemporalCommitmentReconciliationService
    : BackgroundService
{
    private const int ReviewVersion = 1;
    private const double MinimumApplyConfidence = 0.78;

    private static readonly TimeSpan ReviewInterval =
        TimeSpan.FromMinutes(10);

    private readonly SegaSelfModelService _selfModel;
    private readonly SegaSelfModelStore _store;
    private readonly SegaTemporalCommitmentReasoner _reasoner;

    public SegaTemporalCommitmentReconciliationService(
        SegaSelfModelService selfModel,
        SegaSelfModelStore store,
        SegaTemporalCommitmentReasoner reasoner)
    {
        _selfModel = selfModel ?? throw new ArgumentNullException(nameof(selfModel));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _reasoner = reasoner ?? throw new ArgumentNullException(nameof(reasoner));
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        Debug.WriteLine("[TemporalCommitments] RECONCILIATION STARTED");

        await RunReviewPassSafelyAsync(stoppingToken);

        using PeriodicTimer timer = new(ReviewInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunReviewPassSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }


    private async Task RunReviewPassSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await ReviewCandidatesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reconciliation is repair work, never a reason to kill Sega's
            // host. A later pass may retry transient failures.
            Debug.WriteLine(
                $"[TemporalCommitments] RECONCILIATION PASS ERROR | {ex}");
        }
    }

    private async Task ReviewCandidatesAsync(
        CancellationToken cancellationToken)
    {
        SegaCommitmentState[] candidates =
            _selfModel.CurrentCommitments
                .Where(value => value.IsActive && value.Temporal == null)
                .OrderBy(value => value.CreatedAt)
                .Take(8)
                .ToArray();

        foreach (SegaCommitmentState commitment in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _store.HasTemporalReviewAsync(
                    commitment.Id,
                    ReviewVersion,
                    cancellationToken))
            {
                continue;
            }

            SegaTemporalInterpretationResult result;

            try
            {
                result = await _reasoner.ReviewAsync(
                    commitment,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[TemporalCommitments] RECONCILIATION ERROR | " +
                    $"Id={commitment.Id:D} | {ex.Message}");

                await _store.RecordTemporalReviewAsync(
                    commitment.Id,
                    ReviewVersion,
                    "Error",
                    "Temporal reconciliation provider/runtime error.",
                    DateTimeOffset.UtcNow,
                    cancellationToken);

                continue;
            }

            bool applied = false;

            if (result.HasTemporalMeaning &&
                result.Temporal != null &&
                result.Confidence >= MinimumApplyConfidence)
            {
                applied = await _selfModel.ReconcileTemporalScheduleAsync(
                    commitment.Id,
                    result.Temporal,
                    result.EvidenceQuote,
                    result.Reason,
                    commitment.CreatedAt,
                    cancellationToken);
            }

            string outcome = applied
                ? "Scheduled"
                : result.HasTemporalMeaning
                    ? "Rejected"
                    : "NoTemporalMeaning";

            await _store.RecordTemporalReviewAsync(
                commitment.Id,
                ReviewVersion,
                outcome,
                result.Reason,
                DateTimeOffset.UtcNow,
                cancellationToken);

            Debug.WriteLine(
                $"[TemporalCommitments] RECONCILIATION {outcome.ToUpperInvariant()} | " +
                $"Id={commitment.Id:D} | " +
                $"Confidence={result.Confidence:0.00} | " +
                $"Summary='{TrimLog(commitment.Summary)}'");
        }
    }

    private static string TrimLog(string value)
    {
        const int maximumLength = 160;
        string clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }
}

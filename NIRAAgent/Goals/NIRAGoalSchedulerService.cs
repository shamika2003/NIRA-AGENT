/*
 * filename: NIRAGoalSchedulerService.cs
 */

using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using NIRAAgent.Mind;

namespace NIRAAgent.Goals;

public sealed class NIRAGoalSchedulerService : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromSeconds(2);

    private readonly NIRAGoalService _goals;
    private readonly NIRABackgroundProcessor _background;

    public NIRAGoalSchedulerService(
        NIRAGoalService goals,
        NIRABackgroundProcessor background)
    {
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
        _background = background ?? throw new ArgumentNullException(nameof(background));
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        Debug.WriteLine("[GoalScheduler] STARTED");

        using PeriodicTimer timer = new(PollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                IReadOnlyList<NIRAGoalState> due =
                    await _goals.ClaimDueGoalsAsync(
                        DateTimeOffset.UtcNow,
                        maximumGoals: 3,
                        stoppingToken);

                foreach (NIRAGoalState goal in due)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    Debug.WriteLine(
                        $"[GoalScheduler] WAKE | " +
                        $"Id={goal.Id:D} | Objective='{TrimLog(goal.Objective)}'");

                    try
                    {
                        await _background.ProcessInternalAsync(
                            NIRAMindEvent.GoalWake(goal),
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
                            $"[GoalScheduler] ERROR | Id={goal.Id:D} | {ex}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static string TrimLog(string value)
    {
        const int maximumLength = 140;
        string clean = value.Trim();
        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }
}


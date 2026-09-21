/*
 * filename: NIRAInteractionObservationService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using NIRAAgent.Character.History;

namespace NIRAAgent.Character.Interaction;

public sealed class NIRAInteractionObservationService
    : IHostedService
{
    private const int MaximumCachedContexts =
        250;


    private readonly NIRASocialHistoryService
        _history;


    private readonly NIRAInteractionContextBuilder
        _contextBuilder;


    private readonly object _sync =
        new();


    private NIRAInteractionContext?
        _latest;


    private readonly Dictionary<
        Guid,
        NIRAInteractionContext>
        _contexts =
            new();


    public event Action<
        NIRAInteractionContext>?
        InteractionObserved;


    public NIRAInteractionObservationService(
        NIRASocialHistoryService history,
        NIRAInteractionContextBuilder contextBuilder)
    {
        _history =
            history
            ?? throw new ArgumentNullException(
                nameof(history));


        _contextBuilder =
            contextBuilder
            ?? throw new ArgumentNullException(
                nameof(contextBuilder));
    }


    public NIRAInteractionContext?
        Latest
    {
        get
        {
            lock (_sync)
            {
                return _latest;
            }
        }
    }


    public NIRAInteractionContext?
        GetForEvent(
            Guid eventId)
    {
        lock (_sync)
        {
            return _contexts.TryGetValue(
                    eventId,
                    out NIRAInteractionContext?
                        context)
                ? context
                : null;
        }
    }


    public Task StartAsync(
        CancellationToken cancellationToken)
    {
        _history.EventRecorded +=
            History_EventRecorded;


        Debug.WriteLine(
            "[Interaction] OBSERVATION SERVICE STARTED");


        return Task.CompletedTask;
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        _history.EventRecorded -=
            History_EventRecorded;


        Debug.WriteLine(
            "[Interaction] OBSERVATION SERVICE STOPPED");


        return Task.CompletedTask;
    }


    private void History_EventRecorded(
        NIRASocialEvent socialEvent)
    {
        try
        {
            NIRAInteractionContext context =
                _contextBuilder.Build(
                    socialEvent);


            lock (_sync)
            {
                _latest =
                    context;


                _contexts[
                    socialEvent.Id] =
                        context;


                TrimCache();
            }


            Debug.WriteLine(
                $"[Interaction] " +
                $"Event=#{socialEvent.Sequence} | " +
                $"Topic='{socialEvent.TopicKey}' | " +
                $"TopicCount=" +
                $"{context.RecentTopicOccurrences} | " +
                $"Semantic=" +
                $"{context.Semantic.Available} | " +
                $"Closest=" +
                $"{context.Semantic.ClosestSimilarity:F3} | " +
                $"Recurrence=" +
                $"{context.SemanticRecurrence:F3} | " +
                $"AlreadyResponded=" +
                $"{context.NIRAAlreadyRespondedRecently}");


            Publish(
                context);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[Interaction] ERROR: {ex}");
        }
    }


    private void TrimCache()
    {
        int excess =
            _contexts.Count -
            MaximumCachedContexts;


        if (excess <=
            0)
        {
            return;
        }


        Guid[] oldest =
            _contexts
                .OrderBy(
                    pair =>
                        pair.Value
                            .Event
                            .Sequence)
                .Take(
                    excess)
                .Select(
                    pair =>
                        pair.Key)
                .ToArray();


        foreach (
            Guid id
            in oldest)
        {
            _contexts.Remove(
                id);
        }
    }


    private void Publish(
        NIRAInteractionContext context)
    {
        Action<NIRAInteractionContext>?
            handlers =
                InteractionObserved;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<NIRAInteractionContext>
                handler
            in handlers.GetInvocationList())
        {
            try
            {
                handler(
                    context);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[Interaction] OBSERVER ERROR: {ex}");
            }
        }
    }
}

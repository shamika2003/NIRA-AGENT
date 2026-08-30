/*
 * filename: SegaInteractionObservationService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

using SegaAgent.Character.History;

namespace SegaAgent.Character.Interaction;

public sealed class SegaInteractionObservationService
    : IHostedService
{
    private const int MaximumCachedContexts =
        250;


    private readonly SegaSocialHistoryService
        _history;


    private readonly SegaInteractionContextBuilder
        _contextBuilder;


    private readonly object _sync =
        new();


    private SegaInteractionContext?
        _latest;


    private readonly Dictionary<
        Guid,
        SegaInteractionContext>
        _contexts =
            new();


    public event Action<
        SegaInteractionContext>?
        InteractionObserved;


    public SegaInteractionObservationService(
        SegaSocialHistoryService history,
        SegaInteractionContextBuilder contextBuilder)
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


    public SegaInteractionContext?
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


    public SegaInteractionContext?
        GetForEvent(
            Guid eventId)
    {
        lock (_sync)
        {
            return _contexts.TryGetValue(
                    eventId,
                    out SegaInteractionContext?
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
        SegaSocialEvent socialEvent)
    {
        try
        {
            SegaInteractionContext context =
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
                $"{context.SegaAlreadyRespondedRecently}");


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
        SegaInteractionContext context)
    {
        Action<SegaInteractionContext>?
            handlers =
                InteractionObserved;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<SegaInteractionContext>
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
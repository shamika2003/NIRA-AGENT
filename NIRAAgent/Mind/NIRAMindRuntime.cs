/*
 * filename: NIRAMindRuntime.cs
 */

using System.Runtime.CompilerServices;
using System.Diagnostics;

using NIRAAgent.Agent.State;
using NIRAAgent.Character.History;
using NIRAAgent.Conversation;
using NIRAAgent.Integrations.Elvara;
using NIRAAgent.Perception;

namespace NIRAAgent.Mind;

public sealed class NIRAMindRuntime
{
    private readonly NIRAExecutive
        _executive;


    private readonly NIRAMindActivityTracker
        _activity;


    private readonly NIRAStateService
        _state;


    private readonly NIRASocialHistoryService
        _socialHistory;


    private readonly ConversationManager
        _conversation;


    // Run slots bound concurrent user-turn reasoning without serializing an
    // entire long-running user task. Each run retains its own async context;
    // the Executive remains the sole owner of tool authorization and effects.
    // New messages may wait for a slot, but do not cancel existing work.
    internal const int MaximumConcurrentUserRuns = 3;
    private readonly SemaphoreSlim _userRunSlots =
        new(MaximumConcurrentUserRuns, MaximumConcurrentUserRuns);


    private readonly NIRAActiveTurnLedger _activeUserWork = new();

    private int
        _activeCognitionRuns;


    public NIRAMindRuntime(
        NIRAExecutive executive,
        NIRAMindActivityTracker activity,
        NIRAStateService state,
        NIRASocialHistoryService socialHistory,
        ConversationManager conversation)
    {
        _executive =
            executive
            ?? throw new ArgumentNullException(
                nameof(executive));


        _activity =
            activity
            ?? throw new ArgumentNullException(
                nameof(activity));


        _state =
            state
            ?? throw new ArgumentNullException(
                nameof(state));


        _socialHistory =
            socialHistory
            ?? throw new ArgumentNullException(
                nameof(socialHistory));


        _conversation =
            conversation
            ?? throw new ArgumentNullException(
                nameof(conversation));

        _executive.AttachActiveWorkLedger(_activeUserWork);
        Debug.WriteLine($"[ConcurrentMind] READY | Revision=NIRA-CONCURRENT-20261014-S2 | UserRunSlots={MaximumConcurrentUserRuns}");
    }


    public async IAsyncEnumerable<NIRAOutputChunk> ProcessUserMessageAsync(
        string userInput,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default,
        Guid? attachedPastChatSessionId = null,
        IReadOnlyList<string>? imagePaths = null)
    {
        if (string.IsNullOrWhiteSpace(
                userInput))
        {
            yield break;
        }


        await _userRunSlots
            .WaitAsync(
                cancellationToken);


        try
        {
            string input =
                userInput.Trim();


            _activity.RecordUserInteraction();


            NIRASocialEvent socialEvent =
                _socialHistory.Record(
                    NIRASocialEventSource.User,
                    NIRASocialEventKind.UserMessage,
                    "UserMessage",
                    NIRASocialTopicKeys.UserConversation,
                    input);


            _conversation.AddUserMessage(
                input,
                socialEvent.Id);


            Dictionary<string, string> userMetadata = new();
            var explicitImages = NIRAAgent.AI.Cognition.NIRAImageInput.FindExplicitLocalImages(input);
            var allImages = (imagePaths ?? Array.Empty<string>()).Concat(explicitImages).ToArray();
            if (allImages.Length > 0)
                userMetadata[NIRAAgent.AI.Cognition.NIRAImageInput.MetadataKey] =
                    NIRAAgent.AI.Cognition.NIRAImageInput.Serialize(allImages);
            if (attachedPastChatSessionId is Guid sid && sid != Guid.Empty)
                userMetadata["attachedPastChatSessionId"] = sid.ToString("D");
            NIRAActiveWorkObservation otherWork = _activeUserWork.Observe();
            if (otherWork.Context.Length > 0)
            {
                userMetadata["otherActiveUserWork"] = otherWork.Context;
                userMetadata["observedActiveWorkIds"] = otherWork.ObservedIds;
            }
            else
            {
                string recentWork = _activeUserWork.RecentOutcomeContext();
                if (recentWork.Length > 0)
                    userMetadata["otherActiveUserWork"] = recentWork;
            }

            NIRAMindEvent mindEvent =
                NIRAMindEvent.UserMessage(input, socialEvent.Id,
                    socialEvent.TopicKey) with { Metadata = userMetadata };


            await foreach (
                NIRAOutputChunk chunk
                in RunTrackedAsync(
                    mindEvent,
                    autonomous:
                        false,
                    cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            _userRunSlots.Release();
        }
    }


    // =========================================================
    // ELVARA EMBEDDED APPLICATION USER MESSAGE
    //
    // This is still the SAME NIRA runtime. The only separation is
    // the short-term conversation scope used for continuity.
    // Personality, mood, memory, self-model, cognition and LLM
    // remain NIRA's existing global systems.
    // =========================================================

    public async IAsyncEnumerable<NIRAOutputChunk> ProcessExternalAppMessageAsync(
        string userInput,
        NIRAExternalAppContext appContext,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            appContext);


        if (string.IsNullOrWhiteSpace(
                userInput))
        {
            yield break;
        }


        string appId =
            NormalizeExternalValue(
                appContext.AppId,
                80,
                required:
                    true);


        string surface =
            NormalizeExternalValue(
                appContext.Surface,
                120,
                required:
                    true);


        string page =
            NormalizeExternalValue(
                appContext.Page,
                160,
                required:
                    false);


        string selectedEntity =
            NormalizeExternalValue(
                appContext.SelectedEntity,
                160,
                required:
                    false);


        await _userRunSlots
            .WaitAsync(
                cancellationToken);


        try
        {
            using IDisposable conversationScope =
                _conversation.PushScope(
                    $"app:{appId}",
                    persistToArchive:
                        false);


            string input =
                userInput.Trim();


            _activity.RecordUserInteraction();


            NIRASocialEvent socialEvent =
                _socialHistory.Record(
                    NIRASocialEventSource.User,
                    NIRASocialEventKind.UserMessage,
                    "ExternalAppUserMessage",
                    $"elvara:{appId}",
                    input);


            _conversation.AddUserMessage(
                input,
                socialEvent.Id);


            Dictionary<string, string> metadata =
                new(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["externalAppId"] =
                        appId,

                    ["externalAppSurface"] =
                        surface
                };


            if (!string.IsNullOrWhiteSpace(
                    page))
            {
                metadata[
                    "externalAppPage"] =
                        page;
            }


            if (!string.IsNullOrWhiteSpace(
                    selectedEntity))
            {
                metadata[
                    "externalAppSelectedEntity"] =
                        selectedEntity;
            }


            NIRAActiveWorkObservation otherWork = _activeUserWork.Observe();
            if (otherWork.Context.Length > 0)
            {
                metadata["otherActiveUserWork"] = otherWork.Context;
                metadata["observedActiveWorkIds"] = otherWork.ObservedIds;
            }

            NIRAMindEvent mindEvent =
                NIRAMindEvent.UserMessage(
                    input,
                    socialEvent.Id,
                    socialEvent.TopicKey) with
                {
                    Name =
                        "ExternalAppUserMessage",

                    Metadata =
                        metadata
                };


            await foreach (
                NIRAOutputChunk chunk
                in RunTrackedAsync(
                    mindEvent,
                    autonomous:
                        false,
                    cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            _userRunSlots.Release();
        }
    }


    private static string NormalizeExternalValue(
        string? value,
        int maximumLength,
        bool required)
    {
        string clean =
            value?.Trim()
            ??
            string.Empty;


        if (
            required
            &&
            string.IsNullOrWhiteSpace(
                clean))
        {
            throw new ArgumentException(
                "Required ELVARA application context is missing.");
        }


        if (clean.Length >
            maximumLength)
        {
            clean =
                clean[
                    ..maximumLength];
        }


        return clean;
    }


    public IAsyncEnumerable<NIRAOutputChunk> ProcessInternalAsync(
        NIRAMindEvent mindEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);


        if (mindEvent.Source !=
            NIRAMindEventSource.Internal)
        {
            throw new ArgumentException(
                "ProcessInternalAsync requires an Internal NIRAMindEvent.",
                nameof(mindEvent));
        }


        return RunTrackedAsync(
            mindEvent,
            autonomous: true,
            cancellationToken);
    }


    public IAsyncEnumerable<NIRAOutputChunk> ProcessPerceptionAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            perception);


        return RunTrackedAsync(
            NIRAMindEvent.FromPerception(
                perception,
                proactive: false),
            autonomous: true,
            cancellationToken);
    }


    public IAsyncEnumerable<NIRAOutputChunk> ProcessProactiveAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            perception);


        return RunTrackedAsync(
            NIRAMindEvent.FromPerception(
                perception,
                proactive: true),
            autonomous: true,
            cancellationToken);
    }


    private async IAsyncEnumerable<NIRAOutputChunk> RunTrackedAsync(
        NIRAMindEvent mindEvent,
        bool autonomous,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        using NIRAActiveTurnLedger.Lease? activeTurn =
            mindEvent.Source == NIRAMindEventSource.User
                ? _activeUserWork.Enter(mindEvent.Content)
                : null;
        BeginCognitionRun();

        bool completed = false;
        bool publishedReply = false;

        try
        {
            await foreach (
                NIRAOutputChunk chunk
                in _executive.RunAsync(
                    mindEvent,
                    cancellationToken))
            {
                if (chunk.Type == NIRAOutputChunkType.Text &&
                    (!string.IsNullOrWhiteSpace(chunk.Content) || chunk.DisplayBlocks.Count > 0))
                    publishedReply = true;
                if (chunk.Type == NIRAOutputChunkType.Completed)
                {
                    completed = true;
                    activeTurn?.Finish(publishedReply);
                }

                string origin = mindEvent.Content;
                if (mindEvent.Source != NIRAMindEventSource.User)
                {
                    if (mindEvent.Metadata.TryGetValue("goalObjective", out string? goal) &&
                        !string.IsNullOrWhiteSpace(goal))
                        origin = goal;
                    else if (mindEvent.Metadata.TryGetValue("branchObjective", out string? branch) &&
                        !string.IsNullOrWhiteSpace(branch))
                        origin = branch;
                    else if (mindEvent.Metadata.TryGetValue("commitmentSummary", out string? commitment) &&
                        !string.IsNullOrWhiteSpace(commitment))
                        origin = commitment;
                    else
                        origin = mindEvent.Name;
                }
                origin = origin.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (origin.Length > 150) origin = origin[..150].TrimEnd() + "…";
                yield return chunk with { OriginPrompt = origin };
            }
        }
        finally
        {
            EndCognitionRun();


            if (
                autonomous
                &&
                completed)
            {
                _activity.RecordAutonomousActivity();
            }
        }
    }


    private void BeginCognitionRun()
    {
        _activity.BeginRun();


        int active =
            Interlocked.Increment(
                ref _activeCognitionRuns);


        if (active ==
            1)
        {
            _state.SetThinking(
                true);
        }
    }


    private void EndCognitionRun()
    {
        _activity.EndRun();


        int active =
            Interlocked.Decrement(
                ref _activeCognitionRuns);


        if (active <=
            0)
        {
            Interlocked.Exchange(
                ref _activeCognitionRuns,
                0);


            _state.SetThinking(
                false);
        }
    }
}

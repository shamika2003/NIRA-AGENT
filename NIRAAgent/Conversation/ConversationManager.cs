/*
 * filename: ConversationManager.cs
 */

using System.Diagnostics;
using System.Text;

namespace NIRAAgent.Conversation;


// =============================================================
// CONVERSATION MANAGER
//
// NIRA's short-lived working conversation memory.
//
// This is intentionally different from:
//
// - NIRASocialHistoryService
// - NIRASemanticMemoryService
// - NIRALongTermMemoryService
//
// The retained transcript and the cognition window are both
// strictly bounded. Durable facts, project knowledge and shared
// experiences belong in long-term memory instead of an infinite
// chat transcript.
// =============================================================

public sealed class ConversationManager
{
    // =========================================================
    // RETENTION WINDOW
    //
    // This is what remains in process memory. It is deliberately
    // larger than the context sent to cognition so NIRA preserves
    // a little working margin without allowing unbounded growth.
    // =========================================================

    private const int RetainedMessageLimit =
        48;


    private const int RetainedCharacterLimit =
        64000;


    // =========================================================
    // COGNITION WINDOW
    //
    // Only this recent subset is supplied to main cognition.
    // The current user event is supplied separately by the mind
    // runtime and is therefore excluded from this context when it
    // is the newest matching user message.
    // =========================================================

    public const int ContextMessageLimit =
        18;


    public const int ContextCharacterLimit =
        18000;


    // =========================================================
    // STATE
    // =========================================================

    private readonly object
        _sync =
            new();


    private readonly List<ConversationMessage>
        _messages =
            new();

    // Short-lived conversational handoff, never action authorization or
    // evidence of task completion. Persistent goals retain their own state.
    private ConversationPendingTask? _pendingTask;

    public ConversationPendingTask? PendingTask
    {
        get
        {
            lock (_sync)
                return _pendingTask;
        }
    }

    public void RememberUnresolvedTask(string objective, string question)
    {
        if (string.IsNullOrWhiteSpace(objective) ||
            string.IsNullOrWhiteSpace(question))
            return;

        lock (_sync)
        {
            // A follow-up clarification must not overwrite the actual request.
            _pendingTask = new ConversationPendingTask(
                _pendingTask?.Objective ?? objective.Trim(), question.Trim());
        }
    }

    public void ResolvePendingTask()
    {
        lock (_sync)
            _pendingTask = null;
    }


    // =========================================================
    // COUNT
    // =========================================================

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _messages.Count;
            }
        }
    }


    // =========================================================
    // SNAPSHOT
    // =========================================================

    public IReadOnlyList<ConversationMessage> Messages =>
        GetMessages();


    // =========================================================
    // ADD USER
    // =========================================================

    public void AddUserMessage(
        string content)
    {
        AddMessage(
            "user",
            content);
    }


    // =========================================================
    // ADD ASSISTANT
    // =========================================================

    public void AddAssistantMessage(
        string content)
    {
        AddMessage(
            "assistant",
            content);
    }


    // =========================================================
    // ADD
    // =========================================================

    private void AddMessage(
        string role,
        string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            role);


        ArgumentException.ThrowIfNullOrWhiteSpace(
            content);


        string normalizedRole =
            role.Trim()
                .ToLowerInvariant();


        string normalizedContent =
            content.Trim();


        int removed;


        lock (_sync)
        {
            _messages.Add(
                new ConversationMessage(
                    normalizedRole,
                    normalizedContent));


            removed =
                TrimRetentionWindow();
        }


        if (removed >
            0)
        {
            Debug.WriteLine(
                $"[Conversation] TRIM | " +
                $"Removed={removed} | " +
                $"Retained={Count}");
        }
    }


    // =========================================================
    // CLEAR
    // =========================================================

    public void Clear()
    {
        lock (_sync)
        {
            _messages.Clear();
            _pendingTask = null;
        }
    }


    // =========================================================
    // RETAINED SNAPSHOT
    // =========================================================

    public List<ConversationMessage> GetMessages()
    {
        lock (_sync)
        {
            return new List<ConversationMessage>(
                _messages);
        }
    }


    // =========================================================
    // RECENT COGNITION WINDOW
    //
    // Selection proceeds newest -> oldest and is then reversed
    // so chronological order is preserved.
    //
    // The newest eligible previous message is always retained
    // even if it alone exceeds the normal character budget.
    // =========================================================

    public ConversationContextSnapshot BuildContextSnapshot(
        string? currentUserMessageToExclude = null,
        int maximumMessages =
            ContextMessageLimit,
        int maximumCharacters =
            ContextCharacterLimit)
    {
        maximumMessages =
            Math.Max(
                1,
                maximumMessages);


        maximumCharacters =
            Math.Max(
                1,
                maximumCharacters);


        string? normalizedCurrentUserMessage =
            string.IsNullOrWhiteSpace(
                    currentUserMessageToExclude)
                ? null
                : currentUserMessageToExclude.Trim();


        lock (_sync)
        {
            if (_messages.Count ==
                0)
            {
                return ConversationContextSnapshot.Empty;
            }


            int endIndex =
                _messages.Count -
                1;


            bool excludedCurrentUserMessage =
                false;


            if (
                normalizedCurrentUserMessage !=
                    null
                &&
                endIndex >=
                    0)
            {
                ConversationMessage newest =
                    _messages[endIndex];


                if (
                    newest.Role.Equals(
                        "user",
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    newest.Content.Equals(
                        normalizedCurrentUserMessage,
                        StringComparison.Ordinal))
                {
                    endIndex--;


                    excludedCurrentUserMessage =
                        true;
                }
            }


            if (endIndex <
                0)
            {
                return new ConversationContextSnapshot
                {
                    RetainedMessages =
                        _messages.Count,

                    EligiblePreviousMessages =
                        0,

                    IncludedMessages =
                        0,

                    CharacterCount =
                        0,

                    ExcludedCurrentUserMessage =
                        excludedCurrentUserMessage,

                    WasLimited =
                        false,

                    Content =
                        string.Empty
                };
            }


            int eligiblePreviousMessages =
                endIndex +
                1;


            List<ConversationMessage> selected =
                new(
                    Math.Min(
                        maximumMessages,
                        eligiblePreviousMessages));


            int estimatedCharacters =
                0;


            for (
                int index =
                    endIndex;
                index >=
                    0;
                index--)
            {
                if (selected.Count >=
                    maximumMessages)
                {
                    break;
                }


                ConversationMessage message =
                    _messages[index];


                int messageCharacters =
                    CountCharacters(
                        message);


                bool firstSelected =
                    selected.Count ==
                    0;


                if (
                    !firstSelected
                    &&
                    estimatedCharacters +
                        messageCharacters >
                    maximumCharacters)
                {
                    break;
                }


                selected.Add(
                    message);


                estimatedCharacters +=
                    messageCharacters;
            }


            selected.Reverse();


            // If the window boundary cut into a normal
            // user/assistant pair, discard the orphaned leading
            // assistant message rather than starting context with
            // a reply whose question is no longer present.
            if (
                selected.Count >
                    1
                &&
                selected[0].Role.Equals(
                    "assistant",
                    StringComparison.OrdinalIgnoreCase)
                &&
                selected[1].Role.Equals(
                    "user",
                    StringComparison.OrdinalIgnoreCase))
            {
                selected.RemoveAt(
                    0);
            }


            string content =
                FormatContext(
                    selected);


            bool wasLimited =
                selected.Count <
                eligiblePreviousMessages;


            return new ConversationContextSnapshot
            {
                RetainedMessages =
                    _messages.Count,

                EligiblePreviousMessages =
                    eligiblePreviousMessages,

                IncludedMessages =
                    selected.Count,

                CharacterCount =
                    content.Length,

                ExcludedCurrentUserMessage =
                    excludedCurrentUserMessage,

                WasLimited =
                    wasLimited,

                Content =
                    content
            };
        }
    }


    // =========================================================
    // BUILD CONTEXT
    //
    // Kept as a convenience/compatibility API for callers that
    // only need the text.
    // =========================================================

    public string BuildContext(
        int maximumMessages =
            ContextMessageLimit,
        int maximumCharacters =
            ContextCharacterLimit)
    {
        return BuildContextSnapshot(
                currentUserMessageToExclude: null,
                maximumMessages,
                maximumCharacters)
            .Content;
    }


    // =========================================================
    // FORMAT
    // =========================================================

    private static string FormatContext(
        IReadOnlyList<ConversationMessage> messages)
    {
        if (messages.Count ==
            0)
        {
            return string.Empty;
        }


        StringBuilder builder =
            new();


        for (
            int index = 0;
            index < messages.Count;
            index++)
        {
            ConversationMessage message =
                messages[index];


            if (index >
                0)
            {
                builder.AppendLine();
            }


            builder.Append(
                message.Role);


            builder.Append(
                ": ");


            builder.Append(
                message.Content);
        }


        return builder.ToString();
    }


    // =========================================================
    // RETENTION TRIM
    // =========================================================

    private int TrimRetentionWindow()
    {
        int removed =
            0;


        while (_messages.Count >
            RetainedMessageLimit)
        {
            _messages.RemoveAt(
                0);


            removed++;
        }


        int totalCharacters =
            0;


        for (
            int index = 0;
            index < _messages.Count;
            index++)
        {
            totalCharacters +=
                CountCharacters(
                    _messages[index]);
        }


        // Always retain the newest message even if that one
        // message alone is unusually large.
        while (
            _messages.Count >
                1
            &&
            totalCharacters >
                RetainedCharacterLimit)
        {
            totalCharacters -=
                CountCharacters(
                    _messages[0]);


            _messages.RemoveAt(
                0);


            removed++;
        }


        // Conversation is normally stored as user/assistant
        // pairs. If trimming leaves an orphaned old assistant at
        // the front, remove it so the retained working history
        // starts at a useful boundary.
        if (
            _messages.Count >
                1
            &&
            _messages[0].Role.Equals(
                "assistant",
                StringComparison.OrdinalIgnoreCase)
            &&
            _messages[1].Role.Equals(
                "user",
                StringComparison.OrdinalIgnoreCase))
        {
            _messages.RemoveAt(
                0);


            removed++;
        }


        return removed;
    }


    // =========================================================
    // CHARACTER COST
    // =========================================================

    private static int CountCharacters(
        ConversationMessage message)
    {
        return
            message.Role.Length
            +
            2
            +
            message.Content.Length
            +
            Environment.NewLine.Length;
    }
}


// =============================================================
// CONVERSATION MESSAGE
// =============================================================

public sealed record ConversationMessage(
    string Role,
    string Content);


// =============================================================
// CONTEXT SNAPSHOT
//
// Gives cognition/debugging explicit visibility into how the
// bounded working window was constructed without exposing the
// mutable conversation list.
// =============================================================

public sealed record ConversationContextSnapshot
{
    public static ConversationContextSnapshot Empty =>
        new();


    public int RetainedMessages
    {
        get;
        init;
    }


    public int EligiblePreviousMessages
    {
        get;
        init;
    }


    public int IncludedMessages
    {
        get;
        init;
    }


    public int CharacterCount
    {
        get;
        init;
    }


    public bool ExcludedCurrentUserMessage
    {
        get;
        init;
    }


    public bool WasLimited
    {
        get;
        init;
    }


    public string Content
    {
        get;
        init;
    } =
        string.Empty;
}

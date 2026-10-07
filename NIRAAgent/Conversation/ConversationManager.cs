/*
 * filename: ConversationManager.cs
 */

using System.Diagnostics;
using System.Text;
using NIRAAgent.Presentation;

namespace NIRAAgent.Conversation;


// =============================================================
// CONVERSATION MANAGER
//
// NIRA's short-lived working conversation memory.
//
// The SAME conversation subsystem owns every NIRA surface, but
// each embedded ELVARA application receives its own bounded
// working thread.
//
// Default scope:
//     main
//
// Example embedded scope:
//     app:tradeai
//
// Long-term memory, character, mood, self-model and cognition are
// NOT duplicated by these scopes. Only short-term conversational
// continuity is separated.
// =============================================================

public sealed class ConversationManager
{
    private const int RetainedMessageLimit =
        48;


    private const int RetainedCharacterLimit =
        64000;


    public const int ContextMessageLimit =
        18;


    public const int ContextCharacterLimit =
        18000;


    private const string MainScopeId =
        "main";


    private readonly NIRAConversationArchiveStore
        _archive;


    private readonly object
        _sync =
            new();


    private readonly Dictionary<string, ConversationScopeState>
        _scopes =
            new(
                StringComparer.OrdinalIgnoreCase);


    private readonly AsyncLocal<ConversationScopeFrame?>
        _ambientScope =
            new();


    public ConversationManager(
        NIRAConversationArchiveStore archive)
    {
        _archive =
            archive
            ?? throw new ArgumentNullException(
                nameof(archive));


        _scopes[
            MainScopeId] =
                new ConversationScopeState(
                    persistToArchive:
                        true);
    }


    // =========================================================
    // AMBIENT CONVERSATION SCOPE
    //
    // Scope flows with the current async cognition execution.
    // Main desktop callers do not need to set one.
    // =========================================================

    public IDisposable PushScope(
        string scopeId,
        bool persistToArchive = false)
    {
        string normalized =
            NormalizeScopeId(
                scopeId);


        lock (_sync)
        {
            if (!_scopes.ContainsKey(
                    normalized))
            {
                _scopes[
                    normalized] =
                        new ConversationScopeState(
                            persistToArchive);
            }
        }


        ConversationScopeFrame? previous =
            _ambientScope.Value;


        _ambientScope.Value =
            new ConversationScopeFrame(
                normalized,
                previous);


        return new ConversationScopeLease(
            this,
            previous);
    }


    public string CurrentScopeId =>
        _ambientScope.Value?.ScopeId
        ??
        MainScopeId;


    // =========================================================
    // PENDING TASK
    // =========================================================

    public ConversationPendingTask? PendingTask
    {
        get
        {
            lock (_sync)
            {
                return GetCurrentStateUnsafe()
                    .PendingTask;
            }
        }
    }


    public void RememberUnresolvedTask(
        string objective,
        string question)
    {
        if (
            string.IsNullOrWhiteSpace(
                objective)
            ||
            string.IsNullOrWhiteSpace(
                question))
        {
            return;
        }


        lock (_sync)
        {
            ConversationScopeState state =
                GetCurrentStateUnsafe();


            state.PendingTask =
                new ConversationPendingTask(
                    state.PendingTask?.Objective
                        ??
                        objective.Trim(),
                    question.Trim());
        }
    }


    public void ResolvePendingTask()
    {
        lock (_sync)
        {
            GetCurrentStateUnsafe()
                .PendingTask =
                    null;
        }
    }


    // =========================================================
    // COUNT / SNAPSHOT
    // =========================================================

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return GetCurrentStateUnsafe()
                    .Messages
                    .Count;
            }
        }
    }


    public IReadOnlyList<ConversationMessage> Messages =>
        GetMessages();


    // =========================================================
    // ADD USER
    // =========================================================

    public void AddUserMessage(
        string content,
        Guid? sourceEventId = null)
    {
        AddMessage(
            "user",
            content,
            sourceEventId);
    }


    // =========================================================
    // ADD ASSISTANT
    // =========================================================

    public Guid? AddAssistantMessage(
        string content,
        Guid? sourceEventId = null,
        NIRAPresentationSnapshot? presentation = null)
    {
        return AddMessage(
            "assistant",
            content,
            sourceEventId,
            presentation);
    }


    // =========================================================
    // ADD
    // =========================================================

    private Guid? AddMessage(
        string role,
        string content,
        Guid? sourceEventId,
        NIRAPresentationSnapshot? presentation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            role);


        ArgumentException.ThrowIfNullOrWhiteSpace(
            content);


        string normalizedRole =
            role
                .Trim()
                .ToLowerInvariant();


        string normalizedContent =
            content.Trim();


        string scopeId =
            CurrentScopeId;


        bool persistToArchive;


        lock (_sync)
        {
            persistToArchive =
                GetOrCreateStateUnsafe(
                    scopeId,
                    persistToArchive:
                        scopeId.Equals(
                            MainScopeId,
                            StringComparison.OrdinalIgnoreCase))
                .PersistToArchive;
        }


        // The existing desktop conversation keeps its durable archive.
        // Embedded app working threads stay isolated from that archive.
        Guid? archivedMessageId =
            null;


        if (persistToArchive)
        {
            try
            {
                archivedMessageId =
                    _archive.Append(
                        normalizedRole,
                        normalizedContent,
                        sourceEventId,
                        presentation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[ConversationArchive] SAVE_FAILED | " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }


        int removed;


        lock (_sync)
        {
            ConversationScopeState state =
                GetOrCreateStateUnsafe(
                    scopeId,
                    persistToArchive);


            state.Messages.Add(
                new ConversationMessage(
                    normalizedRole,
                    normalizedContent));


            removed =
                TrimRetentionWindow(
                    state.Messages);
        }


        if (removed >
            0)
        {
            Debug.WriteLine(
                $"[Conversation] TRIM | " +
                $"Scope={scopeId} | " +
                $"Removed={removed} | " +
                $"Retained={Count}");
        }


        return archivedMessageId;
    }


    // =========================================================
    // CLEAR
    // =========================================================

    public void Clear()
    {
        lock (_sync)
        {
            ConversationScopeState state =
                GetCurrentStateUnsafe();


            state.Messages.Clear();


            state.PendingTask =
                null;
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
                GetCurrentStateUnsafe()
                    .Messages);
        }
    }


    // =========================================================
    // RECENT COGNITION WINDOW
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
            List<ConversationMessage> messages =
                GetCurrentStateUnsafe()
                    .Messages;


            if (messages.Count ==
                0)
            {
                return ConversationContextSnapshot.Empty;
            }


            int endIndex =
                messages.Count -
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
                    messages[
                        endIndex];


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
                        messages.Count,

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
                    messages[
                        index];


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
                    messages.Count,

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
    // =========================================================

    public string BuildContext(
        int maximumMessages =
            ContextMessageLimit,
        int maximumCharacters =
            ContextCharacterLimit)
    {
        return BuildContextSnapshot(
                currentUserMessageToExclude:
                    null,
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
            int index =
                0;
            index <
                messages.Count;
            index++)
        {
            ConversationMessage message =
                messages[
                    index];


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

    private static int TrimRetentionWindow(
        List<ConversationMessage> messages)
    {
        int removed =
            0;


        while (messages.Count >
            RetainedMessageLimit)
        {
            messages.RemoveAt(
                0);


            removed++;
        }


        int totalCharacters =
            0;


        for (
            int index =
                0;
            index <
                messages.Count;
            index++)
        {
            totalCharacters +=
                CountCharacters(
                    messages[
                        index]);
        }


        while (
            messages.Count >
                1
            &&
            totalCharacters >
                RetainedCharacterLimit)
        {
            totalCharacters -=
                CountCharacters(
                    messages[
                        0]);


            messages.RemoveAt(
                0);


            removed++;
        }


        if (
            messages.Count >
                1
            &&
            messages[0].Role.Equals(
                "assistant",
                StringComparison.OrdinalIgnoreCase)
            &&
            messages[1].Role.Equals(
                "user",
                StringComparison.OrdinalIgnoreCase))
        {
            messages.RemoveAt(
                0);


            removed++;
        }


        return removed;
    }


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


    // =========================================================
    // SCOPE STATE
    // =========================================================

    private ConversationScopeState GetCurrentStateUnsafe()
    {
        string scopeId =
            CurrentScopeId;


        return GetOrCreateStateUnsafe(
            scopeId,
            persistToArchive:
                scopeId.Equals(
                    MainScopeId,
                    StringComparison.OrdinalIgnoreCase));
    }


    private ConversationScopeState GetOrCreateStateUnsafe(
        string scopeId,
        bool persistToArchive)
    {
        if (_scopes.TryGetValue(
                scopeId,
                out ConversationScopeState? state))
        {
            return state;
        }


        state =
            new ConversationScopeState(
                persistToArchive);


        _scopes[
            scopeId] =
                state;


        return state;
    }


    private static string NormalizeScopeId(
        string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            scopeId);


        string clean =
            scopeId
                .Trim()
                .ToLowerInvariant();


        if (clean.Length >
            120)
        {
            throw new ArgumentException(
                "Conversation scope ID is too long.",
                nameof(scopeId));
        }


        foreach (char character in clean)
        {
            if (
                char.IsLetterOrDigit(
                    character)
                ||
                character is
                    ':' or
                    '-' or
                    '_' or
                    '.')
            {
                continue;
            }


            throw new ArgumentException(
                "Conversation scope ID contains unsupported characters.",
                nameof(scopeId));
        }


        return clean;
    }


    private void RestoreScope(
        ConversationScopeFrame? previous)
    {
        _ambientScope.Value =
            previous;
    }


    private sealed class ConversationScopeState
    {
        public ConversationScopeState(
            bool persistToArchive)
        {
            PersistToArchive =
                persistToArchive;
        }


        public List<ConversationMessage> Messages
        {
            get;
        } =
            new();


        public ConversationPendingTask? PendingTask
        {
            get;
            set;
        }


        public bool PersistToArchive
        {
            get;
        }
    }


    private sealed record ConversationScopeFrame(
        string ScopeId,
        ConversationScopeFrame? Previous);


    private sealed class ConversationScopeLease
        : IDisposable
    {
        private ConversationManager?
            _owner;


        private readonly ConversationScopeFrame?
            _previous;


        public ConversationScopeLease(
            ConversationManager owner,
            ConversationScopeFrame? previous)
        {
            _owner =
                owner;


            _previous =
                previous;
        }


        public void Dispose()
        {
            ConversationManager? owner =
                Interlocked.Exchange(
                    ref _owner,
                    null);


            owner?.RestoreScope(
                _previous);
        }
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

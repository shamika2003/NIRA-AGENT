/*
 * filename: MainWindowViewModel.cs
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using NIRAAgent.Mind;
using NIRAAgent.Conversation;
using NIRAAgent.Artifacts;
using NIRAAgent.Settings;
using NIRAAgent.Voice;

namespace NIRAAgent.UI.ViewModels;

public sealed class MainWindowViewModel
    : INotifyPropertyChanged,
      IDisposable
{
    private NIRAConversationArchiveStore? _conversationArchive;

    public void AttachConversationArchive(NIRAConversationArchiveStore store) =>
        _conversationArchive = store;

    private readonly NIRAMindRuntime
        _mind;


    private readonly NIRAOutputDispatcher
        _dispatcher;


    private readonly VoiceQueue
        _voiceQueue;


    private readonly NIRARuntimeSettingsService
        _settings;


    private readonly SpeechChunker
        _userSpeechChunker =
            new();


    private readonly SpeechResponseState
        _userSpeechState =
            new();


    private readonly Dictionary<Guid, BackgroundResponseState>
        _backgroundResponses =
            new();


    // Throttle spoken background milestones across ALL branch runs, not
    // once per event (each result has its own runId).
    private DateTimeOffset _lastBackgroundJourneySpeech = DateTimeOffset.MinValue;
    private string _lastBackgroundJourneySpeechText = string.Empty;

    private readonly AsyncRelayCommand
        _sendCommand;


    private readonly CancellationTokenSource
        _shutdown =
            new();


    private readonly Task
        _backgroundResponseTask;


    private string
        _messageInput =
            string.Empty;


    private bool
        _isProcessing;


    public ObservableCollection<ChatMessageViewModel> Messages
    {
        get;
    } =
        new();


    public event Action<NIRAVisualArtifact>?
        VisualArtifactReceived;

    private Guid? _attachedPastChatSessionId;
    private string _attachedPastChatName = string.Empty;
    public string AttachedPastChatName => _attachedPastChatName;
    public bool HasAttachedPastChat => _attachedPastChatSessionId.HasValue;

    public void AttachPastChat(Guid sessionId, string title)
    {
        if (sessionId == Guid.Empty) return;
        _attachedPastChatSessionId = sessionId;
        _attachedPastChatName = title;
        OnPropertyChanged(nameof(AttachedPastChatName));
        OnPropertyChanged(nameof(HasAttachedPastChat));
    }

    public void ClearPastChatAttachment()
    {
        _attachedPastChatSessionId = null;
        _attachedPastChatName = string.Empty;
        OnPropertyChanged(nameof(AttachedPastChatName));
        OnPropertyChanged(nameof(HasAttachedPastChat));
    }


    public string MessageInput
    {
        get =>
            _messageInput;

        set
        {
            if (_messageInput ==
                value)
            {
                return;
            }


            _messageInput =
                value;


            OnPropertyChanged();


            OnPropertyChanged(
                nameof(CanSend));


            _sendCommand.RaiseCanExecuteChanged();
        }
    }


    public bool IsProcessing
    {
        get =>
            _isProcessing;

        private set
        {
            if (_isProcessing ==
                value)
            {
                return;
            }


            _isProcessing =
                value;


            OnPropertyChanged();


            OnPropertyChanged(
                nameof(CanSend));


            _sendCommand.RaiseCanExecuteChanged();
        }
    }


    public bool CanSend =>
        !IsProcessing
        &&
        !string.IsNullOrWhiteSpace(
            MessageInput);


    public ICommand SendCommand =>
        _sendCommand;


    public MainWindowViewModel(
        NIRAMindRuntime mind,
        NIRAOutputDispatcher dispatcher,
        VoiceQueue voiceQueue,
        NIRARuntimeSettingsService settings)
    {
        _mind =
            mind
            ?? throw new ArgumentNullException(
                nameof(mind));


        _dispatcher =
            dispatcher
            ?? throw new ArgumentNullException(
                nameof(dispatcher));


        _voiceQueue =
            voiceQueue
            ?? throw new ArgumentNullException(
                nameof(voiceQueue));


        _settings =
            settings
            ?? throw new ArgumentNullException(
                nameof(settings));


        _settings.Changed +=
            Settings_Changed;


        _sendCommand =
            new AsyncRelayCommand(
                SendMessageAsync,
                () =>
                    CanSend);


        _backgroundResponseTask =
            ProcessBackgroundResponsesAsync();
    }


    private void SubmitFollowUp(string choice)
    {
        if (string.IsNullOrWhiteSpace(choice)) return;
        // Clicked options become ordinary user messages, not permissioned actions.
        // If busy, prefill rather than submitting into an active cognition run.
        MessageInput = "Regarding your previous answer: " + choice.Trim();
        if (!IsProcessing) _ = SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        string input =
            MessageInput.Trim();


        if (string.IsNullOrWhiteSpace(
                input))
        {
            return;
        }


        // A past chat is a one-turn explicit source, never merged into this transcript.
        Guid? attachedPastChatSessionId = _attachedPastChatSessionId;
        ClearPastChatAttachment();

        _voiceQueue.Interrupt();


        _userSpeechChunker.Clear();


        _userSpeechState.Reset();


        MessageInput =
            string.Empty;


        IsProcessing =
            true;


        ChatMessageViewModel userMessage =
            new(
                "user",
                input);


        Messages.Add(
            userMessage);


        ChatMessageViewModel assistantMessage =
            new(
                "assistant",
                string.Empty);


        assistantMessage.ProgressText = "Working out the next step…";
        Messages.Add(
            assistantMessage);

        // Spoken journey checkpoints are sparse, not a running narration.
        // They share the current reply's queue generation, so a new user turn
        // still interrupts them as usual.
        DateTimeOffset lastSpokenProgress = DateTimeOffset.UtcNow;
        bool hasSpokenProgress = false;
        string lastSpokenProgressText = string.Empty;

        try
        {
            await foreach (
                NIRAOutputChunk chunk
                in _mind.ProcessUserMessageAsync(
                    input,
                    _shutdown.Token,
                    attachedPastChatSessionId))
            {
                if (chunk.Type == NIRAOutputChunkType.Progress)
                {
                    // Progress is not a chat message, a capability result, or
                    // archival material; show only while the reply is pending.
                    if (!assistantMessage.HasContent &&
                        !assistantMessage.HasRichElements &&
                        !assistantMessage.HasVisualArtifacts)
                        assistantMessage.ProgressText = chunk.Content;
                    string spoken = chunk.SpeechContent.Trim();
                    TimeSpan gap = DateTimeOffset.UtcNow - lastSpokenProgress;
                    if (_settings.Current.VoiceEnabled &&
                        !string.IsNullOrWhiteSpace(spoken) &&
                        !string.Equals(spoken, lastSpokenProgressText,
                            StringComparison.OrdinalIgnoreCase) &&
                        gap >= (!hasSpokenProgress
                            ? TimeSpan.FromSeconds(6)
                            : chunk.IsProgressCorrection
                                ? TimeSpan.FromSeconds(8)
                                : TimeSpan.FromSeconds(20)))
                    {
                        _voiceQueue.Enqueue(new VoiceUtterance(
                            Guid.NewGuid(), 1, spoken,
                            NIRAVoiceExpression.Neutral));
                        lastSpokenProgress = DateTimeOffset.UtcNow;
                        lastSpokenProgressText = spoken;
                        hasSpokenProgress = true;
                    }
                    continue;
                }
                if (chunk.Type == NIRAOutputChunkType.Text)
                    assistantMessage.ProgressText = string.Empty;
                HandleChunk(
                    assistantMessage,
                    chunk,
                    _userSpeechChunker,
                    _userSpeechState,
                    _settings.Current.VoiceEnabled);
            }


            FlushSpeech(
                _userSpeechChunker,
                _userSpeechState,
                _settings.Current.VoiceEnabled);


            assistantMessage.ProgressText = string.Empty;
            if (assistantMessage.IsEmpty)
            {
                Messages.Remove(
                    assistantMessage);
            }
        }
        catch (OperationCanceledException)
            when (_shutdown.IsCancellationRequested)
        {
            _userSpeechChunker.Clear();


            _userSpeechState.Reset();
        }
        catch (Exception ex)
        {
            _userSpeechChunker.Clear();


            _userSpeechState.Reset();


            assistantMessage.Content =
                $"Sorry, something went wrong.\n\n{ex.Message}";
        }
        finally
        {
            assistantMessage.ProgressText = string.Empty;
            if (assistantMessage.IsEmpty)
                Messages.Remove(assistantMessage);
            IsProcessing =
                false;
        }
    }


    private async Task ProcessBackgroundResponsesAsync()
    {
        try
        {
            await foreach (
                NIRAOutputChunk chunk
                in _dispatcher.ReadAllAsync(
                    _shutdown.Token))
            {
                await System.Windows.Application
                    .Current
                    .Dispatcher
                    .InvokeAsync(
                        () =>
                            HandleBackgroundChunk(
                                chunk));
            }
        }
        catch (OperationCanceledException)
            when (_shutdown.IsCancellationRequested)
        {
        }
    }


    private void HandleBackgroundChunk(
        NIRAOutputChunk chunk)
    {
        if (chunk.Type == NIRAOutputChunkType.Progress)
        {
            if (string.IsNullOrWhiteSpace(chunk.Content)) return;
            BackgroundResponseState progress = GetOrCreateBackgroundResponse(chunk.RunId);
            if (!progress.Message.HasContent &&
                !progress.Message.HasRichElements &&
                !progress.Message.HasVisualArtifacts)
                progress.Message.ProgressText = chunk.Content;

            string spoken = chunk.SpeechContent.Trim();
            if (_settings.Current.VoiceEnabled &&
                _settings.Current.SpeakBackgroundUpdates &&
                !string.IsNullOrWhiteSpace(spoken) &&
                !string.Equals(spoken, _lastBackgroundJourneySpeechText,
                    StringComparison.OrdinalIgnoreCase) &&
                DateTimeOffset.UtcNow - _lastBackgroundJourneySpeech >=
                    (chunk.IsProgressCorrection
                        ? TimeSpan.FromSeconds(8)
                        : TimeSpan.FromSeconds(20)))
            {
                _voiceQueue.Enqueue(new VoiceUtterance(
                    Guid.NewGuid(), 1, spoken, NIRAVoiceExpression.Neutral));
                _lastBackgroundJourneySpeech = DateTimeOffset.UtcNow;
                _lastBackgroundJourneySpeechText = spoken;
            }
            return;
        }

        if (chunk.Type ==
            NIRAOutputChunkType.Cancelled)
        {
            RemoveBackgroundRun(
                chunk.RunId,
                removeMessage: true);


            return;
        }


        if (chunk.Type ==
            NIRAOutputChunkType.VisualArtifact)
        {
            if (chunk.VisualArtifact == null)
            {
                return;
            }

            if (chunk.VisualArtifact.Surface !=
                NIRAVisualArtifactPresentationSurface.ToastOnly)
            {
                BackgroundResponseState visualResponseState =
                    GetOrCreateBackgroundResponse(
                        chunk.RunId);

                visualResponseState.Message.AssociateRun(
                    chunk.RunId);

                visualResponseState.Message.AddVisualArtifact(
                    chunk.VisualArtifact);
            }

            VisualArtifactReceived?.Invoke(
                chunk.VisualArtifact);

            return;
        }


        if (chunk.Type ==
            NIRAOutputChunkType.Completed)
        {
            if (_backgroundResponses.TryGetValue(
                    chunk.RunId,
                    out BackgroundResponseState? state))
            {
                FlushSpeech(
                    state.SpeechChunker,
                    state.SpeechState,
                    _settings.Current.VoiceEnabled &&
                    _settings.Current.SpeakBackgroundUpdates);


                state.SpeechState.Reset();
                // Transient progress is not a permanent chat reply.
                state.Message.ProgressText = string.Empty;
                if (state.Message.IsEmpty)
                    Messages.Remove(state.Message);

                _backgroundResponses.Remove(
                    chunk.RunId);
            }


            return;
        }


        if (
            chunk.Type !=
                NIRAOutputChunkType.Text
            ||
            (string.IsNullOrEmpty(chunk.Content) && chunk.DisplayBlocks.Count == 0 &&
             string.IsNullOrWhiteSpace(chunk.SpeechContent)))
        {
            return;
        }


        BackgroundResponseState responseState =
            GetOrCreateBackgroundResponse(
                chunk.RunId);


        responseState.Message.ProgressText = string.Empty;
        HandleChunk(
            responseState.Message,
            chunk,
            responseState.SpeechChunker,
            responseState.SpeechState,
            _settings.Current.VoiceEnabled &&
            _settings.Current.SpeakBackgroundUpdates);
    }


    private BackgroundResponseState GetOrCreateBackgroundResponse(
        Guid runId)
    {
        if (_backgroundResponses.TryGetValue(
                runId,
                out BackgroundResponseState? existing))
        {
            return existing;
        }

        ChatMessageViewModel message =
            new(
                "assistant",
                string.Empty);

        message.AssociateRun(
            runId);

        Messages.Add(
            message);

        BackgroundResponseState created =
            new(
                message);

        _backgroundResponses[
            runId] =
                created;

        return created;
    }


    private void RemoveBackgroundRun(
        Guid runId,
        bool removeMessage)
    {
        if (!_backgroundResponses.Remove(
                runId,
                out BackgroundResponseState? state))
        {
            return;
        }


        state.SpeechChunker.Clear();


        state.SpeechState.Reset();


        if (removeMessage)
        {
            Messages.Remove(
                state.Message);
        }
    }


    private void HandleChunk(
        ChatMessageViewModel message,
        NIRAOutputChunk chunk,
        SpeechChunker speechChunker,
        SpeechResponseState speechState,
        bool allowSpeech)
    {
        message.AssociateRun(
            chunk.RunId);

        if (chunk.Type ==
            NIRAOutputChunkType.VisualArtifact)
        {
            if (chunk.VisualArtifact != null)
            {
                if (chunk.VisualArtifact.Surface !=
                    NIRAVisualArtifactPresentationSurface.ToastOnly)
                {
                    message.AddVisualArtifact(
                        chunk.VisualArtifact);
                }

                VisualArtifactReceived?.Invoke(
                    chunk.VisualArtifact);
            }

            return;
        }

        if (
            chunk.Type !=
                NIRAOutputChunkType.Text
            ||
            (string.IsNullOrEmpty(chunk.Content) && chunk.DisplayBlocks.Count == 0 &&
             string.IsNullOrWhiteSpace(chunk.SpeechContent)))
        {
            return;
        }


        speechState.Expression =
            chunk.VoiceExpression.Normalize();


        message.Content +=
            chunk.Content;

        if (chunk.DisplayBlocks.Count > 0)
            message.AddRichBlocks(chunk.DisplayBlocks, _conversationArchive,
                chunk.ArchiveMessageId, SubmitFollowUp);


        if (!allowSpeech)
        {
            speechChunker.Clear();
            speechState.Reset();
            return;
        }


        IReadOnlyList<string> speechParts =
            speechChunker.Add(
                string.IsNullOrWhiteSpace(chunk.SpeechContent) ? chunk.Content : chunk.SpeechContent);


        foreach (
            string speechPart
            in speechParts)
        {
            if (string.IsNullOrWhiteSpace(
                    speechPart))
            {
                continue;
            }


            _voiceQueue.Enqueue(
                speechState.Create(
                    speechPart));
        }
    }


    private void FlushSpeech(
        SpeechChunker speechChunker,
        SpeechResponseState speechState,
        bool allowSpeech)
    {
        if (!allowSpeech)
        {
            speechChunker.Clear();
            speechState.Reset();
            return;
        }


        string? remaining =
            speechChunker.Complete();


        if (string.IsNullOrWhiteSpace(
                remaining))
        {
            return;
        }


        _voiceQueue.Enqueue(
            speechState.Create(
                remaining));
    }


    private void Settings_Changed(
        NIRARuntimeSettings before,
        NIRARuntimeSettings after)
    {
        if (
            before.VoiceEnabled != after.VoiceEnabled
            ||
            before.SpeakBackgroundUpdates != after.SpeakBackgroundUpdates
            ||
            before.VoiceEngine != after.VoiceEngine)
        {
            _voiceQueue.Interrupt();
        }
    }


    public event PropertyChangedEventHandler?
        PropertyChanged;


    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }


    public void Dispose()
    {
        _settings.Changed -=
            Settings_Changed;


        _shutdown.Cancel();


        _voiceQueue.Interrupt();


        _userSpeechChunker.Clear();


        _userSpeechState.Reset();


        foreach (
            BackgroundResponseState state
            in _backgroundResponses.Values)
        {
            state.SpeechChunker.Clear();


            state.SpeechState.Reset();
        }


        _backgroundResponses.Clear();


        try
        {
            _backgroundResponseTask
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
        }


        _shutdown.Dispose();
    }


    private sealed class BackgroundResponseState
    {
        public ChatMessageViewModel Message
        {
            get;
        }


        public SpeechChunker SpeechChunker
        {
            get;
        } =
            new();


        public SpeechResponseState SpeechState
        {
            get;
        } =
            new();


        public BackgroundResponseState(
            ChatMessageViewModel message)
        {
            Message =
                message;
        }
    }


    private sealed class SpeechResponseState
    {
        public Guid ResponseId
        {
            get;
            private set;
        } =
            Guid.NewGuid();


        public int Sequence
        {
            get;
            private set;
        }


        public NIRAVoiceExpression Expression
        {
            get;
            set;
        } =
            NIRAVoiceExpression.Neutral;


        public VoiceUtterance Create(
            string text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                text);


            Sequence++;


            return new VoiceUtterance(
                ResponseId,
                Sequence,
                text,
                Expression);
        }


        public void Reset()
        {
            ResponseId =
                Guid.NewGuid();


            Sequence =
                0;


            Expression =
                NIRAVoiceExpression.Neutral;
        }
    }
}




/*
 * filename: MainWindowViewModel.cs
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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


    // User replies have independent speech buffers: a later conversation must
    // never clear an earlier run's text or speech state.
    private int _activeUserTurns;
    private long _newestUserTurn;
    internal const int MaximumQueuedUserTurns = 12;

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


    // Local references selected explicitly in the composer. No binary upload or
    // automatic trust assertion; existing authorized capabilities decide inspection.
    public ObservableCollection<NIRAComposerAttachment> ComposerAttachments { get; } = new();
    // Presentation-only strip: never alters the authoritative attachment list
    // sent to cognition. Hide surplus images behind a +N thumbnail.
    public ObservableCollection<NIRAComposerGalleryTile> ComposerGalleryTiles { get; } = new();
    private void RebuildComposerGalleryTiles()
    {
        ComposerGalleryTiles.Clear();
        NIRAComposerAttachment[] images = ComposerAttachments.Where(a => a.IsImage).ToArray();
        foreach (var image in images.Take(2))
            ComposerGalleryTiles.Add(new NIRAComposerGalleryTile(image, false, 0));
        if (images.Length >= 3)
            ComposerGalleryTiles.Add(new NIRAComposerGalleryTile(images[2], true, images.Length - 2));
        foreach (var document in ComposerAttachments.Where(a => !a.IsImage))
            ComposerGalleryTiles.Add(new NIRAComposerGalleryTile(document, false, 0));
    }
    public bool HasComposerAttachments => ComposerAttachments.Count > 0;
    private const int MaximumComposerAttachments = 12;

    public int AddComposerAttachments(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (string? raw in paths)
        {
            if (ComposerAttachments.Count >= MaximumComposerAttachments) break;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string path;
            try { path = Path.GetFullPath(raw); }
            catch (Exception) { continue; }
            if (!File.Exists(path) || ComposerAttachments.Any(x =>
                string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var info = new FileInfo(path);
                ComposerAttachments.Add(new NIRAComposerAttachment(path, info.Name));
                added++;
            }
            catch (Exception) { /* Unavailable file: never pretend it was attached. */ }
        }
        return added;
    }

    public void RemoveComposerAttachment(NIRAComposerAttachment? attachment)
    {
        if (attachment != null) ComposerAttachments.Remove(attachment);
    }

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
        !_shutdown.IsCancellationRequested &&
        _activeUserTurns < MaximumQueuedUserTurns &&
        (!string.IsNullOrWhiteSpace(MessageInput) || ComposerAttachments.Count > 0);


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


        ComposerAttachments.CollectionChanged += (_, _) =>
        {
            RebuildComposerGalleryTiles();
            OnPropertyChanged(nameof(HasComposerAttachments));
            OnPropertyChanged(nameof(CanSend));
            _sendCommand.RaiseCanExecuteChanged();
        };

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
        // Follow-ups are independent user turns even when an earlier task runs.
        MessageInput = "Regarding your previous answer: " + choice.Trim();
        if (CanSend) _ = SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        string userText = MessageInput.Trim();
        NIRAComposerAttachment[] attachments = ComposerAttachments.ToArray();
        if (string.IsNullOrWhiteSpace(userText) && attachments.Length == 0)
            return;

        // Images travel as real multimodal inputs; other file types remain
        // explicit local references requiring authorized file capabilities.
        string[] imagePaths;
        try
        {
            imagePaths = NIRAAgent.AI.Cognition.NIRAImageInput.Validate(
                attachments.Where(a => a.IsImage).Select(a => a.FullPath)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Messages.Add(new ChatMessageViewModel("assistant", "I couldn't attach those images: " + ex.Message));
            return;
        }
        var otherFiles = attachments.Where(a => !a.IsImage).ToArray();
        string input = string.IsNullOrWhiteSpace(userText)
            ? "Please examine the attached image(s) or selected files."
            : userText;
        if (otherFiles.Length != 0)
            input += "\n\n[Selected file paths; contents not observed]\n" +
                string.Join("\n", otherFiles.Select(a => "- " + a.FullPath));
        if (imagePaths.Length != 0)
            input += "\n[Real image data accompanies this turn. Inspect the attached images directly; do not treat filenames as visual evidence.]";

        // A past chat is a one-turn explicit source, never merged into this transcript.
        Guid? attachedPastChatSessionId = _attachedPastChatSessionId;
        ClearPastChatAttachment();

        // Reserve a slot synchronously on the UI thread before the first await.
        // A long Ultra run should not disable the input for the next message.
        if (_activeUserTurns >= MaximumQueuedUserTurns ||
            _shutdown.IsCancellationRequested)
            return;

        _activeUserTurns++;
        IsProcessing = true;
        long turnSequence = Interlocked.Increment(ref _newestUserTurn);
        SpeechChunker speechChunker = new();
        SpeechResponseState speechState = new();
        _voiceQueue.Interrupt();

        MessageInput = string.Empty;
        ComposerAttachments.Clear();
        OnPropertyChanged(nameof(CanSend));
        _sendCommand.RaiseCanExecuteChanged();


        // Keep the user's actual words separate from selected sources.
        // An image is shown AS an image in the bubble, never as "📎 file.png".
        // The cognition path above still receives the real image bytes.
        ChatMessageViewModel userMessage = new("user", userText);
        userMessage.AddUserAttachments(attachments);

        Messages.Add(userMessage);


        ChatMessageViewModel assistantMessage =
            new(
                "assistant",
                string.Empty);


        // Keep every concurrent reply visually tied to its actual input.
        assistantMessage.SetReplySource(userText.Length > 0 ? userText : input);
        // Start with NIRA's living dots only.
        // Text appears later only when the runtime/model emits a real progress update.
        assistantMessage.BeginJourney();
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
                    attachedPastChatSessionId,
                    imagePaths))
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
                        turnSequence == Interlocked.Read(ref _newestUserTurn) &&
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
                    assistantMessage.BeginProgressFadeOut();
                HandleChunk(
                    assistantMessage,
                    chunk,
                    speechChunker,
                    speechState,
                    _settings.Current.VoiceEnabled &&
                    turnSequence == Interlocked.Read(ref _newestUserTurn));
            }


            FlushSpeech(
                speechChunker,
                speechState,
                _settings.Current.VoiceEnabled &&
                turnSequence == Interlocked.Read(ref _newestUserTurn));


            if (assistantMessage.HasContent ||
                assistantMessage.HasRichElements ||
                assistantMessage.HasVisualArtifacts)
            {
                assistantMessage.BeginProgressFadeOut();
            }
            else
            {
                assistantMessage.ClearProgressImmediately();
                if (assistantMessage.IsEmpty)
                {
                    Messages.Remove(
                        assistantMessage);
                }
            }
        }
        catch (OperationCanceledException)
            when (_shutdown.IsCancellationRequested)
        {
            speechChunker.Clear();
            speechState.Reset();
        }
        catch (Exception ex)
        {
            speechChunker.Clear();
            speechState.Reset();


            assistantMessage.Content =
                $"Sorry, something went wrong.\n\n{ex.Message}";
            assistantMessage.BeginProgressFadeOut();
        }
        finally
        {
            if (assistantMessage.HasContent ||
                assistantMessage.HasRichElements ||
                assistantMessage.HasVisualArtifacts)
            {
                assistantMessage.BeginProgressFadeOut();
            }
            else
            {
                assistantMessage.ClearProgressImmediately();
                if (assistantMessage.IsEmpty)
                    Messages.Remove(assistantMessage);
            }

            speechChunker.Clear();
            speechState.Reset();
            _activeUserTurns = Math.Max(0, _activeUserTurns - 1);
            IsProcessing = _activeUserTurns > 0;
            OnPropertyChanged(nameof(CanSend));
            _sendCommand.RaiseCanExecuteChanged();
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
            BackgroundResponseState progress = GetOrCreateBackgroundResponse(chunk.RunId, chunk.OriginPrompt);
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
                        chunk.RunId, chunk.OriginPrompt);

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
                // Transient progress fades away when a real result exists.
                if (state.Message.HasContent ||
                    state.Message.HasRichElements ||
                    state.Message.HasVisualArtifacts)
                {
                    state.Message.BeginProgressFadeOut();
                }
                else
                {
                    state.Message.ClearProgressImmediately();
                    if (state.Message.IsEmpty)
                        Messages.Remove(state.Message);
                }

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
                chunk.RunId, chunk.OriginPrompt);


        responseState.Message.BeginProgressFadeOut();
        HandleChunk(
            responseState.Message,
            chunk,
            responseState.SpeechChunker,
            responseState.SpeechState,
            _settings.Current.VoiceEnabled &&
            _settings.Current.SpeakBackgroundUpdates);
    }


    private BackgroundResponseState GetOrCreateBackgroundResponse(
        Guid runId, string originPrompt = "")
    {
        if (_backgroundResponses.TryGetValue(
                runId,
                out BackgroundResponseState? existing))
        {
            existing.Message.SetReplySource(originPrompt, background: true);
            return existing;
        }

        ChatMessageViewModel message =
            new(
                "assistant",
                string.Empty);

        message.AssociateRun(
            runId);
        message.SetReplySource(originPrompt.Length > 0 ? originPrompt :
            "Autonomous task update", background: true);

        message.BeginJourney();

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


/// <summary>
/// Composer-only local file reference. The model gets only the path until the
/// separate content/vision ingestion stage is implemented.
/// </summary>
public sealed record NIRAComposerAttachment(string FullPath, string FileName)
{
    private string ExtensionLower => Path.GetExtension(FileName).ToLowerInvariant();

    public bool IsImage => ExtensionLower is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";
    // Do not ask WPF image decoders to open PDF or other non-image attachments.
    public string? PreviewPath => IsImage ? FullPath : null;
    public bool IsAudio => ExtensionLower is ".mp3" or ".wav" or ".m4a" or ".flac" or ".aac";
    public bool IsSpreadsheet => ExtensionLower is ".xlsx" or ".xls" or ".csv";
    public bool IsCodeOrData => ExtensionLower is ".json" or ".xml" or ".yaml" or ".yml" or ".md" or ".txt" or ".cs" or ".py";
    public bool IsPdf => ExtensionLower is ".pdf";

    public string ExtensionLabel
    {
        get
        {
            string ext = Path.GetExtension(FileName);
            return string.IsNullOrWhiteSpace(ext) ? "FILE" : ext.TrimStart('.').ToUpperInvariant();
        }
    }

    public string TypeGlyph =>
        IsImage ? "▣" :
        IsAudio ? "♪" :
        IsSpreadsheet ? "▤" :
        IsPdf ? "▥" :
        IsCodeOrData ? "≣" : "•";

    public string TypeLabel =>
        IsImage ? "Image preview" :
        IsAudio ? "Audio reference" :
        IsSpreadsheet ? "Spreadsheet or table" :
        IsPdf ? "PDF document" :
        IsCodeOrData ? "Document or data" : "Attached file";
}

// Exactly three image tiles are shown in the composer. The tile wrapping is
// visual only; sending still includes ALL ComposerAttachments, unmodified.
public sealed record NIRAComposerGalleryTile(NIRAComposerAttachment Attachment, bool IsOverflowTile, int HiddenCount)
{
    public bool IsImage => Attachment.IsImage;
    public string? PreviewPath => Attachment.PreviewPath;
    public string FullPath => Attachment.FullPath;
    public string FileName => Attachment.FileName;
    public string TypeGlyph => Attachment.TypeGlyph;
    public string TypeLabel => Attachment.TypeLabel;
    public string ExtensionLabel => Attachment.ExtensionLabel;
    public string OverflowLabel => IsOverflowTile ? $"+{HiddenCount} more" : string.Empty;
}

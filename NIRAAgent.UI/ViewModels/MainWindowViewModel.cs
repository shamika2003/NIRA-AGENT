/*
 * filename: MainWindowViewModel.cs
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using NIRAAgent.Mind;
using NIRAAgent.Artifacts;
using NIRAAgent.Settings;
using NIRAAgent.Voice;

namespace NIRAAgent.UI.ViewModels;

public sealed class MainWindowViewModel
    : INotifyPropertyChanged,
      IDisposable
{
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


    private async Task SendMessageAsync()
    {
        string input =
            MessageInput.Trim();


        if (string.IsNullOrWhiteSpace(
                input))
        {
            return;
        }


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


        Messages.Add(
            assistantMessage);


        try
        {
            await foreach (
                NIRAOutputChunk chunk
                in _mind.ProcessUserMessageAsync(
                    input,
                    _shutdown.Token))
            {
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


                _backgroundResponses.Remove(
                    chunk.RunId);
            }


            return;
        }


        if (
            chunk.Type !=
                NIRAOutputChunkType.Text
            ||
            string.IsNullOrEmpty(
                chunk.Content))
        {
            return;
        }


        BackgroundResponseState responseState =
            GetOrCreateBackgroundResponse(
                chunk.RunId);


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
            string.IsNullOrEmpty(
                chunk.Content))
        {
            return;
        }


        speechState.Expression =
            chunk.VoiceExpression.Normalize();


        message.Content +=
            chunk.Content;


        if (!allowSpeech)
        {
            speechChunker.Clear();
            speechState.Reset();
            return;
        }


        IReadOnlyList<string> speechParts =
            speechChunker.Add(
                chunk.Content);


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

/*
 * filename: MainWindowViewModel.cs
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using SegaAgent.Agent;
using SegaAgent.Voice;

namespace SegaAgent.UI.ViewModels;

public sealed class MainWindowViewModel
    : INotifyPropertyChanged,
      IDisposable
{
    // =========================================================
    // DEPENDENCIES
    // =========================================================

    private readonly AgentCore
        _agent;


    private readonly AgentResponseDispatcher
        _dispatcher;


    private readonly VoiceQueue
        _voiceQueue;


    // =========================================================
    // SPEECH CHUNKERS
    // =========================================================

    private readonly SpeechChunker
        _userSpeechChunker =
            new();


    private readonly SpeechChunker
        _backgroundSpeechChunker =
            new();


    // =========================================================
    // SPEECH RESPONSE STATE
    //
    // Each response gets:
    //
    // ResponseId
    // sequence number
    // captured Sega voice expression
    //
    // This prevents queued speech from looking up Sega's mood
    // again later when playback actually begins.
    // =========================================================

    private readonly SpeechResponseState
        _userSpeechState =
            new();


    private readonly SpeechResponseState
        _backgroundSpeechState =
            new();


    // =========================================================
    // COMMAND
    // =========================================================

    private readonly AsyncRelayCommand
        _sendCommand;


    // =========================================================
    // LIFETIME
    // =========================================================

    private readonly CancellationTokenSource
        _shutdown =
            new();


    private readonly Task
        _backgroundResponseTask;


    // =========================================================
    // UI STATE
    // =========================================================

    private string _messageInput =
        string.Empty;


    private bool
        _isProcessing;


    // =========================================================
    // BACKGROUND RESPONSE STATE
    // =========================================================

    private ChatMessageViewModel?
        _backgroundMessage;


    private AgentRequestSource?
        _backgroundSource;


    // =========================================================
    // MESSAGES
    // =========================================================

    public ObservableCollection<
        ChatMessageViewModel>
        Messages
    {
        get;
    } =
        new();


    // =========================================================
    // MESSAGE INPUT
    // =========================================================

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


            _sendCommand
                .RaiseCanExecuteChanged();
        }
    }


    // =========================================================
    // PROCESSING
    // =========================================================

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


            _sendCommand
                .RaiseCanExecuteChanged();
        }
    }


    // =========================================================
    // CAN SEND
    // =========================================================

    public bool CanSend =>
        !IsProcessing
        &&
        !string.IsNullOrWhiteSpace(
            MessageInput);


    // =========================================================
    // COMMAND
    // =========================================================

    public ICommand SendCommand =>
        _sendCommand;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public MainWindowViewModel(
        AgentCore agent,
        AgentResponseDispatcher dispatcher,
        VoiceQueue voiceQueue)
    {
        _agent =
            agent
            ?? throw new ArgumentNullException(
                nameof(agent));


        _dispatcher =
            dispatcher
            ?? throw new ArgumentNullException(
                nameof(dispatcher));


        _voiceQueue =
            voiceQueue
            ?? throw new ArgumentNullException(
                nameof(voiceQueue));


        _sendCommand =
            new AsyncRelayCommand(
                SendMessageAsync,
                () =>
                    CanSend);


        _backgroundResponseTask =
            ProcessBackgroundResponsesAsync();
    }


    // =========================================================
    // SEND USER MESSAGE
    // =========================================================

    private async Task SendMessageAsync()
    {
        string input =
            MessageInput.Trim();


        if (string.IsNullOrWhiteSpace(
                input))
        {
            return;
        }


        // =====================================================
        // USER GETS PRIORITY OVER CURRENT SPEECH
        // =====================================================

        _voiceQueue.Interrupt();


        // =====================================================
        // NEW USER RESPONSE
        // =====================================================

        _userSpeechChunker.Clear();


        _userSpeechState.Reset();


        // =====================================================
        // UI
        // =====================================================

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
                AgentStreamChunk chunk
                in _agent.ProcessStreamAsync(
                    input,
                    _shutdown.Token))
            {
                HandleChunk(
                    assistantMessage,
                    chunk,
                    _userSpeechChunker,
                    _userSpeechState);
            }


            FlushSpeech(
                _userSpeechChunker,
                _userSpeechState);


            if (string.IsNullOrWhiteSpace(
                    assistantMessage.Content))
            {
                assistantMessage.Content =
                    "I wasn't able to generate a response.";
            }
        }
        catch (OperationCanceledException)
        {
            _userSpeechChunker.Clear();


            _userSpeechState.Reset();


            assistantMessage.Content =
                "Request cancelled.";
        }
        catch (Exception ex)
        {
            _userSpeechChunker.Clear();


            _userSpeechState.Reset();


            assistantMessage.Content =
                $"Sorry, something went wrong.\n\n" +
                $"{ex.Message}";
        }
        finally
        {
            IsProcessing =
                false;
        }
    }


    // =========================================================
    // BACKGROUND RESPONSE LOOP
    // =========================================================

    private async Task
        ProcessBackgroundResponsesAsync()
    {
        try
        {
            await foreach (
                AgentResponse response
                in _dispatcher.ReadAllAsync(
                    _shutdown.Token))
            {
                await System.Windows
                    .Application
                    .Current
                    .Dispatcher
                    .InvokeAsync(
                        () =>
                            HandleBackgroundResponse(
                                response));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown.
        }
    }


    // =========================================================
    // BACKGROUND RESPONSE
    // =========================================================

    private void HandleBackgroundResponse(
        AgentResponse response)
    {
        // =====================================================
        // CANCELLED
        // =====================================================

        if (
            response.Chunk.Type ==
                AgentStreamChunkType.Cancelled)
        {
            _backgroundSpeechChunker
                .Clear();


            _backgroundSpeechState
                .Reset();


            if (_backgroundMessage !=
                null)
            {
                Messages.Remove(
                    _backgroundMessage);
            }


            ResetBackgroundResponse();


            return;
        }


        // =====================================================
        // COMPLETED
        // =====================================================

        if (
            response.Chunk.Type ==
                AgentStreamChunkType.Completed)
        {
            if (_backgroundMessage !=
                null)
            {
                FlushSpeech(
                    _backgroundSpeechChunker,
                    _backgroundSpeechState);
            }
            else
            {
                _backgroundSpeechChunker
                    .Clear();
            }


            _backgroundSpeechState
                .Reset();


            ResetBackgroundResponse();


            return;
        }


        // =====================================================
        // TEXT ONLY
        // =====================================================

        if (
            response.Chunk.Type !=
                AgentStreamChunkType.Text
            ||
            string.IsNullOrEmpty(
                response.Chunk.Content))
        {
            return;
        }


        // =====================================================
        // NEW BACKGROUND RESPONSE
        // =====================================================

        if (
            _backgroundMessage ==
                null
            ||
            _backgroundSource !=
                response.Source)
        {
            _backgroundSpeechChunker
                .Clear();


            _backgroundSpeechState
                .Reset();


            _backgroundSource =
                response.Source;


            _backgroundMessage =
                new ChatMessageViewModel(
                    "assistant",
                    string.Empty);


            Messages.Add(
                _backgroundMessage);
        }


        // =====================================================
        // PROCESS BACKGROUND TEXT
        // =====================================================

        HandleChunk(
            _backgroundMessage,
            response.Chunk,
            _backgroundSpeechChunker,
            _backgroundSpeechState);
    }


    // =========================================================
    // RESET BACKGROUND RESPONSE
    // =========================================================

    private void ResetBackgroundResponse()
    {
        _backgroundMessage =
            null;


        _backgroundSource =
            null;
    }


    // =========================================================
    // HANDLE STREAM CHUNK
    // =========================================================

    private void HandleChunk(
        ChatMessageViewModel message,
        AgentStreamChunk chunk,
        SpeechChunker speechChunker,
        SpeechResponseState speechState)
    {
        if (
            chunk.Type !=
                AgentStreamChunkType.Text
            ||
            string.IsNullOrEmpty(
                chunk.Content))
        {
            return;
        }


        // =====================================================
        // CAPTURE RESPONSE VOCAL EXPRESSION
        //
        // This comes from AgentCore.
        //
        // It already combines:
        //
        // persistent mood
        // relationship
        // attitude
        // situation
        // current model vocal intent
        //
        // The captured value travels with the queued speech.
        // =====================================================

        speechState.Expression =
            chunk
                .VoiceExpression
                .Normalize();


        // =====================================================
        // CHAT
        // =====================================================

        message.Content +=
            chunk.Content;


        // =====================================================
        // SPEECH CHUNKING
        // =====================================================

        IReadOnlyList<string>
            speechParts =
                speechChunker.Add(
                    chunk.Content);


        // =====================================================
        // VOICE QUEUE
        // =====================================================

        foreach (
            string speechPart
            in speechParts)
        {
            if (string.IsNullOrWhiteSpace(
                    speechPart))
            {
                continue;
            }


            VoiceUtterance utterance =
                speechState.Create(
                    speechPart);


            _voiceQueue.Enqueue(
                utterance);
        }
    }


    // =========================================================
    // FLUSH SPEECH
    // =========================================================

    private void FlushSpeech(
        SpeechChunker speechChunker,
        SpeechResponseState speechState)
    {
        string? remaining =
            speechChunker.Complete();


        if (string.IsNullOrWhiteSpace(
                remaining))
        {
            return;
        }


        VoiceUtterance utterance =
            speechState.Create(
                remaining);


        _voiceQueue.Enqueue(
            utterance);
    }


    // =========================================================
    // PROPERTY CHANGED
    // =========================================================

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


    // =========================================================
    // DISPOSE
    // =========================================================

    public void Dispose()
    {
        _shutdown.Cancel();


        _voiceQueue.Interrupt();


        _userSpeechChunker.Clear();


        _backgroundSpeechChunker.Clear();


        _userSpeechState.Reset();


        _backgroundSpeechState.Reset();


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


    // =========================================================
    // SPEECH RESPONSE STATE
    // =========================================================

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


        public SegaVoiceExpression Expression
        {
            get;
            set;
        } =
            SegaVoiceExpression.Neutral;


        // =====================================================
        // CREATE UTTERANCE
        // =====================================================

        public VoiceUtterance Create(
            string text)
        {
            ArgumentException
                .ThrowIfNullOrWhiteSpace(
                    text);


            Sequence++;


            return new VoiceUtterance(
                ResponseId,
                Sequence,
                text,
                Expression);
        }


        // =====================================================
        // RESET
        // =====================================================

        public void Reset()
        {
            ResponseId =
                Guid.NewGuid();


            Sequence =
                0;


            Expression =
                SegaVoiceExpression.Neutral;
        }
    }
}
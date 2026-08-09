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
    : INotifyPropertyChanged, IDisposable
{
    private readonly AgentCore _agent;

    private readonly AgentResponseDispatcher _dispatcher;

    private readonly VoiceQueue _voiceQueue;

    private readonly SpeechChunker _speechChunker;

    private readonly AsyncRelayCommand _sendCommand;

    private readonly CancellationTokenSource _shutdown =
        new();

    private readonly Task _backgroundResponseTask;

    private string _messageInput = string.Empty;

    private bool _isProcessing;


    // =========================================================
    // MESSAGES
    // =========================================================

    public ObservableCollection<ChatMessageViewModel> Messages { get; }
        = new();


    // =========================================================
    // MESSAGE INPUT
    // =========================================================

    public string MessageInput
    {
        get => _messageInput;

        set
        {
            if (_messageInput == value)
            {
                return;
            }

            _messageInput = value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanSend));

            _sendCommand.RaiseCanExecuteChanged();
        }
    }


    // =========================================================
    // PROCESSING
    // =========================================================

    public bool IsProcessing
    {
        get => _isProcessing;

        private set
        {
            if (_isProcessing == value)
            {
                return;
            }

            _isProcessing = value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanSend));

            _sendCommand.RaiseCanExecuteChanged();
        }
    }


    // =========================================================
    // CAN SEND
    // =========================================================

    public bool CanSend =>
        !IsProcessing &&
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
        _agent = agent;

        _dispatcher = dispatcher;

        _voiceQueue = voiceQueue;

        _speechChunker =
            new SpeechChunker();

        _sendCommand =
            new AsyncRelayCommand(
                SendMessageAsync,
                () => CanSend);

        // -----------------------------------------------------
        // Start listening for accepted background agent output.
        // -----------------------------------------------------

        _backgroundResponseTask =
            ProcessBackgroundResponsesAsync();
    }


    // =========================================================
    // USER MESSAGE
    // =========================================================

    private async Task SendMessageAsync()
    {
        var input =
            MessageInput.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        MessageInput =
            string.Empty;

        IsProcessing =
            true;

        var userMessage =
            new ChatMessageViewModel(
                "user",
                input);

        Messages.Add(
            userMessage);

        var assistantMessage =
            new ChatMessageViewModel(
                "assistant",
                string.Empty);

        Messages.Add(
            assistantMessage);

        try
        {
            await foreach (
                var chunk
                in _agent.ProcessStreamAsync(
                    input,
                    _shutdown.Token))
            {
                HandleChunk(
                    assistantMessage,
                    chunk);
            }

            FlushSpeech();

            if (string.IsNullOrWhiteSpace(
                    assistantMessage.Content))
            {
                assistantMessage.Content =
                    "I wasn't able to generate a response.";
            }
        }
        catch (OperationCanceledException)
        {
            assistantMessage.Content =
                "Request cancelled.";
        }
        catch (Exception ex)
        {
            assistantMessage.Content =
                $"Sorry, something went wrong.\n\n{ex.Message}";
        }
        finally
        {
            IsProcessing =
                false;
        }
    }


    // =========================================================
    // BACKGROUND RESPONSES
    //
    // This is the UI-side consumer.
    //
    // IMPORTANT:
    //
    // These are NOT treated as a different AI response.
    //
    // AgentCore already performed the exact same:
    //
    // Planner → Action → Responder
    //
    // pipeline.
    //
    // We only deliver the resulting stream into the UI.
    // =========================================================

    private async Task ProcessBackgroundResponsesAsync()
    {
        try
        {
            await foreach (
                var response
                in _dispatcher.ReadAllAsync(
                    _shutdown.Token))
            {
                await System.Windows.Application.Current
                    .Dispatcher
                    .InvokeAsync(
                        () =>
                        {
                            HandleBackgroundResponse(
                                response);
                        });
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }


    // =========================================================
    // BACKGROUND RESPONSE
    // =========================================================

    private ChatMessageViewModel? _backgroundMessage;

    private AgentRequestSource? _backgroundSource;


    private void HandleBackgroundResponse(
        AgentResponse response)
    {
        // -----------------------------------------------------
        // New background response.
        // -----------------------------------------------------

        if (_backgroundMessage == null ||
            _backgroundSource != response.Source)
        {
            _backgroundSource =
                response.Source;

            _backgroundMessage =
                new ChatMessageViewModel(
                    "assistant",
                    string.Empty);

            Messages.Add(
                _backgroundMessage);
        }


        HandleChunk(
            _backgroundMessage,
            response.Chunk);


        // -----------------------------------------------------
        // Response finished.
        // -----------------------------------------------------

        if (response.Chunk.Type ==
            AgentStreamChunkType.Completed)
        {
            FlushSpeech();

            _backgroundMessage =
                null;

            _backgroundSource =
                null;
        }
    }


    // =========================================================
    // HANDLE CHUNK
    // =========================================================

    private void HandleChunk(
        ChatMessageViewModel message,
        AgentStreamChunk chunk)
    {
        if (chunk.Type ==
            AgentStreamChunkType.Text)
        {
            message.Content +=
                chunk.Content;

            var speechParts =
                _speechChunker.Add(
                    chunk.Content);

            foreach (
                var speechPart
                in speechParts)
            {
                _voiceQueue.Enqueue(
                    speechPart);
            }
        }
    }


    // =========================================================
    // FLUSH SPEECH
    // =========================================================

    private void FlushSpeech()
    {
        var remaining =
            _speechChunker.Complete();

        if (!string.IsNullOrWhiteSpace(
                remaining))
        {
            _voiceQueue.Enqueue(
                remaining);
        }
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
}
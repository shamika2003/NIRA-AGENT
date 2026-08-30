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
    private readonly AgentCore _agent;

    private readonly AgentResponseDispatcher
        _dispatcher;

    private readonly VoiceQueue
        _voiceQueue;


    private readonly SpeechChunker
        _userSpeechChunker =
            new();


    private readonly SpeechChunker
        _backgroundSpeechChunker =
            new();


    private readonly AsyncRelayCommand
        _sendCommand;


    private readonly CancellationTokenSource
        _shutdown =
            new();


    private readonly Task
        _backgroundResponseTask;


    private string _messageInput =
        string.Empty;


    private bool _isProcessing;


    private ChatMessageViewModel?
        _backgroundMessage;


    private AgentRequestSource?
        _backgroundSource;


    public ObservableCollection<
        ChatMessageViewModel>
        Messages
    {
        get;
    } =
        new();


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


    public bool CanSend =>
        !IsProcessing
        &&
        !string.IsNullOrWhiteSpace(
            MessageInput);


    public ICommand SendCommand =>
        _sendCommand;


    public MainWindowViewModel(
        AgentCore agent,
        AgentResponseDispatcher dispatcher,
        VoiceQueue voiceQueue)
    {
        _agent =
            agent;


        _dispatcher =
            dispatcher;


        _voiceQueue =
            voiceQueue;


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
                    _userSpeechChunker);
            }


            FlushSpeech(
                _userSpeechChunker);


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


            assistantMessage.Content =
                "Request cancelled.";
        }
        catch (Exception ex)
        {
            _userSpeechChunker.Clear();


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
        }
    }


    private void HandleBackgroundResponse(
        AgentResponse response)
    {
        if (
            response.Chunk.Type ==
                AgentStreamChunkType.Cancelled)
        {
            _backgroundSpeechChunker
                .Clear();


            if (_backgroundMessage !=
                null)
            {
                Messages.Remove(
                    _backgroundMessage);
            }


            ResetBackgroundResponse();

            return;
        }


        if (
            response.Chunk.Type ==
                AgentStreamChunkType.Completed)
        {
            if (_backgroundMessage !=
                null)
            {
                FlushSpeech(
                    _backgroundSpeechChunker);
            }
            else
            {
                _backgroundSpeechChunker
                    .Clear();
            }


            ResetBackgroundResponse();

            return;
        }


        if (
            response.Chunk.Type !=
                AgentStreamChunkType.Text
            ||
            string.IsNullOrEmpty(
                response.Chunk.Content))
        {
            return;
        }


        if (
            _backgroundMessage ==
                null
            ||
            _backgroundSource !=
                response.Source)
        {
            _backgroundSpeechChunker
                .Clear();


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
            response.Chunk,
            _backgroundSpeechChunker);
    }


    private void ResetBackgroundResponse()
    {
        _backgroundMessage =
            null;


        _backgroundSource =
            null;
    }


    private void HandleChunk(
        ChatMessageViewModel message,
        AgentStreamChunk chunk,
        SpeechChunker speechChunker)
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


        message.Content +=
            chunk.Content;


        IReadOnlyList<string>
            speechParts =
                speechChunker.Add(
                    chunk.Content);


        foreach (
            string speechPart
            in speechParts)
        {
            _voiceQueue.Enqueue(
                speechPart);
        }
    }


    // =========================================================
    // FLUSH SPEECH
    // =========================================================

    private void FlushSpeech(
        SpeechChunker speechChunker)
    {
        string? remaining =
            speechChunker.Complete();


        if (!string.IsNullOrWhiteSpace(
                remaining))
        {
            _voiceQueue.Enqueue(
                remaining);
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
        _shutdown.Cancel();


        _voiceQueue.Interrupt();


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
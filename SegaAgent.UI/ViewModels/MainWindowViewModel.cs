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

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly AgentCore _agent;

    private readonly VoiceQueue _voiceQueue;

    private readonly SpeechChunker _speechChunker;

    private readonly AsyncRelayCommand _sendCommand;

    private string _messageInput = string.Empty;

    private bool _isProcessing;


    // ==========================================
    // MESSAGES
    // ==========================================

    public ObservableCollection<ChatMessageViewModel> Messages { get; }
        = new();


    // ==========================================
    // MESSAGE INPUT
    // ==========================================

    public string MessageInput
    {
        get => _messageInput;

        set
        {
            if (_messageInput == value)
                return;

            _messageInput = value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanSend)
            );

            _sendCommand.RaiseCanExecuteChanged();
        }
    }


    // ==========================================
    // PROCESSING
    // ==========================================

    public bool IsProcessing
    {
        get => _isProcessing;

        private set
        {
            if (_isProcessing == value)
                return;

            _isProcessing = value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanSend)
            );

            _sendCommand.RaiseCanExecuteChanged();
        }
    }


    // ==========================================
    // CAN SEND
    // ==========================================

    public bool CanSend =>
        !IsProcessing &&
        !string.IsNullOrWhiteSpace(
            MessageInput
        );


    // ==========================================
    // COMMAND
    // ==========================================

    public ICommand SendCommand =>
        _sendCommand;


    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public MainWindowViewModel(
        AgentCore agent,
        VoiceQueue voiceQueue)
    {
        _agent = agent;

        _voiceQueue = voiceQueue;

        _speechChunker =
            new SpeechChunker();

        _sendCommand =
            new AsyncRelayCommand(
                SendMessageAsync,
                () => CanSend
            );
    }


    // ==========================================
    // SEND MESSAGE
    // ==========================================

    private async Task SendMessageAsync()
    {
        var input =
            MessageInput.Trim();


        if (string.IsNullOrWhiteSpace(input))
            return;


        // ==========================================
        // CLEAR INPUT
        // ==========================================

        MessageInput =
            string.Empty;


        // ==========================================
        // START PROCESSING
        // ==========================================

        IsProcessing =
            true;


        // ==========================================
        // USER MESSAGE
        // ==========================================

        var userMessage =
            new ChatMessageViewModel(
                "user",
                input
            );


        Messages.Add(
            userMessage
        );


        // ==========================================
        // ASSISTANT PLACEHOLDER
        //
        // IMPORTANT:
        //
        // Start with empty content.
        //
        // We no longer use "Thinking..."
        // as the actual message content because
        // streamed text will be inserted here.
        // ==========================================

        var assistantMessage =
            new ChatMessageViewModel(
                "assistant",
                string.Empty
            );


        Messages.Add(
            assistantMessage
        );


        try
        {
            // ==========================================
            // STREAMING AGENT
            // ==========================================

            await foreach (
     var chunk
     in _agent.ProcessStreamAsync(
         input))
            {
                // ==========================================
                // TEXT
                // ==========================================

                if (chunk.Type ==
                    AgentStreamChunkType.Text)
                {
                    assistantMessage.Content +=
                        chunk.Content;


                    // ==========================================
                    // TTS SENTENCE CHUNKING
                    // ==========================================

                    var speechParts =
                        _speechChunker.Add(
                            chunk.Content
                        );


                    foreach (var speechPart in speechParts)
                    {
                        _voiceQueue.Enqueue(
                            speechPart
                        );
                    }
                }


                // ==========================================
                // COMPLETED
                // ==========================================

                if (chunk.Type ==
                    AgentStreamChunkType.Completed)
                {
                    // Nothing required here.
                }
            }


            // ==========================================
            // FLUSH REMAINING SPEECH
            // ==========================================

            var remainingSpeech =
                _speechChunker.Complete();

            if (!string.IsNullOrWhiteSpace(
                    remainingSpeech))
            {
                _voiceQueue.Enqueue(
                    remainingSpeech
                );
            }


            // ==========================================
            // EMPTY RESPONSE SAFETY
            // ==========================================

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
            // ==========================================
            // FINISHED
            // ==========================================

            IsProcessing =
                false;
        }
    }


    // ==========================================
    // PROPERTY CHANGED
    // ==========================================

    public event PropertyChangedEventHandler?
        PropertyChanged;


    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName
            )
        );
    }
}
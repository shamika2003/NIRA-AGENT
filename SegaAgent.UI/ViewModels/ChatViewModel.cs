/*
 * filename: ChatViewModel.cs
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SegaAgent.Agent;
using SegaAgent.UI.Models;

namespace SegaAgent.UI.ViewModels;

public sealed class ChatViewModel : INotifyPropertyChanged
{
    private readonly AgentCore _agent;

    private bool _isProcessing;


    public ObservableCollection<ChatMessage> Messages { get; } = new();


    public bool IsProcessing
    {
        get => _isProcessing;

        private set
        {
            if (_isProcessing == value)
                return;

            _isProcessing = value;

            OnPropertyChanged();
        }
    }


    public ChatViewModel(
        AgentCore agent)
    {
        _agent = agent;
    }


    public async Task SendMessageAsync(
        string userInput,
        CancellationToken cancellationToken = default)
    {
        if (IsProcessing)
            return;


        if (string.IsNullOrWhiteSpace(userInput))
            return;


        userInput =
            userInput.Trim();


        IsProcessing = true;


        try
        {
            // USER MESSAGE
            Messages.Add(
                new ChatMessage(
                    "user",
                    userInput
                )
            );


            // Create empty assistant message.
            var assistantMessage =
                new ChatMessage(
                    "assistant",
                    ""
                );


            Messages.Add(
                assistantMessage
            );


            // STREAM RESPONSE
            await foreach (
                var chunk
                in _agent.ProcessStreamAsync(
                    userInput,
                    cancellationToken))
            {

                if (chunk.Type ==
                    AgentStreamChunkType.Text)
                {
                    assistantMessage.Content +=
                        chunk.Content;
                }


                if (chunk.Type ==
                    AgentStreamChunkType.Completed)
                {
                    break;
                }


                // Notify UI refresh.
                OnPropertyChanged(
                    nameof(Messages)
                );
            }
        }


        catch (OperationCanceledException)
        {
            // User cancelled.
        }


        catch (Exception ex)
        {
            Messages.Add(
                new ChatMessage(
                    "assistant",
                    $"Sorry, something went wrong.\n\n{ex.Message}"
                )
                {
                    IsError = true
                }
            );
        }


        finally
        {
            IsProcessing = false;
        }
    }



    public event PropertyChangedEventHandler? PropertyChanged;


    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName)
        );
    }
}
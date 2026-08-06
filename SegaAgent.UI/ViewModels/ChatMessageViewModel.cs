/*
 * filename: ChatMessageViewModel.cs
 */


using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SegaAgent.UI.ViewModels;

public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private string _content;

    public string Role { get; }

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value)
                return;

            _content = value;
            OnPropertyChanged();
        }
    }

    public bool IsUser => Role == "user";

    public bool IsAssistant => Role == "assistant";

    public ChatMessageViewModel(
        string role,
        string content)
    {
        Role = role;
        _content = content;
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
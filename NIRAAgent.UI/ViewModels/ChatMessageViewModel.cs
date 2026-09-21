/*
 * filename: ChatMessageViewModel.cs
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using NIRAAgent.Artifacts;

namespace NIRAAgent.UI.ViewModels;

public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private string
        _content;


    public Guid? RunId
    {
        get;
        private set;
    }


    public string Role
    {
        get;
    }


    public string Content
    {
        get =>
            _content;

        set
        {
            if (_content ==
                value)
            {
                return;
            }

            _content =
                value;

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(HasContent));
            OnPropertyChanged(
                nameof(IsEmpty));
        }
    }


    public ObservableCollection<VisualArtifactViewModel> VisualArtifacts
    {
        get;
    } =
        new();


    public bool IsUser =>
        Role ==
        "user";


    public bool IsAssistant =>
        Role ==
        "assistant";


    public bool HasContent =>
        !string.IsNullOrWhiteSpace(
            Content);


    public bool HasVisualArtifacts =>
        VisualArtifacts.Count >
        0;


    public bool IsEmpty =>
        !HasContent
        &&
        !HasVisualArtifacts;


    public ChatMessageViewModel(
        string role,
        string content)
    {
        Role =
            role;

        _content =
            content;

        VisualArtifacts.CollectionChanged +=
            (_, _) =>
            {
                OnPropertyChanged(
                    nameof(HasVisualArtifacts));
                OnPropertyChanged(
                    nameof(IsEmpty));
            };
    }


    public void AssociateRun(
        Guid runId)
    {
        if (runId ==
            Guid.Empty)
        {
            return;
        }

        if (RunId ==
            runId)
        {
            return;
        }

        RunId =
            runId;

        OnPropertyChanged(
            nameof(RunId));
    }


    public VisualArtifactViewModel AddVisualArtifact(
        NIRAVisualArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(
            artifact);

        VisualArtifactViewModel visual =
            new(artifact);

        VisualArtifacts.Add(
            visual);

        return visual;
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
}


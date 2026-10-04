/*
 * filename: ChatMessageViewModel.cs
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using NIRAAgent.Artifacts;
using NIRAAgent.Presentation;
using NIRAAgent.UI.Presentation;
using NIRAAgent.Conversation;
using System.Diagnostics;
using System.Windows;

namespace NIRAAgent.UI.ViewModels;

public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private string
        _content;

    private string _progressText = string.Empty;
    private bool _isJourneyActive;
    private bool _isJourneyFading;
    private int _journeyVersion;

    public string ProgressText
    {
        get => _progressText;
        set
        {
            string normalized = value ?? string.Empty;
            if (_progressText == normalized) return;

            _progressText = normalized;

            // Only real runtime/model progress text appears here.
            // A new update also keeps the current journey indicator alive.
            if (!string.IsNullOrWhiteSpace(_progressText))
            {
                _journeyVersion++;
                IsJourneyActive = true;
                IsJourneyFading = false;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProgress));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool HasProgress =>
        !string.IsNullOrWhiteSpace(ProgressText);

    public bool IsJourneyActive
    {
        get => _isJourneyActive;
        private set
        {
            if (_isJourneyActive == value) return;
            _isJourneyActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsJourneyFading
    {
        get => _isJourneyFading;
        private set
        {
            if (_isJourneyFading == value) return;
            _isJourneyFading = value;
            OnPropertyChanged();
        }
    }

    // Start with ONLY the living three dots. No fake/default status text.
    public void BeginJourney()
    {
        _journeyVersion++;
        _progressText = string.Empty;
        IsJourneyFading = false;
        IsJourneyActive = true;

        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(IsEmpty));
    }

    // Final content can render immediately while the dots/current real
    // progress line quietly fade out.
    public void BeginJourneyFadeOut()
    {
        if (!IsJourneyActive || IsJourneyFading)
            return;

        int version = ++_journeyVersion;
        IsJourneyFading = true;
        _ = CompleteJourneyFadeOutAsync(version);
    }

    public void ClearJourneyImmediately()
    {
        _journeyVersion++;
        _progressText = string.Empty;
        IsJourneyFading = false;
        IsJourneyActive = false;

        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task CompleteJourneyFadeOutAsync(int version)
    {
        await Task.Delay(190);

        if (version != _journeyVersion)
            return;

        _progressText = string.Empty;
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(HasProgress));

        IsJourneyActive = false;
        IsJourneyFading = false;
        OnPropertyChanged(nameof(IsEmpty));
    }

    // Compatibility with the V4 call sites.
    public void BeginProgressFadeOut() =>
        BeginJourneyFadeOut();

    public void ClearProgressImmediately() =>
        ClearJourneyImmediately();


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


    public ObservableCollection<FrameworkElement> RichElements { get; } = new();
    public bool HasRichElements => RichElements.Count > 0;

    public void AddRichBlocks(IReadOnlyList<NIRARichBlock> blocks,
        NIRAConversationArchiveStore? archive = null, Guid? messageId = null,
        Action<string>? followUp = null)
    {
        var normalized = NIRAPresentationPolicy.Normalize(blocks);
        IReadOnlyDictionary<int, NIRARichInteractionState> states =
            new Dictionary<int, NIRARichInteractionState>();
        if (archive is { Enabled: true } && messageId is Guid id)
        {
            try { states = archive.ReadInteractiveStates(id); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RichInteraction] STATE_READ_FAILED | {ex.GetType().Name}");
            }
        }
        for (int index = 0; index < normalized.Count;)
        {
            if (normalized[index].Type is "card" or "metric")
            {
                string type = normalized[index].Type;
                var group = new List<NIRARichBlock>();
                while (index < normalized.Count && normalized[index].Type == type)
                    group.Add(normalized[index++]);
                RichElements.Add(type == "card"
                    ? NIRARichBlockRenderer.RenderCardGroup(group)
                    : NIRARichBlockRenderer.RenderMetricGroup(group));
            }
            else
            {
                int blockIndex = index;
                var block = normalized[index++];
                states.TryGetValue(blockIndex, out var saved);
                Action<NIRARichInteractionState>? persist = null;
                if (archive is { Enabled: true } && messageId is Guid stateMessageId)
                    persist = state =>
                    {
                        try { archive.SaveInteractiveState(stateMessageId, blockIndex, state); }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[RichInteraction] STATE_SAVE_FAILED | {ex.GetType().Name}");
                        }
                    };
                RichElements.Add(NIRARichBlockRenderer.Render(block, saved, persist, followUp));
            }
        }
    }

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
        !HasVisualArtifacts
        &&
        !HasRichElements
        &&
        !IsJourneyActive;


    public ChatMessageViewModel(
        string role,
        string content)
    {
        Role =
            role;

        _content =
            content;

        RichElements.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRichElements));
            OnPropertyChanged(nameof(IsEmpty));
        };

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

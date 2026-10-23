/*
 * filename: ChatMessageViewModel.cs
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using NIRAAgent.Artifacts;
using NIRAAgent.Presentation;
using NIRAAgent.UI.Presentation;
using NIRAAgent.Conversation;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace NIRAAgent.UI.ViewModels;

public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private string
        _content;

    private string _progressText = string.Empty;
    private bool _isImageGenerationActive;
    public bool IsImageGenerationActive
    {
        get => _isImageGenerationActive;
        set { if (_isImageGenerationActive == value) return; _isImageGenerationActive = value; OnPropertyChanged(); }
    }
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
        IsImageGenerationActive = false;
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

        IsImageGenerationActive = false;
        int version = ++_journeyVersion;
        IsJourneyFading = true;
        _ = CompleteJourneyFadeOutAsync(version);
    }

    public void ClearJourneyImmediately()
    {
        IsImageGenerationActive = false;
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

    private string _replyToText = string.Empty;
    public string ReplyToText => _replyToText;
    public bool HasReplySource => !string.IsNullOrWhiteSpace(_replyToText);
    public void SetReplySource(string? request, bool background = false)
    {
        if (string.IsNullOrWhiteSpace(request)) return;
        string text = request.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length > 120) text = text[..120].TrimEnd() + "…";
        string label = (background ? "Background work · " : "Reply to · ") + text;
        if (label == _replyToText) return;
        _replyToText = label;
        OnPropertyChanged(nameof(ReplyToText));
        OnPropertyChanged(nameof(HasReplySource));
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


    // User-chosen attachments are a visual snapshot of the sent turn, not
    // model output or a new capability. Images use the same safe on-load,
    // bounded bitmap preview as existing trusted visual-artifact cards.
    public ObservableCollection<VisualArtifactViewModel> UserImageAttachments { get; } = new();
    public ObservableCollection<NIRAUserImageGalleryTileViewModel> UserImageGalleryTiles { get; } = new();
    public ObservableCollection<NIRAUserImageGalleryTileViewModel> AssistantImageGalleryTiles { get; } = new();
    public ObservableCollection<NIRAComposerAttachment> UserFileAttachments { get; } = new();
    public bool HasUserImages => UserImageAttachments.Count > 0;
    public bool HasSingleUserImage => UserImageAttachments.Count == 1;
    public bool HasUserFiles => UserFileAttachments.Count > 0;
    public int UserImageCount => UserImageAttachments.Count;
    public bool HasOverflowUserImages => UserImageAttachments.Count > 2;
    public int OverflowUserImageCount => Math.Max(0, UserImageAttachments.Count - 2);

    public void AddUserAttachments(IEnumerable<NIRAComposerAttachment> attachments)
    {
        foreach (NIRAComposerAttachment attachment in attachments)
        {
            if (attachment.IsImage)
            {
                UserImageAttachments.Add(new VisualArtifactViewModel(new NIRAVisualArtifact
                {
                    ArtifactId = Guid.NewGuid(),
                    SourceKind = NIRAVisualArtifactSourceKind.LocalImage,
                    LocalPath = attachment.FullPath,
                    Title = attachment.FileName,
                    Caption = string.Empty
                }));
            }
            else
            {
                UserFileAttachments.Add(attachment);
            }
        }

        RebuildUserImageGalleryTiles();
    }

    public IReadOnlyList<VisualArtifactViewModel> GetUserImageArtifacts() =>
        UserImageAttachments.ToArray();

    public IReadOnlyList<VisualArtifactViewModel> GetAssistantImageArtifacts() =>
        VisualArtifacts.ToArray();

    // Both directions share one compact, bounded, one-row gallery contract.
    // Two actual previews and an overflow tile keep a large attachment set
    // from growing the chat into a very tall column. The gallery viewer
    // still receives ALL original artifacts in their original order.
    private void RebuildUserImageGalleryTiles()
    {
        BuildGalleryTiles(UserImageGalleryTiles, UserImageAttachments, assistant: false);
        OnPropertyChanged(nameof(UserImageCount));
        OnPropertyChanged(nameof(HasOverflowUserImages));
        OnPropertyChanged(nameof(OverflowUserImageCount));
    }

    private void RebuildAssistantImageGalleryTiles() =>
        BuildGalleryTiles(AssistantImageGalleryTiles, VisualArtifacts, assistant: true);

    private void BuildGalleryTiles(
        ObservableCollection<NIRAUserImageGalleryTileViewModel> target,
        IReadOnlyList<VisualArtifactViewModel> images,
        bool assistant)
    {
        target.Clear();
        int count = images.Count;
        if (count == 0) return;
        int visible = Math.Min(count, 3);
        for (int index = 0; index < visible; index++)
        {
            bool overflow = count > 3 && index == 2;
            target.Add(new NIRAUserImageGalleryTileViewModel(
                this, images[index], index,
                hiddenCount: overflow ? count - 2 : 0,
                isOverflowTile: overflow,
                isAssistantTile: assistant,
                isSingleImage: count == 1));
        }
    }

    // The prose document may find the same video as a structured block.
    // These keys suppress duplicate embedded players, never the explanatory text.
    public string EmbeddedVideoKeys { get; private set; } = string.Empty;
    public ObservableCollection<FrameworkElement> RichElements { get; } = new();
    private readonly List<(FrameworkElement View, NIRARichBlock Block)> _photoCreditFrames = new();
    public bool HasRichElements => RichElements.Count > 0;
    private bool HasRetrievedCreditedImage => VisualArtifacts.Any(image =>
        image.HasStructuredCredit && image.HasSourceUri);

    private void SuppressDuplicatePhotoCreditFrames()
    {
        if (!HasRetrievedCreditedImage) return;
        foreach (var entry in _photoCreditFrames.ToArray())
        {
            if (!NIRAImageCreditLayoutPolicy.IsDuplicatePhotoCredit(entry.Block, true)) continue;
            RichElements.Remove(entry.View);
            _photoCreditFrames.Remove(entry);
        }
    }

    public void AddRichBlocks(IReadOnlyList<NIRARichBlock> blocks,
        NIRAConversationArchiveStore? archive = null, Guid? messageId = null,
        Action<string>? followUp = null)
    {
        var normalized = NIRAPresentationPolicy.Normalize(blocks);
        string keys = string.Join("|", normalized.Where(b => b.Type == "video")
            .Select(b => NIRAVideoReferencePolicy.TryParse(b.Text, out var video)
                ? video?.Key : null)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        if (keys.Length > 0 && keys != EmbeddedVideoKeys)
        {
            EmbeddedVideoKeys = keys;
            OnPropertyChanged(nameof(EmbeddedVideoKeys));
        }
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
                {
                    // Credit-only cards must remain independent of other cards,
                    // so a later trusted image can suppress only the duplicate.
                    bool isCredit = NIRAImageCreditLayoutPolicy.IsDuplicatePhotoCredit(normalized[index], true);
                    if (isCredit && group.Count > 0) break;
                    group.Add(normalized[index++]);
                    if (isCredit) break;
                }
                if (group.Count == 1 && NIRAImageCreditLayoutPolicy.IsDuplicatePhotoCredit(
                        group[0], HasRetrievedCreditedImage)) continue;
                FrameworkElement rendered = type == "card"
                    ? NIRARichBlockRenderer.RenderCardGroup(group)
                    : NIRARichBlockRenderer.RenderMetricGroup(group);
                RichElements.Add(rendered);
                if (group.Count == 1 && NIRAImageCreditLayoutPolicy.IsDuplicatePhotoCredit(group[0], true))
                    _photoCreditFrames.Add((rendered, group[0]));
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
                if (NIRAImageCreditLayoutPolicy.IsDuplicatePhotoCredit(block, HasRetrievedCreditedImage))
                    continue;
                FrameworkElement rendered = NIRARichBlockRenderer.Render(block, saved, persist, followUp);
                RichElements.Add(rendered);
                if (NIRAImageCreditLayoutPolicy.IsDuplicatePhotoCredit(block, true))
                    _photoCreditFrames.Add((rendered, block));
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
        !HasUserImages
        &&
        !HasUserFiles
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

        UserImageAttachments.CollectionChanged += (_, _) =>
        {
            RebuildUserImageGalleryTiles();
            OnPropertyChanged(nameof(HasUserImages));
            OnPropertyChanged(nameof(HasSingleUserImage));
            OnPropertyChanged(nameof(IsEmpty));
        };
        UserFileAttachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasUserFiles));
            OnPropertyChanged(nameof(IsEmpty));
        };

        RichElements.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRichElements));
            OnPropertyChanged(nameof(IsEmpty));
        };

        VisualArtifacts.CollectionChanged +=
            (_, _) =>
            {
                RebuildAssistantImageGalleryTiles();
                SuppressDuplicatePhotoCreditFrames();
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

public sealed class NIRAUserImageGalleryTileViewModel
{
    public NIRAUserImageGalleryTileViewModel(
        ChatMessageViewModel owner,
        VisualArtifactViewModel artifact,
        int sourceIndex,
        int hiddenCount = 0,
        bool isOverflowTile = false,
        bool isAssistantTile = false,
        bool isSingleImage = false)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        SourceIndex = sourceIndex;
        HiddenCount = Math.Max(0, hiddenCount);
        IsOverflowTile = isOverflowTile;
        IsAssistantTile = isAssistantTile;
        IsSingleImage = isSingleImage;
    }

    public ChatMessageViewModel Owner { get; }
    public VisualArtifactViewModel Artifact { get; }
    public int SourceIndex { get; }
    public int HiddenCount { get; }
    public bool IsOverflowTile { get; }
    public bool IsAssistantTile { get; }
    public bool IsSingleImage { get; }
    public string Title => Artifact.Title;
    public string Caption => Artifact.Caption;
    public bool HasCaption => Artifact.HasCaption;
    public string SourceUri => Artifact.SourceUri;
    public bool HasSourceUri => Artifact.HasSourceUri;
    public bool HasStructuredCredit => Artifact.HasStructuredCredit;
    public string CreditProvider => Artifact.CreditProvider;
    public string CreditCreator => Artifact.CreditCreator;
    public bool HasCreditCreator => Artifact.HasCreditCreator;
    public string CreditLicense => Artifact.CreditLicense;
    public bool HasCreditLicense => Artifact.HasCreditLicense;
    public string SourceActionLabel => Artifact.SourceActionLabel;
    public string ReferenceCaption => Artifact.ReferenceCaption;
    public bool HasReferenceCaption => Artifact.HasReferenceCaption;
    public bool IsGeneratedImage => Artifact.IsGeneratedImage;
    public string CardBadgeText => Artifact.CardBadgeText;
    public string GeneratedMetaText => Artifact.GeneratedMetaText;
    public IReadOnlyList<VisualArtifactViewModel> GetGalleryImages() =>
        IsAssistantTile ? Owner.GetAssistantImageArtifacts() : Owner.GetUserImageArtifacts();
    public ImageSource? PreviewImage => Artifact.PreviewImage;
    public bool HasPreview => Artifact.HasPreview;
    public string OverflowLabel => HiddenCount > 0 ? $"+{HiddenCount}" : string.Empty;
}

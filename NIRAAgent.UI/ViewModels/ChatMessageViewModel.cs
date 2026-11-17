/*
 * filename: ChatMessageViewModel.cs
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using NIRAAgent.Artifacts;
using NIRAAgent.Presentation;
using NIRAAgent.UI.Presentation;
using NIRAAgent.Conversation;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NIRAAgent.UI.ViewModels;

public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private string
        _content;

    // Live, read-only task activity. These entries are based ONLY on host
    // events already persisted by AgentLoopEngine. They are not model thoughts.
    public ObservableCollection<NIRAActivityTimelineEntry> ActivityEntries { get; } = new();
    private readonly DispatcherTimer _activityClock = new() {
        Interval = TimeSpan.FromSeconds(1)
    };
    private DateTimeOffset _activityStartedUtc;
    private DateTimeOffset? _activityFinishedUtc;
    private bool _hasActivity;
    private bool _isActivityExpanded;
    private bool _activityFailed;
    private string _activityLiveLabel = "Thinking";
    private string _lastActivityToolId = string.Empty;

    public bool HasActivity
    {
        get => _hasActivity;
        private set
        {
            if (_hasActivity == value) return;
            _hasActivity = value;
            OnPropertyChanged();
        }
    }

    public bool IsActivityExpanded
    {
        get => _isActivityExpanded;
        set
        {
            if (_isActivityExpanded == value) return;
            _isActivityExpanded = value;
            OnPropertyChanged();
        }
    }

    public string ActivityHeader
    {
        get
        {
            if (_activityStartedUtc == default) return "Activity";
            var duration = (_activityFinishedUtc ?? DateTimeOffset.UtcNow) - _activityStartedUtc;
            int seconds = Math.Max(0, (int)duration.TotalSeconds);
            if (_activityFinishedUtc is null) return $"{_activityLiveLabel} · {seconds}s";
            if (_activityFailed) return $"Stopped after {seconds}s";
            return $"Worked for {seconds}s";
        }
    }

    public void AddAgentActivity(string? code, string? toolId, DateTimeOffset atUtc)
    {
        if (!NIRAActivityPresentationPolicy.IsKnown(code) || _activityFinishedUtc is not null)
            return;
        if (_activityStartedUtc == default)
            _activityStartedUtc = atUtc == default ? DateTimeOffset.UtcNow : atUtc;
        if (!HasActivity)
        {
            HasActivity = true;
            IsActivityExpanded = true;
            if (!_activityClock.IsEnabled) _activityClock.Start();
        }
        string id = toolId ?? string.Empty;
        switch (code)
        {
            case "RUN_ACTIVE":
                // RUN_ACTIVE is a persisted lifecycle transition, not a model step.
                break;
            case "REASONING_STARTED":
                _activityLiveLabel = "Thinking";
                AddActivityEntry(ActivityEntries.Count == 0
                    ? "Considering the request" : "Deciding what to do next",
                    "Model decision in progress", "○");
                break;
            case "TOOL_PROPOSED":
                // Tool selection is a model decision, NOT execution.
                for (int i = ActivityEntries.Count - 1; i >= 0; i--)
                {
                    NIRAActivityTimelineEntry item = ActivityEntries[i];
                    if (!item.IsTool && !item.IsFinal && item.Glyph == "○")
                    {
                        item.Complete("Selected a capability", true);
                        break;
                    }
                }
                break;
            case "TOOL_DISPATCHED":
                _activityLiveLabel = "Working";
                _lastActivityToolId = id;
                AddActivityEntry(NIRAActivityPresentationPolicy.ToolTitle(id),
                    "Capability dispatched", "○");
                break;
            case "TOOL_SUCCEEDED":
            case "TOOL_FAILED":
            case "TOOL_CONFIRMED_NO_EFFECT":
                // A failed observation may be recovered on a later model round;
                // only terminal run status determines the overall outcome.
                _activityLiveLabel = "Thinking";
                CompleteLatestToolActivity(id,
                    NIRAActivityPresentationPolicy.ResultLabel(code),
                    code == "TOOL_SUCCEEDED");
                break;
            case "AUTHORIZATION_REQUIRED":
            case "RECONCILIATION_REQUIRED":
                _activityFailed = true;
                AddActivityEntry(NIRAActivityPresentationPolicy.ResultLabel(code),
                    "No further action was automatically executed", "!");
                break;
            case "RUN_COMPLETED":
                FinishAgentActivity(true);
                break;
            case "RUN_CANCELLED":
            case "RUN_INCOMPLETE":
                _activityFailed = true;
                AddActivityEntry(NIRAActivityPresentationPolicy.ResultLabel(code),
                    "Task ended without verified completion", "!");
                FinishAgentActivity(false);
                break;
        }
        OnPropertyChanged(nameof(ActivityHeader));
    }

    private void AddActivityEntry(string title, string detail, string glyph)
    {
        if (ActivityEntries.Count >= 28) return;
        ActivityEntries.Add(new NIRAActivityTimelineEntry(title, detail, glyph,
            isTool: detail == "Capability dispatched",
            toolIdentity: detail == "Capability dispatched" ? _lastActivityToolId : ""));
    }

    private void CompleteLatestToolActivity(string id, string result, bool succeeded)
    {
        for (int i = ActivityEntries.Count - 1; i >= 0; i--)
        {
            NIRAActivityTimelineEntry item = ActivityEntries[i];
            if (item.IsTool && !item.IsFinal &&
                string.Equals(item.ToolIdentity, id, StringComparison.Ordinal))
            {
                item.Complete(result, succeeded);
                return;
            }
        }
        // If an interrupted/restarted run has no matching dispatch in this
        // current UI session, state only the outcome, not an invented action.
        AddActivityEntry("Capability result", result, succeeded ? "✓" : "!");
    }

    public void FinishAgentActivity(bool completed)
    {
        _activityClock.Stop();
        if (!HasActivity || _activityFinishedUtc is not null) return;
        _activityFinishedUtc = DateTimeOffset.UtcNow;
        if (!completed) _activityFailed = true;
        foreach (var entry in ActivityEntries)
            if (entry.Glyph == "○") entry.Complete(
                completed ? "Decision returned" : "Interrupted before confirmation", completed);
        IsActivityExpanded = false;
        OnPropertyChanged(nameof(ActivityHeader));
    }

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
        _activityClock.Stop();
        ActivityEntries.Clear();
        _activityStartedUtc = DateTimeOffset.UtcNow;
        _activityFinishedUtc = null;
        _activityFailed = false;
        _activityLiveLabel = "Thinking";
        _lastActivityToolId = string.Empty;
        HasActivity = false;
        IsActivityExpanded = false;
        OnPropertyChanged(nameof(ActivityHeader));
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
            // One media deck per reply, regardless of whether the evidence
            // arrived as prose URLs or typed video blocks.
            SyncVideoDeckFromProse();
            if (VisualArtifacts.Count > 0) RefreshRichPhotoPlacement();

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(HasContent));
            OnPropertyChanged(nameof(HasInlineVisuals));
            OnPropertyChanged(nameof(InlineVisualRevision));
            OnPropertyChanged(
                nameof(IsEmpty));
        }
    }


    // Only verified source-backed images enter the paragraph-aware document.
    // Nonretrieved and generated assets keep their established gallery/viewer.
    public bool HasInlineVisuals =>
        (Content.Length >= 100 || VisualArtifacts.Count >= 4 ||
         _richContentFrames.Any(frame => frame.Blocks.Any(block =>
             !string.IsNullOrWhiteSpace(block.ImageSubject)))) &&
        VisualArtifacts.Count is > 0 and <= 30 &&
        VisualArtifacts.All(v => v.HasPreview && v.HasSourceUri && !v.IsGeneratedImage);
    public int InlineVisualRevision => VisualArtifacts.Count;

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
                this, images[index], assistant ? VisualArtifacts.IndexOf(images[index]) : index,
                hiddenCount: overflow ? count - 2 : 0,
                isOverflowTile: overflow,
                isAssistantTile: assistant,
                isSingleImage: count == 1));
        }
    }

    // The prose document may find the same video as a structured block.
    // These keys suppress duplicate embedded players, never the explanatory text.
    public string EmbeddedVideoKeys { get; private set; } = string.Empty;
    // Keep typed video refs unique even when rich blocks arrive in batches.
    private readonly HashSet<string> _renderedRichVideoKeys =
        new(StringComparer.OrdinalIgnoreCase);
    // Persists across structured rich-block batches for the SAME reply.
    private NIRAVideoDepthDeck? _videoDepthDeck;

    // Use the same strict HTTPS/provider/ID policy as typed video blocks.
    // A public response can contain ordinary Markdown references and native
    // video blocks in separate batches. They must share ONE player/deck.
    // The earlier independent prose renderer kept creating a single player,
    // even when more suitable clips were present elsewhere in the reply.
    private static readonly Regex AnswerVideoUrls = new(
        @"https://[^\s<>\]\)""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private void AddToVideoDeck(NIRARichBlock block, NIRAVideoReference reference)
    {
        if (!_renderedRichVideoKeys.Add(reference.Key)) return;
        _videoDepthDeck ??= new NIRAVideoDepthDeck();
        if (_videoDepthDeck.TryAdd(block)) KeepVideoDeckLast();
    }

    private void KeepVideoDeckLast()
    {
        if (_videoDepthDeck is not { Count: > 0 }) return;
        FrameworkElement view = _videoDepthDeck.View;
        int existing = RichElements.IndexOf(view);
        if (existing == RichElements.Count - 1) return;
        if (existing >= 0) RichElements.RemoveAt(existing);
        RichElements.Add(view);
    }

    private void SyncVideoDeckFromProse()
    {
        if (!IsAssistant || string.IsNullOrWhiteSpace(_content)) return;
        // Avoid excessive scanning on large reports and prevent unbounded
        // embedded media surfaces. The deck can still receive typed blocks.
        string answer = _content.Length > 50000 ? _content[..50000] : _content;
        var keys = new HashSet<string>(
            EmbeddedVideoKeys.Split('|', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
        int processed = 0;
        foreach (Match match in AnswerVideoUrls.Matches(answer))
        {
            string url = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '*');
            if (!NIRAVideoReferencePolicy.TryParse(url, out var video) || video is null)
                continue;
            if (!keys.Add(video.Key)) continue;

            string title = "Watch " + video.Provider + " video";
            // [Actual descriptive label](https://...) is a display label;
            // content/identity is never inferred from it or from a thumbnail.
            int labelEnd = match.Index - 2;
            if (labelEnd >= 0 && answer.Substring(labelEnd, 2) == "](")
            {
                int labelStart = answer.LastIndexOf('[', labelEnd);
                if (labelStart >= 0 && labelEnd - labelStart is > 1 and < 125)
                {
                    string candidate = answer[(labelStart + 1)..labelEnd]
                        .Trim('*', '_', ' ', '\t');
                    if (candidate.Length is > 0 and <= 110) title = candidate;
                }
            }
            AddToVideoDeck(new NIRARichBlock
            {
                Type = "video", Title = title, Text = video.OriginalUrl
            }, video);
            if (++processed >= 8) break;
        }
        string updated = string.Join("|", keys.OrderBy(k => k,
            StringComparer.OrdinalIgnoreCase));
        if (updated != EmbeddedVideoKeys)
        {
            EmbeddedVideoKeys = updated;
            OnPropertyChanged(nameof(EmbeddedVideoKeys));
        }
    }

    public ObservableCollection<FrameworkElement> RichElements { get; } = new();
    private readonly List<(FrameworkElement View, NIRARichBlock Block)> _photoCreditFrames = new();
    // Real rich-block views provide grounded target sections for verified photos.
    // Track content frames separately from media frames so refreshes are idempotent.
    private readonly List<(FrameworkElement View, IReadOnlyList<NIRARichBlock> Blocks)> _richContentFrames = new();
    private readonly List<FrameworkElement> _richImageFrames = new();
    private readonly HashSet<Guid> _richPlacedPhotoIds = new();
    // Only unplaced photos go into the fallback row. Never hide a valid
    // image merely because it was eligible for inline rendering.
    public ObservableCollection<NIRAUserImageGalleryTileViewModel> UnplacedAssistantImageGalleryTiles { get; } = new();
    public bool HasUnplacedAssistantImages => UnplacedAssistantImageGalleryTiles.Count > 0;

    private void RebuildUnplacedAssistantImages()
    {
        bool imageOnlyInlineGallery = HasInlineVisuals && Content.Length < 100 &&
            VisualArtifacts.Count >= 4;
        var unplaced = VisualArtifacts.Where(photo =>
            !imageOnlyInlineGallery && (!HasInlineVisuals ||
            !(photo.HasPreview && photo.HasSourceUri && !photo.IsGeneratedImage) ||
            (NIRAInlineImageAnchorPolicy.FindLine(
                 NIRAResearchPresentationPolicy.StripEditorialImagePlacement(Content),
                 string.IsNullOrWhiteSpace(photo.SubjectQuery) ? photo.Title : photo.SubjectQuery) < 0 &&
             !_richPlacedPhotoIds.Contains(photo.ArtifactId)))).ToArray();
        BuildGalleryTiles(UnplacedAssistantImageGalleryTiles, unplaced, assistant: true);
        OnPropertyChanged(nameof(HasUnplacedAssistantImages));
    }

    private void RefreshRichPhotoPlacement()
    {
        foreach (var previous in _richImageFrames) RichElements.Remove(previous);
        _richImageFrames.Clear();
        _richPlacedPhotoIds.Clear();
        if (_richContentFrames.Count == 0 || !HasInlineVisuals)
        {
            RebuildUnplacedAssistantImages();
            return;
        }
        var photos = VisualArtifacts.Where(p => p.HasPreview && p.HasSourceUri && !p.IsGeneratedImage)
            .Take(30).ToArray();
        if (photos.Length == 0)
        {
            RebuildUnplacedAssistantImages();
            return;
        }

        // A photo paired to several tabbed sections is more useful when the
        // sections become separate readable cards. That lets each verified
        // image sit under its own explanation instead of collecting at the end.
        for (int g = 0; g < _richContentFrames.Count; g++)
        {
            var group = _richContentFrames[g];
            if (group.Blocks.Count != 1 || group.Blocks[0].Type != "tabs")
                continue;
            var tabs = group.Blocks[0];
            var matchedTabIndices = photos.Select(photo => tabs.Items
                .Select((item, index) => new
                {
                    Index = index,
                    Score = NIRAInlineImageAnchorPolicy.MatchSection(
                        string.IsNullOrWhiteSpace(photo.SubjectQuery) ? photo.Title : photo.SubjectQuery,
                        item, null,
                        index < tabs.Panels.Count ? new[] { tabs.Panels[index] } : null,
                        index < tabs.Panels.Count ? tabs.Panels[index] : string.Empty)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault())
                .Where(x => x != null)
                .Select(x => x!.Index)
                .Distinct()
                .ToArray();
            if (matchedTabIndices.Length < 2) continue;
            int startIndex = RichElements.IndexOf(group.View);
            if (startIndex < 0) continue;
            RichElements.RemoveAt(startIndex);
            _richContentFrames.RemoveAt(g);
            int offset = 0;
            for (int index = 0; index < tabs.Items.Count && index < tabs.Panels.Count; index++)
            {
                var card = new NIRARichBlock
                {
                    Type = "card",
                    Title = tabs.Items[index],
                    Text = tabs.Panels[index]
                };
                FrameworkElement rendered = NIRARichBlockRenderer.Render(card);
                RichElements.Insert(startIndex + offset, rendered);
                _richContentFrames.Insert(g + offset, (rendered, new[] { card }));
                offset++;
            }
            g += Math.Max(0, tabs.Items.Count - 1);
        }

        // Card groups are normally responsive multi-column surfaces. If photos
        // target separate cards, split only that group into independent sections
        // so each photo can sit directly under its own explanation.
        for (int g = 0; g < _richContentFrames.Count; g++)
        {
            var group = _richContentFrames[g];
            if (group.Blocks.Count <= 1 || group.Blocks.Any(b => b.Type != "card") ||
                !photos.Any(p => group.Blocks.Any(b =>
                    NIRAInlineImageAnchorPolicy.MatchSection(
                        string.IsNullOrWhiteSpace(p.SubjectQuery) ? p.Title : p.SubjectQuery,
                        b.Title, null, null, b.Text) > 0)))
                continue;
            int start = RichElements.IndexOf(group.View);
            if (start < 0) continue;
            RichElements.RemoveAt(start);
            _richContentFrames.RemoveAt(g);
            int offset = 0;
            foreach (var block in group.Blocks)
            {
                FrameworkElement separate = NIRARichBlockRenderer.Render(block);
                RichElements.Insert(start + offset, separate);
                _richContentFrames.Insert(g + offset, (separate, new[] { block }));
                offset++;
            }
            g += offset - 1;
        }

        // Anchor only when the whole verified search subject appears in a
        // block heading/label. Unsupported or ambiguous photo titles are NOT
        // evidence that an unrelated paragraph describes the picture.
        var attachedCount = new Dictionary<FrameworkElement, int>();
        foreach (var photo in photos)
        {
            string subject = string.IsNullOrWhiteSpace(photo.SubjectQuery)
                ? photo.Title : photo.SubjectQuery;
            // Prose and rich blocks are different WPF rows. Let the document
            // own photos with a real paragraph anchor; never double-render.
            if (!_richContentFrames.Any(pair => pair.Blocks.Any(b =>
                    !string.IsNullOrWhiteSpace(b.ImageSubject) &&
                    string.Equals(b.ImageSubject.Trim(), photo.SubjectQuery.Trim(),
                        StringComparison.OrdinalIgnoreCase))) &&
                NIRAInlineImageAnchorPolicy.FindLine(
                    NIRAResearchPresentationPolicy.StripEditorialImagePlacement(Content), subject) >= 0)
                continue;
            // Block 03 owns placement: an imageSubject equals one ACTUALLY
            // returned image-search subject. No match => no model-directed
            // placement; the established verified-text heuristic remains as
            // an automatic fallback for older saved conversations.
            var ranked = _richContentFrames.Select(pair => new
            {
                pair.View,
                Score = pair.Blocks.Max(b =>
                    !string.IsNullOrWhiteSpace(b.ImageSubject) &&
                    string.Equals(b.ImageSubject.Trim(), photo.SubjectQuery.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    ? 1000
                    : NIRAInlineImageAnchorPolicy.MatchSection(
                        string.IsNullOrWhiteSpace(photo.SubjectQuery) ? photo.Title : photo.SubjectQuery,
                        b.Title,
                        b.Type == "tabs" ? b.Items : null,
                        b.Type == "tabs" ? b.Panels : null,
                        b.Text + " " + string.Join(" ",
                            b.Rows.SelectMany(row => row).Take(80))))
            }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).ToArray();
            // A rich block must actually identify the subject. Dropping an
            // unanchored image is safer than a fake visual placed at the end.
            if (ranked.Length == 0) continue;
            FrameworkElement anchor = ranked[0].View;
            int position = RichElements.IndexOf(anchor) + 1;
            if (position <= 0) continue;
            if (attachedCount.TryGetValue(anchor, out int previousCount))
                position += previousCount;
            var frame = NIRAChatDocument.MakeInlinePhoto(photo, prominent: false);
            RichElements.Insert(Math.Min(position, RichElements.Count), frame);
            _richImageFrames.Add(frame);
            _richPlacedPhotoIds.Add(photo.ArtifactId);
            attachedCount[anchor] = attachedCount.GetValueOrDefault(anchor) + 1;
        }
        RebuildUnplacedAssistantImages();
    }

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
            _richContentFrames.RemoveAll(frame => ReferenceEquals(frame.View, entry.View));
            _photoCreditFrames.Remove(entry);
        }
    }

    public void AddRichBlocks(IReadOnlyList<NIRARichBlock> blocks,
        NIRAConversationArchiveStore? archive = null, Guid? messageId = null,
        Action<string>? followUp = null)
    {
        // Promote standalone, supported watch links in recommendation cards
        // to NIRA's existing native player. No topic/phrase-specific branching;
        // the trusted video URL parser controls admissibility.
        var normalized = NIRANativeVideoPresentationPolicy.ExpandCards(
            NIRAPresentationPolicy.Normalize(blocks));
        // Union rather than replace when multiple typed batches contribute.
        var known = new HashSet<string>(EmbeddedVideoKeys.Split('|',
            StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        foreach (var block in normalized.Where(b => b.Type == "video"))
            if (NIRAVideoReferencePolicy.TryParse(block.Text, out var video) && video != null)
                known.Add(video.Key);
        string keys = string.Join("|", known.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        if (keys != EmbeddedVideoKeys)
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
                _richContentFrames.Add((rendered, group.ToArray()));
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
                if ((block.Type is "card" or "text" or "insight") &&
                    !string.IsNullOrWhiteSpace(block.ImageSubject) &&
                    System.Text.RegularExpressions.Regex.IsMatch(block.Text,
                        @"(?im)^\s*imageSubject\s*:",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                    continue; // internal layout metadata, not explanatory content
                // Dupes from model-authored displayBlocks and later batches must
                // not generate new WebView2 processes or repeated players.
                if (block.Type == "video" &&
                    NIRAVideoReferencePolicy.TryParse(block.Text, out var richVideo) &&
                    richVideo != null)
                {
                    AddToVideoDeck(block, richVideo);
                    continue;
                }
                FrameworkElement rendered = NIRARichBlockRenderer.Render(block, saved, persist, followUp);
                RichElements.Add(rendered);
                _richContentFrames.Add((rendered, new[] { block }));
                if (NIRAImageCreditLayoutPolicy.IsDuplicatePhotoCredit(block, true))
                    _photoCreditFrames.Add((rendered, block));
            }
        }
        RefreshRichPhotoPlacement();
        KeepVideoDeckLast();
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
        _activityClock.Tick += (_, _) => OnPropertyChanged(nameof(ActivityHeader));

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
            OnPropertyChanged(nameof(HasInlineVisuals));
            OnPropertyChanged(nameof(IsEmpty));
        };

        VisualArtifacts.CollectionChanged +=
            (_, _) =>
            {
                RefreshRichPhotoPlacement();
                RebuildAssistantImageGalleryTiles();
                SuppressDuplicatePhotoCreditFrames();
                OnPropertyChanged(
                    nameof(HasVisualArtifacts));
                OnPropertyChanged(nameof(HasInlineVisuals));
                OnPropertyChanged(nameof(InlineVisualRevision));
                OnPropertyChanged(
                    nameof(IsEmpty));
            };
        SyncVideoDeckFromProse();
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

        // A true decoded visual fingerprint rejects rescaled/alternate-CDN
        // copies as well as repeated exact URLs. Never render one picture twice.
        if (!NIRAVisualDuplicateGuard.RegisterIfNovel(visual))
        {
            NIRAAgent.Diagnostics.NIRADiagnosticTrace.Event("IMAGE-VISUAL-DEDUP",
                "Stage=ChatPresentation Action=DropNearDuplicate Scope=RecentChat");
            return visual;
        }
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

// Each stage is based on the host-owned activity ledger. No model prose is
// accepted here, and internal tool IDs never become visible UI text.
public sealed class NIRAActivityTimelineEntry : INotifyPropertyChanged
{
    private string _detail;
    private string _glyph;
    public string Title { get; }
    public string Detail { get => _detail; private set { _detail = value; OnPropertyChanged(); } }
    public string Glyph { get => _glyph; private set { _glyph = value; OnPropertyChanged(); } }
    public bool IsTool { get; }
    public string ToolIdentity { get; }
    public bool IsFinal { get; private set; }

    public NIRAActivityTimelineEntry(string title, string detail, string glyph,
        bool isTool = false, string toolIdentity = "")
    {
        Title = title;
        _detail = detail;
        _glyph = glyph;
        IsTool = isTool;
        ToolIdentity = toolIdentity;
    }

    public void Complete(string detail, bool succeeded)
    {
        if (IsFinal) return;
        Detail = detail;
        Glyph = succeeded ? "✓" : "!";
        IsFinal = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

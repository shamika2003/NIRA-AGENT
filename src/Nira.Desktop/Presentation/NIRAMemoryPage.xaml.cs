using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Nira.Core;
using Nira.Infrastructure;

namespace NIRAAgent.UI.Presentation;

/// <summary>Embedded local memory workspace; opening it never calls the reasoning provider.</summary>
public partial class NIRAMemoryPage : UserControl
{
    private enum MemorySection { Notes, Suggestions, Preferences, Skills }
    private MemorySection _section = MemorySection.Notes;
    private SqliteConversationStore? _store;
    private bool _loading;

    public NIRAMemoryPage() => InitializeComponent();

    internal async Task InitializeAsync(SqliteConversationStore store)
    {
        _store = store;
        await RefreshAsync();
    }

    private async void Notes_Click(object sender, RoutedEventArgs e) => await SelectAsync(MemorySection.Notes);
    private async void Suggestions_Click(object sender, RoutedEventArgs e) => await SelectAsync(MemorySection.Suggestions);
    private async void Preferences_Click(object sender, RoutedEventArgs e) => await SelectAsync(MemorySection.Preferences);
    private async void Skills_Click(object sender, RoutedEventArgs e) => await SelectAsync(MemorySection.Skills);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task SelectAsync(MemorySection section)
    {
        if (_loading) return;
        _section = section;
        NotesButton.Tag = section == MemorySection.Notes ? "ACTIVE" : null;
        SuggestionsButton.Tag = section == MemorySection.Suggestions ? "ACTIVE" : null;
        PreferencesButton.Tag = section == MemorySection.Preferences ? "ACTIVE" : null;
        SkillsButton.Tag = section == MemorySection.Skills ? "ACTIVE" : null;
        (SectionTitle.Text, SectionSummary.Text) = section switch
        {
            MemorySection.Notes => ("Saved memories", "Review and manage the notes NIRA can recall."),
            MemorySection.Suggestions => ("Learning suggestions", "Review pending suggestions from manual learning mode."),
            MemorySection.Skills => ("Learned skills", "Reusable procedures NIRA has learned from completed operations."),
            _ => ("Memory preferences", "Control automatic recall and learning for future conversations.")
        };
        ContentScroll.ScrollToTop();
        await RefreshAsync();
    }

    private void SetLoading(bool loading)
    {
        _loading = loading;
        PageCard.IsEnabled = !loading;
        // The scroll content may not yet have a visual parent during rendering.
        // Disable its own root as well; never use effective IsEnabled as consent.
        PageBody.IsEnabled = !loading;
        NotesButton.IsEnabled = SuggestionsButton.IsEnabled = PreferencesButton.IsEnabled = SkillsButton.IsEnabled = !loading;
    }

    private async Task RefreshAsync(string success = "Ready")
    {
        if (_loading || _store is not { } store) return;
        SetLoading(true);
        StatusText.Text = "Loading local memory…";
        try
        {
            PageBody.Children.Clear();
            switch (_section)
            {
                case MemorySection.Notes:
                    RenderNotes(store, await Task.Run(() => store.ListMemoryNotesAsync(default)));
                    break;
                case MemorySection.Suggestions:
                    var pending = await Task.Run(() => store.ListMemoryCandidatesAsync(default));
                    var saved = await Task.Run(() => store.ListMemoryNotesAsync(default));
                    RenderSuggestions(store, pending, saved);
                    break;
                case MemorySection.Skills:
                    RenderSkills(store, await Task.Run(() => store.GetSkillLearningAsync(default)),
                        await Task.Run(() => store.ListProceduresAsync(default)));
                    break;
                default:
                    RenderPreferences(store, await Task.Run(() => store.GetMemorySettingsAsync(default)));
                    break;
            }
            StatusText.Text = success;
        }
        catch (Exception error)
        {
            Debug.WriteLine(error);
            PageBody.Children.Clear();
            StatusText.Text = "Could not load local memory. Use Refresh to try again.";
        }
        finally { SetLoading(false); }
    }

    private TextBlock AddText(string text, bool heading = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
            FontSize = heading ? 16 : 13, Margin = new Thickness(0, 0, 0, heading ? 9 : 13),
            FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal };
        block.SetResourceReference(TextBlock.ForegroundProperty, "NIRATextBrush");
        PageBody.Children.Add(block); return block;
    }
    private Button AddButton(string text, Action action)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 140, Margin = new Thickness(0, 0, 0, 9), Padding = new Thickness(12, 8, 12, 8) };
        button.Click += (_, _) => { try { action(); } catch (Exception error) { StatusText.Text = error.Message; } };
        PageBody.Children.Add(button); return button;
    }
    private void RenderSkills(SqliteConversationStore store, bool enabled, IReadOnlyList<LearnedProcedure> procedures)
    {
        AddText("Procedural learning", true);
        var option = new CheckBox { Content = "Automatically learn and recall reusable procedures", IsChecked = enabled,
            Margin = new Thickness(0, 0, 0, 12) };
        option.SetResourceReference(Control.ForegroundProperty, "NIRATextBrush");
        PageBody.Children.Add(option);
        AddText("When enabled, NIRA reviews completed operation sequences with the configured reasoning model. Only capability names and schemas, not actual arguments, file contents or results, are sent for this review. Saved procedures are local and encrypted. Reuse always requires fresh evidence and normal authorization.");
        AddButton("Save skill preference", () => {
            bool requested = option.IsChecked == true;
            _ = ChangeMemoryAsync(() => store.SetSkillLearningAsync(requested, default));
        });
        AddText($"Learned procedures ({procedures.Count})", true);
        if (procedures.Count == 0) { AddText("No learned procedures yet. Complete a useful multi-capability task with learning enabled."); return; }
        var list = new ListBox { ItemsSource = procedures, DisplayMemberPath = nameof(LearnedProcedure.Name),
            MaxHeight = 260, MinHeight = 80, Margin = new Thickness(0, 0, 0, 10) };
        PageBody.Children.Add(list);
        var details = AddText("Select a procedure to inspect its inputs, steps and evidence.");
        list.SelectionChanged += (_, _) => details.Text = list.SelectedItem is LearnedProcedure p
            ? $"Version {p.Version} · Matching reuse sequences: {p.Matches} · Deviations: {p.Deviations}\n{p.Purpose}\nInputs: {p.Inputs}\nPreconditions: {p.Preconditions}\n" +
                string.Join("\n", p.Steps.Select((s, i) => $"{i + 1}. {s.Tool}: {s.Guidance}")) +
                $"\nVerification: {p.Verification}\nSource task: {p.SourceTask}\nObserved operation IDs: {string.Join(", ", p.EvidenceIds)}"
            : "Select a procedure to inspect its inputs, steps and evidence.";
        AddText("Sequence matches are not proof that every user objective was achieved. Older incompatible contracts are not offered for reuse.");
        AddButton("Remove selected procedure", () => {
            if (list.SelectedItem is not LearnedProcedure p) return;
            if (MessageBox.Show("Remove this procedure and all its versions? Original conversation evidence is retained.", "NIRA",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _ = ChangeMemoryAsync(() => store.DeleteProcedureAsync(p.Id, default));
        });
    }

    private void RenderPreferences(SqliteConversationStore store, MemoryRecallSettings settings)
    {
        AddText("Memory and learning", true);
        AddText("Saved memories are separate from chat history. Only saved notes are used for automatic recall. Do not store passwords or secret keys.");
        CheckBox AddOption(string label, bool enabled)
        {
            var option = new CheckBox { Content = label, IsChecked = enabled, Margin = new Thickness(0, 0, 0, 12) };
            option.SetResourceReference(Control.ForegroundProperty, "NIRATextBrush");
            PageBody.Children.Add(option);
            return option;
        }
        var recall = AddOption("Automatically recall relevant saved notes", settings.RecallEnabled);
        var learning = AddOption("Learn from completed tasks", settings.SuggestLearning);
        var auto = AddOption("Automatically save new memories without individual approval", settings.AutoSaveLearning);
        void RefreshAutoOption()
        {
            // IsEnabled is inherited/coerced by WPF. The page is temporarily
            // disabled during loading, which must not clear a persisted choice.
            bool prerequisitesMet = recall.IsChecked == true && learning.IsChecked == true;
            auto.IsEnabled = prerequisitesMet;
            if (!prerequisitesMet) auto.IsChecked = false;
        }
        recall.Checked += (_, _) => RefreshAutoOption();
        recall.Unchecked += (_, _) => RefreshAutoOption();
        learning.Checked += (_, _) => RefreshAutoOption();
        learning.Unchecked += (_, _) => RefreshAutoOption();
        RefreshAutoOption();
        AddText("Recall sends selected matching notes to your configured reasoning model. If local matching finds nothing, NIRA may ask that model to suggest search phrases using only the current message, then match saved notes locally. Learning reviews recent user text in a separate bounded request. With recall and learning enabled, automatic saving can retain new grounded facts without per-note approval. Complementary facts may be added; explicitly changed automatic memories may be revised after comparison, retaining previous versions locally. Ambiguous conflicts and manually saved memories are not automatically overwritten. Without automatic saving, learning remains review-only. Previously pending suggestions remain pending; they are not silently promoted. You can disable learning or edit and remove saved notes at any time.");
        AddButton("Save memory preferences", () => {
            // Snapshot the user's choices before the page enters its busy state.
            var requested = new MemoryRecallSettings(recall.IsChecked == true,
                learning.IsChecked == true, auto.IsChecked == true);
            _ = ChangeMemoryAsync(() => store.SetMemorySettingsAsync(requested, default));
        });

    }

    private void RenderSuggestions(SqliteConversationStore store, IReadOnlyList<PendingMemoryCandidate> pending,
        IReadOnlyList<SavedMemoryNote> saved)
    {
        AddText($"Learning suggestions ({pending.Count})", true);
        if (pending.Count == 0) AddText("No pending suggestions.");
        else
        {
            var candidates = new ListBox { ItemsSource = pending, DisplayMemberPath = nameof(PendingMemoryCandidate.Content), MaxHeight = 150, MinHeight = 60, Margin = new Thickness(0, 0, 0, 8) };
            PageBody.Children.Add(candidates);
            var candidateDetails = AddText("Select a suggestion to review its evidence.");
            candidates.SelectionChanged += (_, _) => candidateDetails.Text = candidates.SelectedItem is PendingMemoryCandidate candidate
                ? $"{candidate.Details.Kind} · {candidate.Details.Topic}\n{candidate.Content}\nSource: {candidate.Details.SourceExcerpt}\nCreated: {candidate.CreatedUtc}" + RelatedSuggestionText(candidate, saved)
                : "Select a suggestion to review its evidence.";
            AddButton("Accept as new saved note", () => {
                if (candidates.SelectedItem is PendingMemoryCandidate candidate)
                    _ = ChangeMemoryAsync(async () => {
                        if (!await store.ReviewMemoryCandidateAsync(candidate.Id, true, default)) throw new InvalidOperationException("Suggestion no longer exists.");
                    });
            });
            AddButton("Dismiss selected suggestion", () => {
                if (candidates.SelectedItem is PendingMemoryCandidate candidate)
                    _ = ChangeMemoryAsync(async () => {
                        if (!await store.ReviewMemoryCandidateAsync(candidate.Id, false, default)) throw new InvalidOperationException("Suggestion no longer exists.");
                    });
            });
        }

    }

    private static string RelatedSuggestionText(PendingMemoryCandidate candidate, IReadOnlyList<SavedMemoryNote> saved)
    {
        var related = SqliteConversationStore.RelatedLearningNotes(candidate, saved);
        if (related.Count == 0) return "";
        return "\n\nRelated approved notes (local comparison):\n" +
            string.Join("\n", related.Select(note => "• " + note.Content)) +
            "\nAccept only if the suggestion is a distinct fact. If it replaces an existing fact, correct that note in Saved memories instead.";
    }

    private void RenderNotes(SqliteConversationStore store, IReadOnlyList<SavedMemoryNote> notes)
    {
        AddText($"Saved notes ({notes.Count})", true);
        AddText("Search by note text, category, topic or tag.");
        var filter = new TextBox { MaxLength = 200, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Filter by text, category, topic or tag" };
        PageBody.Children.Add(filter);
        var list = new ListBox { ItemsSource = notes, DisplayMemberPath = nameof(SavedMemoryNote.Content), MaxHeight = 330, MinHeight = 100, Margin = new Thickness(0, 0, 0, 12) };
        PageBody.Children.Add(list);
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(notes);
        view.Filter = item => item is SavedMemoryNote note &&
            (note.Content + " " + note.Kind + " " + note.Topic + " " + string.Join(" ", note.Tags)).Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase);
        filter.TextChanged += (_, _) => view.Refresh();
        var selected = AddText("Select a saved note to inspect or correct it.");
        var input = new TextBox { MaxLength = 3000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, Margin = new Thickness(0, 0, 0, 12) };
        PageBody.Children.Add(input);
        list.SelectionChanged += (_, _) => {
            if (list.SelectedItem is SavedMemoryNote note)
            {
                selected.Text = $"{note.Kind} · {note.Topic}\nSource: {note.Source}\nUpdated: {note.UpdatedUtc}\nTags: {string.Join(", ", note.Tags)}" +
                    (note.Metadata?.SourceExcerpt is { } excerpt ? "\nEvidence: " + excerpt : "");
                input.Text = note.Content;
            }
            else { selected.Text = "Select a saved note to inspect or correct it."; input.Clear(); }
        };
        var versions = AddText("");
        list.SelectionChanged += (_, _) => versions.Text = "";
        AddButton("Show previous automatic versions", () => {
            if (list.SelectedItem is SavedMemoryNote note)
                _ = ShowVersionsAsync(store, list, versions, note);
        });
        AddButton("New blank note", () => { list.SelectedItem = null; input.Clear(); input.Focus(); });
        AddButton("Save as new note", () => _ = ChangeMemoryAsync(async () => {
            await store.StoreMemoryAsync(input.Text, "explicit-memory-user", default);
        }));
        AddButton("Update selected note", () => {
            if (list.SelectedItem is SavedMemoryNote note)
                _ = ChangeMemoryAsync(async () => {
                    if (!await store.ChangeMemoryNoteAsync(note.Id, note.Content, input.Text, "explicit-memory-user",
                            new MemoryDetails(note.Kind, note.Topic, note.Tags), default))
                        throw new InvalidOperationException("Note changed, disappeared, or the new text duplicates another note.");
                });
        });
        AddButton("Remove selected note", () => {
            if (list.SelectedItem is SavedMemoryNote note && Window.GetWindow(this) is { } owner && MessageBox.Show(owner, "Remove this saved note and its previous automatic versions? Original chats and audit records will remain.", "NIRA", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _ = ChangeMemoryAsync(async () => {
                    if (!await store.ChangeMemoryNoteAsync(note.Id, note.Content, null, "explicit-memory-user", null, default))
                        throw new InvalidOperationException("Note changed or no longer exists.");
                });
        });
        AddText("Up to 2,000 recent saved notes are shown and searched. Deleting a note does not erase original conversations, model-provider history or earlier audit records. Explicit private history searches require separate approval.");
    }

    private static async Task ShowVersionsAsync(SqliteConversationStore store, ListBox list, TextBlock output, SavedMemoryNote note)
    {
        try
        {
            var versions = await store.ListMemoryVersionsAsync(note.Id, default);
            if (list.SelectedItem is not SavedMemoryNote current || current.Id != note.Id) return;
            output.Text = versions.Count == 0 ? "No previous automatic versions." : string.Join("\n\n", versions.Select(v =>
                $"Replaced: {v.ReplacedUtc}\n{v.Prior.Content}\nEvidence: {v.Prior.Metadata?.SourceExcerpt ?? "Not recorded"}"));
        }
        catch (Exception error) when (error is not OperationCanceledException) { output.Text = "Previous versions could not be read."; }
    }

    private async Task ChangeMemoryAsync(Func<Task> change)
    {
        if (_loading) return;
        SetLoading(true);
        try { await change(); }
        catch (Exception error) { StatusText.Text = error.Message; return; }
        finally { SetLoading(false); }
        await RefreshAsync("Memory updated");
    }
}

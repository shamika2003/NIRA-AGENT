using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Nira.Core;
using Nira.Infrastructure;

namespace NIRAAgent.UI.Presentation;

/// <summary>Embedded local memory workspace; opening it never calls the reasoning provider.</summary>
public partial class NIRAMemoryPage : UserControl
{
    private enum MemorySection { Notes, Suggestions, Preferences }
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
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task SelectAsync(MemorySection section)
    {
        if (_loading) return;
        _section = section;
        NotesButton.Tag = section == MemorySection.Notes ? "ACTIVE" : null;
        SuggestionsButton.Tag = section == MemorySection.Suggestions ? "ACTIVE" : null;
        PreferencesButton.Tag = section == MemorySection.Preferences ? "ACTIVE" : null;
        (SectionTitle.Text, SectionSummary.Text) = section switch
        {
            MemorySection.Notes => ("Saved memories", "Review and manage the notes NIRA can recall."),
            MemorySection.Suggestions => ("Learning suggestions", "Review what NIRA proposes to remember before accepting it."),
            _ => ("Memory preferences", "Control automatic recall and learning for future conversations.")
        };
        ContentScroll.ScrollToTop();
        await RefreshAsync();
    }

    private void SetLoading(bool loading)
    {
        _loading = loading;
        PageCard.IsEnabled = !loading;
        NotesButton.IsEnabled = SuggestionsButton.IsEnabled = PreferencesButton.IsEnabled = !loading;
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
    private void RenderPreferences(SqliteConversationStore store, MemoryRecallSettings settings)
    {
        AddText("Memory and learning", true);
        AddText("Saved memories are separate from chat history. Only approved notes are used for automatic recall. Do not store passwords or secret keys.");
        CheckBox AddOption(string label, bool enabled)
        {
            var option = new CheckBox { Content = label, IsChecked = enabled, Margin = new Thickness(0, 0, 0, 12) };
            option.SetResourceReference(Control.ForegroundProperty, "NIRATextBrush");
            PageBody.Children.Add(option);
            return option;
        }
        var recall = AddOption("Automatically recall relevant saved notes", settings.RecallEnabled);
        var learning = AddOption("Suggest memories after completed tasks", settings.SuggestLearning);
        AddText("Recall sends only selected matching notes to your configured reasoning model. Learning reviews recent user text from a completed task with the same model in a separate bounded request. When both options are enabled, relevant approved notes are included for comparison to avoid repeated suggestions. Pending suggestions stay local and are not recalled until you accept them. Both options are off by default.");
        AddButton("Save memory preferences", () => _ = ChangeMemoryAsync(async () =>
            await store.SetMemorySettingsAsync(new MemoryRecallSettings(recall.IsChecked == true, learning.IsChecked == true), default)));

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
        AddText($"Approved notes ({notes.Count})", true);
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
            if (list.SelectedItem is SavedMemoryNote note && Window.GetWindow(this) is { } owner && MessageBox.Show(owner, "Remove this saved note? Original chats and audit records will remain.", "NIRA", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _ = ChangeMemoryAsync(async () => {
                    if (!await store.ChangeMemoryNoteAsync(note.Id, note.Content, null, "explicit-memory-user", null, default))
                        throw new InvalidOperationException("Note changed or no longer exists.");
                });
        });
        AddText("Up to 2,000 recent approved notes are shown and searched. Deleting a note does not erase original conversations, model-provider history or earlier audit records. Explicit private history searches require separate approval.");
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

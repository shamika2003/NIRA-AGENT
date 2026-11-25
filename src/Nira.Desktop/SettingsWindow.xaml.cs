using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nira.Infrastructure;
using NIRAAgent.UI;
using NIRAAgent.UI.Theming;

namespace Nira.Desktop;

public partial class SettingsWindow : Window
{
    private readonly NiraRuntime _runtime;
    private readonly NIRAWorkAreaWindowGuard _guard;
    private readonly SqliteConversationStore _store;
    private bool _loading;
    private bool _closed;
    private string _page = "Appearance";

    public SettingsWindow(NiraRuntime runtime, string page = "Appearance")
    {
        _runtime = runtime;
        _store = (SqliteConversationStore)runtime.Conversations;
        InitializeComponent();
        _guard = new NIRAWorkAreaWindowGuard(this, fitNormalWindow: true);
        UtilityBackgroundEclipse.Source = NIRABackdropBorder.LoadBitmap("nira-utility-eclipse.png");
        UtilityBackgroundHalo.Source = NIRABackdropBorder.LoadBitmap("nira-utility-halo.png");
        UpdateTheme();
        Loaded += async (_, _) => await ShowPageAsync(page);
        Closed += (_, _) => { _closed = true; _guard.Dispose(); };
    }

    private void UpdateTheme()
    {
        bool halo = NIRAThemeManager.CurrentMode == NIRAThemeMode.Halo;
        UtilityBackgroundHalo.Opacity = halo ? 1 : 0;
        UtilityBackgroundEclipse.Opacity = halo ? 0 : 1;
        ThemeToggleButton.Content = halo ? "HALO" : "ECLIPSE";
    }
    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        NIRAThemeManager.ApplyTheme(NIRAThemeManager.CurrentMode == NIRAThemeMode.Halo ? NIRAThemeMode.Eclipse : NIRAThemeMode.Halo);
        UpdateTheme();
        StatusText.Text = "Appearance applied";
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private async void SettingsNavigation_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string page }) await ShowPageAsync(page); }

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
    private async Task ShowPageAsync(string page)
    {
        if (_loading || _closed) return;
        _loading = true; _page = page; PageCard.IsEnabled = false;
        try
        {
            PageTitle.Text = page == "Model" ? "Model & credentials" : page == "History" ? "Conversation history" : page == "Memory" ? "Saved memory" : page;
            PageBody.Children.Clear(); StatusText.Text = "Ready"; SettingsContentScroll.ScrollToTop();
            switch (page)
            {
                case "Appearance":
                    AddText("Interface theme", true);
                    AddText("HALO and ECLIPSE use the original NIRA artwork. The title-bar switch applies and saves the theme immediately.");
                    AddButton("Switch theme", () => ThemeToggleButton_Click(this, new RoutedEventArgs()));
                    AddText("Motion", true);
                    var motion = new CheckBox { Content = "Reduce chat and sidebar motion for this session", IsChecked = Environment.GetEnvironmentVariable("NIRA_REDUCE_MOTION") == "1", Margin = new Thickness(0, 0, 0, 12) };
                    motion.Checked += (_, _) => Environment.SetEnvironmentVariable("NIRA_REDUCE_MOTION", "1");
                    motion.Unchecked += (_, _) => Environment.SetEnvironmentVariable("NIRA_REDUCE_MOTION", null);
                    PageBody.Children.Add(motion);
                    AddText("Voice, proactive presence and other legacy controls are not connected yet.");
                    break;
                case "Model":
                    AddText("Active reasoning provider", true);
                    AddText((Environment.GetEnvironmentVariable("NIRA_MODEL_NAME") ?? "gpt-oss:120b") + "\n" + (Environment.GetEnvironmentVariable("NIRA_MODEL_ENDPOINT") ?? "https://ollama.com/v1/"));
                    AddText("Vision provider", true);
                    AddText(Environment.GetEnvironmentVariable("NIRA_VISION_MODEL") ?? "Not configured");
                    AddText("Reasoning API credential", true);
                    AddText("Enter a replacement key. It is encrypted for this Windows account and endpoint. The saved key is never displayed.");
                    var password = new PasswordBox { MaxLength = 8192, MinHeight = 36, Margin = new Thickness(0, 0, 0, 10) };
                    PageBody.Children.Add(password);
                    AddButton("Save credential", () => { _runtime.ModelCredentials.Save(password.Password); password.Clear(); StatusText.Text = "Credential saved securely"; });
                    AddButton("Clear saved credential", () => {
                        if (MessageBox.Show(this, "Clear the stored key for the current endpoint? An environment key may remain.", "NIRA", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                        _runtime.ModelCredentials.ClearStored(); password.Clear(); StatusText.Text = "Saved credential cleared";
                    });
                    AddText("Model and endpoint changes still require launch configuration and a restart.");
                    AddText("Image generation", true);
                    AddText(_runtime.DevelopmentPermissions
                        ? "Cloudflare Workers AI · FLUX.2 Klein 4B. Development mode sends requested image prompts without a confirmation. Quotas or usage charges may apply."
                        : "Cloudflare Workers AI · FLUX.2 Klein 4B. The image prompt is transmitted only after approval. Quotas or usage charges may apply.");
                    string imageHome = System.IO.Path.GetDirectoryName(_runtime.DatabasePath)!;
                    var imageStatus = AddText(ImageGenerationProvider.Load(imageHome) is null ? "Image credentials: not configured" : "Image credentials: saved (available now; provider access untested)");
                    AddText("Cloudflare Account ID");
                    var imageAccount = new TextBox { MaxLength = 32, MinHeight = 36, Margin = new Thickness(0, 0, 0, 10) };
                    PageBody.Children.Add(imageAccount);
                    AddText("Workers AI API token (hidden)");
                    var imageToken = new PasswordBox { MaxLength = 512, MinHeight = 36, Margin = new Thickness(0, 0, 0, 10) };
                    PageBody.Children.Add(imageToken);
                    AddButton("Save image credentials", () => {
                        ImageGenerationProvider.Save(imageHome, imageAccount.Text.Trim(), imageToken.Password.Trim());
                        imageToken.Clear(); imageAccount.Clear();
                        imageStatus.Text = "Image credentials: saved (available now; provider access untested)";
                        StatusText.Text = "Saved securely. Available now. No image request sent.";
                    });
                    AddButton("Clear image credentials", () => {
                        if (MessageBox.Show(this, "Clear the stored image credentials? Future generation will require new credentials.", "NIRA", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                        ImageGenerationProvider.Clear(imageHome); imageToken.Clear(); imageAccount.Clear();
                        imageStatus.Text = "Image credentials: not configured";
                        StatusText.Text = "Saved image credentials cleared.";
                    });
                    break;
                case "History": await ShowHistoryAsync(); break;
                case "Memory": await ShowMemoryAsync(); break;
                case "Permissions":
                    AddText("Current authorized workspace", true); AddText(_runtime.Workspace);
                    AddText(_runtime.DevelopmentPermissions ? "Development permissions · ACTIVE" : "Standard permissions · ACTIVE", true);
                    AddText(_runtime.DevelopmentPermissions
                        ? "Public searches, ordinary reads, public browsing and requested image/GIF generation run without repeated confirmation. File creation, editing, deletion, private-file uploads, commands and desktop/browser mutations still require approval. Image generation may consume provider quota or incur charges."
                        : "Each outside-workspace or dangerous operation requires the runtime's one-time approval. Attachments and saved chats grant no extra authority.");
                    if (_runtime.DevelopmentPermissions) AddText("This policy applies to Debug builds only. Restart with NIRA_DEVELOPMENT_PERMISSIONS=0 to use standard permissions.");
                    AddText("This page is read-only. It cannot issue approvals, change workspace scope or replay previous operations.");
                    break;
            }
        }
        catch (Exception error) { if (!_closed) StatusText.Text = error.Message; }
        finally { _loading = false; if (!_closed) PageCard.IsEnabled = true; }
    }
    private async Task ShowHistoryAsync()
    {
        AddText("Rename or archive saved conversations. Archiving hides the sidebar card; it retains all messages, files and execution records. Restore archived chats here.");
        var items = await _store.ListRecentAsync(default, includeArchived: true);
        if (_closed) return;
        var list = new ComboBox { MinHeight = 36, Margin = new Thickness(0, 0, 0, 12), ItemsSource = items.Select(item => new HistoryChoice(item)).ToArray() };
        PageBody.Children.Add(list);
        var title = new TextBox { MaxLength = 160, MinHeight = 35, Margin = new Thickness(0, 0, 0, 10) };
        PageBody.Children.Add(title);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is HistoryChoice choice) title.Text = choice.Item.ThreadTitle; };
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        AddButton("Rename conversation", () => _ = ChangeHistoryAsync(list, title.Text, null));
        AddButton("Archive conversation", () => _ = ChangeHistoryAsync(list, null, true));
        AddButton("Restore conversation", () => _ = ChangeHistoryAsync(list, null, false));
        AddText("Up to 80 recent logical chats from the latest 500 task records are listed. Archiving does not cancel tasks.");
    }
    private async Task ChangeHistoryAsync(ComboBox list, string? title, bool? archive)
    {
        if (_loading || _closed || list.SelectedItem is not HistoryChoice choice) return;
        _loading = true; PageCard.IsEnabled = false;
        try { await _store.SetChatPreferencesAsync(choice.Item.ThreadId, title, archive ?? choice.Item.Archived, default); }
        catch (Exception error) { StatusText.Text = error.Message; return; }
        finally { _loading = false; if (!_closed) PageCard.IsEnabled = true; }
        await ShowPageAsync(_page);
        if (!_closed) StatusText.Text = "Conversation preferences saved";
    }
    private async Task ShowMemoryAsync()
    {
        AddText("Explicit saved notes", true);
        AddText("These notes are separate from chat history. Add only facts you want NIRA to retain. Do not store passwords or secret keys.");
        var notes = await _store.ListMemoryNotesAsync(default);
        if (_closed) return;
        var list = new ListBox { ItemsSource = notes, DisplayMemberPath = nameof(SavedMemoryNote.Content), MaxHeight = 210, MinHeight = 65, Margin = new Thickness(0, 0, 0, 12) };
        PageBody.Children.Add(list);
        var selected = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        selected.SetResourceReference(TextBlock.ForegroundProperty, "NIRATextBrush");
        list.SelectionChanged += (_, _) => selected.Text = (list.SelectedItem as SavedMemoryNote)?.Content ?? "";
        PageBody.Children.Add(selected);
        AddButton("Remove selected note", () => {
            if (list.SelectedItem is SavedMemoryNote note && MessageBox.Show(this, "Remove this saved note? Original chats and audit records will remain.", "NIRA", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _ = ChangeMemoryAsync(null, note.Id);
        });
        AddText("Add a note", true);
        var input = new TextBox { MaxLength = 3000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, Margin = new Thickness(0, 0, 0, 12) };
        PageBody.Children.Add(input);
        AddButton("Save note", () => _ = ChangeMemoryAsync(input.Text, null));
        AddText("Notes are searchable through NIRA's existing memory tool; automatic recall is not added by this update. Up to 500 recent notes are shown.");
    }
    private async Task ChangeMemoryAsync(string? note, string? id)
    {
        if (_loading || _closed) return;
        _loading = true; PageCard.IsEnabled = false;
        try
        {
            if (id is not null) { if (!await _store.RemoveMemoryNoteAsync(id, default)) throw new InvalidOperationException("Note no longer exists."); }
            else { string text = note?.Trim() ?? ""; if (text.Length is < 1 or > 3000) throw new ArgumentException("Enter 1–3000 characters."); await _store.StoreMemoryAsync(text, "explicit-settings-user", default); }
        }
        catch (Exception error) { StatusText.Text = error.Message; return; }
        finally { _loading = false; if (!_closed) PageCard.IsEnabled = true; }
        await ShowPageAsync(_page);
        if (!_closed) StatusText.Text = "Saved notes updated";
    }
    private sealed record HistoryChoice(SavedConversationSummary Item)
    {
        public override string ToString() => (Item.Archived ? "[Archived] " : "") + Item.ThreadTitle;
    }
}

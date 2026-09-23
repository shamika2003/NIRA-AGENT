using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using NIRAAgent.Conversation;
using NIRAAgent.UI.Theming;
using NIRAAgent.Presentation;
using NIRAAgent.UI.Presentation;

namespace NIRAAgent.UI;

// An archived conversation remains read-only. Attaching provides evidence for
// one new user turn; this window never reactivates prior goals or permissions.
public sealed class PastChatWindow : Window
{
    private readonly IReadOnlyList<NIRAArchivedChatTurn> _turns;
    private readonly NIRAConversationArchiveStore _archive;
    private readonly StackPanel _messages = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _search;
    private readonly TextBlock _searchStatus;
    private readonly List<Border> _matchingBubbles = new();
    private int _matchIndex;
    private readonly Image _backgroundEclipse;
    private readonly Image _backgroundHalo;
    private readonly TextBlock _searchHint;

    // The PNGs are resources already included in NIRAAgent.UI.csproj.
    // Use the same artwork as MainWindow rather than copying image assets.
    private static BitmapImage Artwork(string file) => new(
        new Uri($"pack://application:,,,/Assets/{file}", UriKind.Absolute));

    private static void Theme(FrameworkElement element, DependencyProperty property, string resourceKey) =>
        element.SetResourceReference(property, resourceKey);

    private static void Theme(FrameworkContentElement element, DependencyProperty property, string resourceKey) =>
        element.SetResourceReference(property, resourceKey);

    private static TextBlock Label(string value, double size, string brush, FontWeight? weight = null)
    {
        var label = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (weight.HasValue) label.FontWeight = weight.Value;
        Theme(label, TextBlock.ForegroundProperty, brush);
        return label;
    }

    private static Border Panel(string background, string border, double radius = 12)
    {
        var panel = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1) };
        Theme(panel, Border.BackgroundProperty, background);
        Theme(panel, Border.BorderBrushProperty, border);
        return panel;
    }

    private static ControlTemplate RoundedButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Button.Background))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Button.BorderBrush))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Button.BorderThickness))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        // A custom ControlTemplate does NOT apply Button.Padding automatically.
        // Without this binding every label measures as unpadded and gets clipped.
        content.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(Button.Padding))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        content.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Button.Content))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static ControlTemplate RoundedSearchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(TextBox.Background))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(TextBox.BorderBrush))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(TextBox.BorderThickness))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.PaddingProperty, new Binding(nameof(TextBox.Padding))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        border.AppendChild(host);
        return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
    }

    private static Button ActionButton(string label, bool primary, string tooltip)
    {
        var button = new Button
        {
            Content = label, ToolTip = tooltip, MinHeight = 38,
            Padding = new Thickness(15, 7, 15, 7),
            FontSize = 12.5, FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
            Template = RoundedButtonTemplate()
        };
        if (primary)
            button.Foreground = Brushes.White; // Halo text is dark; primary blue needs white text.
        else
            Theme(button, Control.ForegroundProperty, "NIRATextBrush");
        Theme(button, Control.BackgroundProperty, primary ? "NIRAPrimaryButtonFill" : "NIRAChipBrush");
        Theme(button, Control.BorderBrushProperty, primary ? "NIRABlueBrush" : "NIRAGlassBorderBrush");
        button.MouseEnter += (_, _) =>
        {
            if (!primary) Theme(button, Control.BackgroundProperty, "NIRACardHoverBrush");
            Theme(button, Control.BorderBrushProperty, "NIRAFocusBorderBrush");
        };
        button.MouseLeave += (_, _) =>
        {
            Theme(button, Control.BackgroundProperty, primary ? "NIRAPrimaryButtonFill" : "NIRAChipBrush");
            Theme(button, Control.BorderBrushProperty, primary ? "NIRABlueBrush" : "NIRAGlassBorderBrush");
        };
        return button;
    }

    private Button WindowButton(string glyph, string tooltip, Action action, bool close = false)
    {
        var button = ActionButton(glyph, false, tooltip);
        button.Width = 39;
        button.Height = 34;
        button.MinHeight = 34;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(4, 0, 0, 0);
        button.FontFamily = new FontFamily("Segoe MDL2 Assets");
        button.FontSize = 12;
        button.Click += (_, _) => action();
        if (close)
        {
            button.MouseEnter += (_, _) => Theme(button, Control.BorderBrushProperty, "NIRARedBrush");
        }
        return button;
    }

    public PastChatWindow(NIRAConversationArchiveStore archive, NIRAArchivedChatSession session)
    {
        _archive = archive;
        _turns = archive.ReadSession(session.SessionId);
        Title = "NIRA · Past Chat";
        // Keep the native taskbar icon as the app icon, but use the official PNG
        // for the custom title strip; ICO scaling looked blurry in the viewer.
        Icon = Application.Current?.MainWindow?.Icon;
        Width = 1000;
        Height = 750;
        MinWidth = 640;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Theme(this, BackgroundProperty, "NIRABackgroundBrush");
        Theme(this, ForegroundProperty, "NIRATextBrush");
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(7),
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        var outer = new Grid();
        Theme(outer, System.Windows.Controls.Panel.BackgroundProperty, "NIRAWindowGradient");
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(57) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Content = outer;

        // Match MainWindow's Eclipse/Halo background images and quiet veil.
        // Put artwork BEHIND all controls, spanning the complete custom window.
        _backgroundEclipse = new Image
        {
            Source = Artwork("nira-background-eclipse.png"),
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false
        };
        _backgroundHalo = new Image
        {
            Source = Artwork("nira-background-halo.png"),
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(_backgroundEclipse, 3);
        Grid.SetRowSpan(_backgroundHalo, 3);
        outer.Children.Add(_backgroundEclipse);
        outer.Children.Add(_backgroundHalo);

        var backdropVeil = new Border { Opacity = 0.45, IsHitTestVisible = false };
        Theme(backdropVeil, Border.BackgroundProperty, "NIRABackdropVeilBrush");
        Grid.SetRowSpan(backdropVeil, 3);
        outer.Children.Add(backdropVeil);

        ApplyThemeArtwork(NIRAThemeManager.CurrentMode);
        NIRAThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => NIRAThemeManager.ThemeChanged -= OnThemeChanged;

        // Completely custom, draggable ELVARA/NIRA title strip: no native white title bar.
        var chrome = new Grid { Margin = new Thickness(16, 0, 14, 0) };
        chrome.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        chrome.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        chrome.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        chrome.MouseLeftButtonDown += Chrome_MouseLeftButtonDown;
        var chromeSurface = new Border { Child = chrome, BorderThickness = new Thickness(0, 0, 0, 1) };
        Theme(chromeSurface, Border.BackgroundProperty, "NIRATopBarTintBrush");
        Theme(chromeSurface, Border.BorderBrushProperty, "NIRAGlassBorderBrush");
        Grid.SetRow(chromeSurface, 0);
        outer.Children.Add(chromeSurface);

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Image
        {
            Source = Artwork("Nira-Icon.png"),
            Width = 36, Height = 36,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 11, 0),
            IsHitTestVisible = false
        });
        var wordmark = Label("NIRA", 21, "NIRATextBrush", FontWeights.Light);
        wordmark.FontFamily = new FontFamily("Bahnschrift");
        brand.Children.Add(wordmark);
        var separator = new Border { Width = 1, Height = 17, Margin = new Thickness(10, 0, 9, 0), Opacity = 0.7 };
        Theme(separator, Border.BackgroundProperty, "NIRACyanBrush");
        brand.Children.Add(separator);
        brand.Children.Add(Label("BY ELVARA", 9, "NIRASecondaryBrush", FontWeights.SemiBold));
        Grid.SetColumn(brand, 0);
        chrome.Children.Add(brand);
        var archiveTag = Label("CONVERSATION ARCHIVE   /   READ ONLY", 10, "NIRAMutedBrush", FontWeights.SemiBold);
        archiveTag.HorizontalAlignment = HorizontalAlignment.Right;
        archiveTag.VerticalAlignment = VerticalAlignment.Center;
        archiveTag.Margin = new Thickness(12, 0, 15, 0);
        Grid.SetColumn(archiveTag, 1);
        chrome.Children.Add(archiveTag);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(WindowButton("\uE921", "Minimize", () => WindowState = WindowState.Minimized));
        controls.Children.Add(WindowButton("\uE922", "Maximize / restore", ToggleMaximize));
        controls.Children.Add(WindowButton("\uE8BB", "Close past chat", Close, close: true));
        Grid.SetColumn(controls, 2);
        chrome.Children.Add(controls);

        var accent = new Border { Height = 2 };
        Theme(accent, Border.BackgroundProperty, "NIRANeonLine");
        Grid.SetRow(accent, 1);
        outer.Children.Add(accent);

        var body = new Grid { Margin = new Thickness(26, 24, 26, 20) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(body, 2);
        outer.Children.Add(body);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(Label("PAST CHAT   /   SAVED CONVERSATION", 10, "NIRACyanBrush", FontWeights.SemiBold));
        var title = Label(string.IsNullOrWhiteSpace(session.Title) ? "Past conversation" : session.Title, 23, "NIRATextBrush", FontWeights.SemiBold);
        title.MaxHeight = 75;
        title.Margin = new Thickness(0, 10, 0, 0);
        header.Children.Add(title);
        var subtitle = Label($"{session.StartedAtUtc.ToLocalTime():dddd, MMMM d, yyyy  ·  h:mm tt}     •     {_turns.Count} messages", 11, "NIRASecondaryBrush");
        subtitle.Margin = new Thickness(0, 9, 0, 0);
        header.Children.Add(subtitle);
        Grid.SetRow(header, 0);
        body.Children.Add(header);

        var divider = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 17) };
        Theme(divider, Border.BackgroundProperty, "NIRAGlassBorderBrush");
        Grid.SetRow(divider, 1);
        body.Children.Add(divider);

        var tools = new Grid { Margin = new Thickness(0, 0, 0, 15) };
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search = new TextBox
        {
            Height = 40, Padding = new Thickness(14, 9, 14, 8), FontSize = 12.5,
            BorderThickness = new Thickness(1), Template = RoundedSearchTemplate(),
            ToolTip = "Find words in this saved conversation (Ctrl+F)"
        };
        Theme(_search, Control.BackgroundProperty, "NIRAInputTintBrush");
        Theme(_search, Control.ForegroundProperty, "NIRATextBrush");
        Theme(_search, Control.BorderBrushProperty, "NIRAGlassBorderBrush");
        Theme(_search, TextBoxBase.SelectionBrushProperty, "NIRASelectionBrush");
        Theme(_search, TextBoxBase.CaretBrushProperty, "NIRACyanBrush");
        _search.GotKeyboardFocus += (_, _) => Theme(_search, Control.BorderBrushProperty, "NIRAFocusBorderBrush");
        _search.LostKeyboardFocus += (_, _) => Theme(_search, Control.BorderBrushProperty, "NIRAGlassBorderBrush");
        _searchHint = Label("Search messages (Ctrl+F)", 12.5, "NIRAMutedBrush");
        _searchHint.Margin = new Thickness(14, 0, 0, 0);
        _searchHint.VerticalAlignment = VerticalAlignment.Center;
        _searchHint.IsHitTestVisible = false;
        _search.TextChanged += (_, _) =>
        {
            _searchHint.Visibility = string.IsNullOrEmpty(_search.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            RefreshMatches();
        };
        var searchSurface = new Grid();
        searchSurface.Children.Add(_search);
        searchSurface.Children.Add(_searchHint);
        Grid.SetColumn(searchSurface, 0);
        tools.Children.Add(searchSurface);
        _searchStatus = Label($"{_turns.Count} turns", 10, "NIRAMutedBrush");
        _searchStatus.VerticalAlignment = VerticalAlignment.Center;
        _searchStatus.TextAlignment = TextAlignment.Right;
        _searchStatus.Margin = new Thickness(12, 0, 9, 0);
        _searchStatus.MinWidth = 52;
        Grid.SetColumn(_searchStatus, 1);
        tools.Children.Add(_searchStatus);
        var prev = ActionButton("↑", false, "Previous match (Shift+Enter)");
        prev.Click += (_, _) => MoveMatch(-1);
        prev.Width = 40;
        prev.Height = 40;
        prev.Padding = new Thickness(0);
        prev.FontSize = 17;
        prev.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(prev, 2);
        tools.Children.Add(prev);
        var next = ActionButton("↓", false, "Next match (Enter)");
        next.Click += (_, _) => MoveMatch(1);
        next.Width = 40;
        next.Height = 40;
        next.Padding = new Thickness(0);
        next.FontSize = 17;
        next.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(next, 3);
        tools.Children.Add(next);
        Grid.SetRow(tools, 2);
        body.Children.Add(tools);

        var transcriptPanel = Panel("NIRAGlassTintBrush", "NIRAGlassBorderBrush", 13);
        transcriptPanel.Padding = new Thickness(16, 15, 9, 10);
        _scroll = new ScrollViewer
        {
            Content = _messages,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent
        };
        transcriptPanel.Child = _scroll;
        Grid.SetRow(transcriptPanel, 3);
        body.Children.Add(transcriptPanel);

        // Two-row footer stays inside the window even at minimum width/height.
        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var note = Label("This chat is saved evidence. Attaching it will not resume its old tasks or permissions.", 10, "NIRAMutedBrush");
        note.Margin = new Thickness(0, 0, 0, 11);
        footer.Children.Add(note);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copy = ActionButton("Copy transcript", false, "Copy the full saved conversation");
        copy.MinWidth = 136;
        copy.Click += (_, _) => CopyTranscript();
        buttons.Children.Add(copy);
        var close = ActionButton("Close", false, "Close archive");
        close.MinWidth = 78;
        close.Margin = new Thickness(9, 0, 0, 0);
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        var attach = ActionButton("Attach as source", true, "Attach this chat as bounded evidence to your next message");
        attach.MinWidth = 153;
        attach.Margin = new Thickness(9, 0, 0, 0);
        attach.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(attach);
        Grid.SetRow(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 4);
        body.Children.Add(footer);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _search.Focus(); _search.SelectAll(); e.Handled = true;
            }
            else if (e.Key == Key.Enter && _search.IsKeyboardFocusWithin)
            {
                MoveMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                e.Handled = true;
            }
        };
        RefreshMatches();
    }

    private void OnThemeChanged(NIRAThemeMode mode)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyThemeArtwork(mode)));
            return;
        }
        ApplyThemeArtwork(mode);
    }

    private void ApplyThemeArtwork(NIRAThemeMode mode)
    {
        bool halo = mode == NIRAThemeMode.Halo;
        _backgroundEclipse.Opacity = halo ? 0 : 1;
        _backgroundHalo.Opacity = halo ? 1 : 0;
    }

    private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? current = e.OriginalSource as DependencyObject;
             current is not null;)
        {
            if (current is ButtonBase || current is TextBox) return;
            if (ReferenceEquals(current, sender)) break;
            current = current is FrameworkContentElement content
                ? content.Parent
                : VisualTreeHelper.GetParent(current);
        }
        if (e.ClickCount == 2) ToggleMaximize();
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { /* Mouse released mid-drag. */ }
        }
        e.Handled = true;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal : WindowState.Maximized;

    private void RefreshMatches()
    {
        _messages.Children.Clear();
        _matchingBubbles.Clear();
        string query = _search.Text.Trim();
        foreach (NIRAArchivedChatTurn turn in _turns)
        {
            bool matched = query.Length > 0 &&
                turn.Content.Contains(query, StringComparison.OrdinalIgnoreCase);
            Border bubble = CreateBubble(turn, query, matched);
            _messages.Children.Add(bubble);
            if (matched) _matchingBubbles.Add(bubble);
        }
        _matchIndex = 0;
        UpdateMatchStatus();
        if (_matchingBubbles.Count > 0)
            Dispatcher.BeginInvoke(new Action(ScrollToActiveMatch));
    }

    private Border CreateBubble(NIRAArchivedChatTurn turn, string query, bool matched)
    {
        bool user = turn.Role.Equals("user", StringComparison.OrdinalIgnoreCase);
        var bubble = Panel(user ? "NIRAChipBrush" : "NIRAGlassStrongTintBrush",
            matched ? "NIRAFocusBorderBrush" : "NIRAGlassBorderBrush");
        bubble.Padding = new Thickness(15, 12, 15, 14);
        bubble.Margin = new Thickness(user ? 48 : 0, 0, user ? 0 : 48, 11);
        var content = new StackPanel();
        var meta = new DockPanel { LastChildFill = true };
        var copy = ActionButton("COPY", false, "Copy this message");
        copy.FontSize = 10;
        copy.MinHeight = 29;
        copy.MinWidth = 57;
        copy.Padding = new Thickness(8, 4, 8, 4);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(turn.Content); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Copy failed"); }
        };
        DockPanel.SetDock(copy, Dock.Right);
        meta.Children.Add(copy);
        var metaLabel = Label($"{(user ? "YOU" : "NIRA")}   ·   {turn.OccurredAtUtc.ToLocalTime():MMM d, h:mm tt}",
            10, user ? "NIRACyanBrush" : "NIRASecondaryBrush", FontWeights.SemiBold);
        metaLabel.VerticalAlignment = VerticalAlignment.Center;
        meta.Children.Add(metaLabel);
        content.Children.Add(meta);
        var message = Label("", 12.5, "NIRATextBrush");
        message.LineHeight = 20;
        message.Margin = new Thickness(0, 10, 0, 0);
        AppendHighlighted(message, turn.Content, query);
        content.Children.Add(message);
        if (!user)
        {
            NIRAPresentationSnapshot? presentation = _archive.ReadPresentation(turn.MessageId);
            if (presentation is { Blocks.Count: > 0 })
            {
                IReadOnlyDictionary<int, NIRARichInteractionState> states =
                    _archive.ReadInteractiveStates(turn.MessageId);
                for (int i = 0; i < presentation.Blocks.Count;)
                {
                    if (presentation.Blocks[i].Type == "metric")
                    {
                        var metrics = new List<NIRARichBlock>();
                        while (i < presentation.Blocks.Count && presentation.Blocks[i].Type == "metric")
                            metrics.Add(presentation.Blocks[i++]);
                        content.Children.Add(NIRARichBlockRenderer.RenderMetricGroup(metrics));
                        continue;
                    }
                    int blockIndex = i;
                    var block = presentation.Blocks[i++];
                    states.TryGetValue(blockIndex, out var state);
                    content.Children.Add(NIRARichBlockRenderer.Render(block, state, updated =>
                    {
                        try { _archive.SaveInteractiveState(turn.MessageId, blockIndex, updated); }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[PastChat] UI_STATE_SAVE_FAILED | {ex.GetType().Name}");
                        }
                    }));
                }
            }
        }
        bubble.Child = content;
        return bubble;
    }

    private static void AppendHighlighted(TextBlock target, string value, string query)
    {
        if (string.IsNullOrEmpty(query)) { target.Text = value; return; }
        int pos = 0;
        while (pos < value.Length)
        {
            int match = value.IndexOf(query, pos, StringComparison.OrdinalIgnoreCase);
            if (match < 0) { target.Inlines.Add(new Run(value[pos..])); break; }
            if (match > pos) target.Inlines.Add(new Run(value[pos..match]));
            var run = new Run(value.Substring(match, query.Length)) { FontWeight = FontWeights.SemiBold };
            Theme(run, TextElement.BackgroundProperty, "NIRASelectionBrush");
            Theme(run, TextElement.ForegroundProperty, "NIRATextBrush");
            target.Inlines.Add(run);
            pos = match + query.Length;
        }
    }

    private void MoveMatch(int direction)
    {
        if (_matchingBubbles.Count == 0) return;
        _matchIndex = (_matchIndex + direction + _matchingBubbles.Count) % _matchingBubbles.Count;
        UpdateMatchStatus();
        ScrollToActiveMatch();
    }

    private void UpdateMatchStatus()
    {
        _searchStatus.Text = string.IsNullOrWhiteSpace(_search.Text)
            ? $"{_turns.Count} turns"
            : _matchingBubbles.Count == 0 ? "No matches"
            : $"{_matchIndex + 1} / {_matchingBubbles.Count}";
    }

    private void ScrollToActiveMatch()
    {
        if (_matchingBubbles.Count == 0) return;
        Border bubble = _matchingBubbles[_matchIndex];
        _scroll.UpdateLayout();
        if (!bubble.IsLoaded) return;
        double y = bubble.TranslatePoint(new Point(0, 0), _messages).Y;
        _scroll.ScrollToVerticalOffset(Math.Max(0, y - 45));
    }

    private void CopyTranscript()
    {
        var text = new StringBuilder();
        foreach (NIRAArchivedChatTurn turn in _turns)
            text.Append('[').Append(turn.OccurredAtUtc.ToLocalTime().ToString("g"))
                .Append("] ").Append(turn.Role).Append(": ")
                .AppendLine(turn.Content).AppendLine();
        try { Clipboard.SetText(text.ToString()); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Copy failed"); }
    }
}



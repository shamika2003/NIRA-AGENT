using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIRAAgent.Branches;
using NIRAAgent.Goals;
using NIRAAgent.Self.Model;

namespace NIRAAgent.UI.Goals;

// Read-only workspace projection. It never changes a goal/branch/commitment,
// creates an authority grant, or dispatches an agent task. A user-initiated
// continuation is drafted in Chat, where normal permissions still apply.
public sealed class NIRAGoalsPage : UserControl
{
    private readonly StackPanel _cards = new() { Orientation = Orientation.Vertical };
    private readonly TextBlock _counts = new() { FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _includeFinished = new()
    {
        Content = "Include completed / cancelled",
        FontSize = 11.5,
        VerticalAlignment = VerticalAlignment.Center
    };
    private NIRAGoalService? _goals;
    private NIRABranchService? _branches;
    private NIRABranchWorkService? _work;
    private NIRASelfModelService? _self;

    public event Action<string>? DraftRequested;

    public NIRAGoalsPage()
    {
        var root = new Grid { Margin = new Thickness(12, 6, 12, 4) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel { Margin = new Thickness(10, 10, 10, 20) };
        heading.Children.Add(Label("GOALS  /  PROJECTS", 11, "NIRACyanBrush", true));
        heading.Children.Add(Label("Saved work, ready when you are", 27, "NIRATextBrush", true));
        heading.Children.Add(Label("Projects, unfinished stages and commitments stay here. Only work actually running appears in Chat.",
            12, "NIRASecondaryBrush"));
        Resource(_counts, TextBlock.ForegroundProperty, "NIRASecondaryBrush");
        heading.Children.Add(_counts);

        var toolbar = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        Resource(_includeFinished, Control.ForegroundProperty, "NIRATextBrush");
        _includeFinished.Checked += (_, _) => Refresh();
        _includeFinished.Unchecked += (_, _) => Refresh();
        DockPanel.SetDock(_includeFinished, Dock.Left);
        toolbar.Children.Add(_includeFinished);
        var refresh = ActionButton("Refresh", () => Refresh());
        refresh.Margin = new Thickness(15, 0, 0, 0);
        DockPanel.SetDock(refresh, Dock.Right);
        toolbar.Children.Add(refresh);
        heading.Children.Add(toolbar);
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 0, 16, 16)
        };
        scroll.Content = _cards;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
    }

    public void Attach(NIRAGoalService goals, NIRABranchService branches,
        NIRABranchWorkService work, NIRASelfModelService self)
    {
        Detach();
        _goals = goals;
        _branches = branches;
        _work = work;
        _self = self;
        _goals.StateChanged += StateChanged;
        _branches.StateChanged += StateChanged;
        _work.StateChanged += StateChanged;
        _self.StateChanged += StateChanged;
        Refresh();
    }

    public void Detach()
    {
        if (_goals is not null) _goals.StateChanged -= StateChanged;
        if (_branches is not null) _branches.StateChanged -= StateChanged;
        if (_work is not null) _work.StateChanged -= StateChanged;
        if (_self is not null) _self.StateChanged -= StateChanged;
        _goals = null;
        _branches = null;
        _work = null;
        _self = null;
    }

    private void StateChanged()
    {
        if (Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Visibility == Visibility.Visible) Refresh();
            }));
            return;
        }
        if (Visibility == Visibility.Visible) Refresh();
    }

    // One authoritative policy used by the actual Chat sidebar and the
    // offline acceptance test. A passive Active/Waiting branch with no queued
    // or executing assignment is NOT current work.
    public static NIRABranchState[] LiveBranches(
        IEnumerable<NIRABranchState> branches, IEnumerable<NIRABranchWorkItem> work)
    {
        HashSet<Guid> live = work
            .Where(w => w.Status is NIRABranchWorkStatus.Pending or NIRABranchWorkStatus.Running)
            .Select(w => w.BranchId)
            .ToHashSet();
        return branches.Where(b => b.IsOpen &&
                b.Status is not (NIRABranchStatus.Waiting or NIRABranchStatus.Blocked) &&
                live.Contains(b.Id))
            .OrderByDescending(b => work.Any(w => w.BranchId == b.Id &&
                w.Status == NIRABranchWorkStatus.Running))
            .ThenByDescending(b => b.Priority)
            .ThenByDescending(b => b.UpdatedAt)
            .ToArray();
    }

    public static int RunWorkspaceSelfCheck()
    {
        var active = new NIRABranchState { Id = Guid.NewGuid(), GoalId = Guid.NewGuid(),
            Status = NIRABranchStatus.Active, Objective = "Active" };
        var waiting = active with { Id = Guid.NewGuid(), Status = NIRABranchStatus.Waiting };
        var blocked = active with { Id = Guid.NewGuid(), Status = NIRABranchStatus.Blocked };
        var passive = active with { Id = Guid.NewGuid() };
        var resolved = active with { Id = Guid.NewGuid(), Status = NIRABranchStatus.Completed };
        var queued = active with { Id = Guid.NewGuid() };
        var assignments = new[]
        {
            new NIRABranchWorkItem { BranchId = active.Id, Status = NIRABranchWorkStatus.Running },
            new NIRABranchWorkItem { BranchId = waiting.Id, Status = NIRABranchWorkStatus.Running },
            new NIRABranchWorkItem { BranchId = blocked.Id, Status = NIRABranchWorkStatus.Pending },
            new NIRABranchWorkItem { BranchId = resolved.Id, Status = NIRABranchWorkStatus.Running },
            new NIRABranchWorkItem { BranchId = queued.Id, Status = NIRABranchWorkStatus.Pending }
        };
        var result = LiveBranches(new[] { passive, waiting, resolved, active, blocked, queued }, assignments);
        if (result.Length != 2 || result[0].Id != active.Id || result[1].Id != queued.Id)
            throw new InvalidOperationException("Chat live-work filtering includes passive or terminal branches.");
        if (LiveBranches(new[] { active }, new[] {
            new NIRABranchWorkItem { BranchId = active.Id, Status = NIRABranchWorkStatus.Succeeded } }).Length != 0)
            throw new InvalidOperationException("Finished work was displayed as running.");
        if (LiveBranches(new[] { active }, Array.Empty<NIRABranchWorkItem>()).Length != 0)
            throw new InvalidOperationException("A branch without work was displayed as running.");
        return 4;
    }

    public void Refresh()
    {
        _cards.Children.Clear();
        if (_goals is null || _branches is null || _work is null || _self is null)
        {
            _counts.Text = "Saved work is not connected yet.";
            return;
        }

        bool showFinished = _includeFinished.IsChecked == true;
        var allGoals = _goals.CurrentGoals;
        var allBranches = _branches.CurrentBranches;
        var allWork = _work.CurrentWork;
        var allCommitments = _self.CurrentCommitments;
        var displayGoals = allGoals.Where(g => showFinished || g.IsOpen)
            .OrderByDescending(g => g.IsOpen)
            .ThenByDescending(g => g.Priority)
            .ThenByDescending(g => g.UpdatedAt)
            .ToArray();
        var displayCommitments = allCommitments.Where(c => showFinished || c.IsActive)
            .OrderByDescending(c => c.IsActive)
            .ThenByDescending(c => c.UpdatedAt)
            .ToArray();
        _counts.Text = $"{allGoals.Count(g => g.IsOpen)} open goals  ·  " +
            $"{allBranches.Count(b => b.IsOpen)} unfinished stages  ·  " +
            $"{allCommitments.Count(c => c.IsActive)} commitments";

        _cards.Children.Add(SectionHeading("PROJECTS AND STAGES"));
        if (displayGoals.Length == 0)
            _cards.Children.Add(Empty("No saved projects in this view."));
        foreach (var goal in displayGoals)
            _cards.Children.Add(GoalCard(goal,
                allBranches.Where(b => b.GoalId == goal.Id).ToArray(), allWork));

        _cards.Children.Add(SectionHeading("COMMITMENTS"));
        if (displayCommitments.Length == 0)
            _cards.Children.Add(Empty("No saved commitments in this view."));
        foreach (var commitment in displayCommitments)
            _cards.Children.Add(CommitmentCard(commitment));
    }

    private UIElement GoalCard(NIRAGoalState goal, NIRABranchState[] branches,
        IReadOnlyList<NIRABranchWorkItem> work)
    {
        var body = new StackPanel();
        var header = new DockPanel { LastChildFill = true };
        var badge = StatusBadge(goal.Status.ToString());
        DockPanel.SetDock(badge, Dock.Right);
        header.Children.Add(badge);
        header.Children.Add(Label(goal.Objective, 17, "NIRATextBrush", true));
        body.Children.Add(header);
        body.Children.Add(Label($"{branches.Count(b => b.IsOpen)} unfinished / {branches.Length} total stages  ·  " +
            $"Updated {goal.UpdatedAt.ToLocalTime():dd MMM yyyy}", 11, "NIRASecondaryBrush"));
        string detail = goal.Status == NIRAGoalStatus.Blocked ? goal.Blocker ?? "" :
            goal.Status == NIRAGoalStatus.Waiting ? goal.WaitingFor ?? "" : "";
        if (!string.IsNullOrWhiteSpace(detail))
            body.Children.Add(Label(detail, 11.5, "NIRASecondaryBrush"));

        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 5) };
        actions.Children.Add(ActionButton("Continue in Chat", () => DraftRequested?.Invoke(
            $"Nira, let's continue the saved project: {goal.Objective}\n" +
            $"Saved goal ID: {goal.Id:D}. Please review the latest state of its " +
            "unfinished stages and help me choose the next step. Don't start background work just from opening this plan.")));
        body.Children.Add(actions);
        if (branches.Length != 0)
        {
            body.Children.Add(Label("STAGES", 10, "NIRACyanBrush", true));
            foreach (var stage in branches.OrderBy(b => b.IsResolved).ThenByDescending(b => b.Priority))
                body.Children.Add(BranchRow(goal, stage, work));
        }
        return Card(body);
    }

    private UIElement BranchRow(NIRAGoalState goal, NIRABranchState stage,
        IReadOnlyList<NIRABranchWorkItem> work)
    {
        string activity = work.Any(w => w.BranchId == stage.Id &&
            w.Status == NIRABranchWorkStatus.Running) ? " · RUNNING NOW" :
            work.Any(w => w.BranchId == stage.Id &&
            w.Status == NIRABranchWorkStatus.Pending) ? " · QUEUED" : "";
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(Label(stage.Objective, 12.5, "NIRATextBrush", true));
        text.Children.Add(Label(stage.Status.ToString().ToUpperInvariant() + activity,
            10.5, "NIRASecondaryBrush"));
        string detail = stage.Status == NIRABranchStatus.Blocked ? stage.Blocker ?? "" :
            stage.Status == NIRABranchStatus.Waiting ? stage.WaitingFor ?? "" : "";
        if (!string.IsNullOrWhiteSpace(detail))
            text.Children.Add(Label(detail, 10.5, "NIRASecondaryBrush"));
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);
        var action = ActionButton(stage.IsResolved ? "Discuss" : "Continue", () => DraftRequested?.Invoke(
            $"Nira, I'd like to revisit the saved project '{goal.Objective}' " +
            $"(goal ID {goal.Id:D}), specifically stage '{stage.Objective}' " +
            $"(branch ID {stage.Id:D}). Review its current state with me and " +
            "help me take the next appropriate step. Don't start background work merely because I opened the plan."));
        action.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(action, 1);
        grid.Children.Add(action);
        var border = new Border { Padding = new Thickness(12, 9, 12, 9),
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        Resource(border, Border.BackgroundProperty, "NIRAGlassTintBrush");
        Resource(border, Border.BorderBrushProperty, "NIRAGlassBorderBrush");
        border.Child = grid;
        return border;
    }

    private UIElement CommitmentCard(NIRACommitmentState commitment)
    {
        var body = new StackPanel();
        var header = new DockPanel { LastChildFill = true };
        var badge = StatusBadge(commitment.Status.ToString());
        DockPanel.SetDock(badge, Dock.Right);
        header.Children.Add(badge);
        header.Children.Add(Label(commitment.Summary, 14.5, "NIRATextBrush", true));
        body.Children.Add(header);
        if (commitment.Temporal?.NextWakeAtUtc is DateTimeOffset wake)
            body.Children.Add(Label("Next scheduled review: " + wake.ToLocalTime().ToString("dd MMM yyyy, h:mm tt"),
                11, "NIRASecondaryBrush"));
        if (!string.IsNullOrWhiteSpace(commitment.LastReason))
            body.Children.Add(Label(commitment.LastReason, 11, "NIRASecondaryBrush"));
        var button = ActionButton("Discuss in Chat", () => DraftRequested?.Invoke(
            $"Nira, let's review our saved commitment '{commitment.Summary}' " +
            $"(commitment ID {commitment.Id:D}). Tell me its current status and " +
            "discuss what should happen next before taking any new action."));
        button.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(button);
        return Card(body);
    }

    private static UIElement Card(UIElement content)
    {
        var border = new Border
        {
            Padding = new Thickness(19, 16, 19, 18),
            Margin = new Thickness(0, 0, 0, 14),
            CornerRadius = new CornerRadius(17),
            BorderThickness = new Thickness(1),
            Child = content
        };
        Resource(border, Border.BackgroundProperty, "NIRAActivityCardBrush");
        Resource(border, Border.BorderBrushProperty, "NIRAGlassBorderBrush");
        return border;
    }

    private static Border StatusBadge(string status)
    {
        var border = new Border { CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1), Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(12, 0, 0, 0) };
        Resource(border, Border.BackgroundProperty, "NIRAChipBrush");
        Resource(border, Border.BorderBrushProperty, "NIRAGlassBorderBrush");
        border.Child = Label(status.ToUpperInvariant(), 10, "NIRACyanBrush", true);
        return border;
    }

    private static TextBlock SectionHeading(string title)
    {
        var heading = Label(title, 11, "NIRACyanBrush", true);
        heading.Margin = new Thickness(3, 10, 0, 13);
        return heading;
    }

    private static UIElement Empty(string message)
    {
        var label = Label(message, 12, "NIRASecondaryBrush");
        label.Margin = new Thickness(7, 6, 0, 22);
        return label;
    }

    private static TextBlock Label(string text, double size, string brush, bool bold = false)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5)
        };
        Resource(label, TextBlock.ForegroundProperty, brush);
        return label;
    }

    private static Button ActionButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label, Padding = new Thickness(12, 7, 12, 7),
            MinHeight = 30, FontSize = 11, Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Resource(button, Control.ForegroundProperty, "NIRATextBrush");
        Resource(button, Control.BackgroundProperty, "NIRAChipBrush");
        Resource(button, Control.BorderBrushProperty, "NIRAGlassBorderBrush");
        button.Click += (_, _) => action();
        return button;
    }

    private static void Resource(FrameworkElement element, DependencyProperty property, string key) =>
        element.SetResourceReference(property, key);
}

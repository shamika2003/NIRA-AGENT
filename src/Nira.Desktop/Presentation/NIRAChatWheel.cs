using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NIRAAgent.UI.Presentation;

public static class NIRAChatWheel
{
    // Tunnel at the chat root, before a nested formula/code/table viewer or
    // RichTextBox can consume the wheel. Respect genuine inner vertical scroll.
    public static void Forward(ScrollViewer chat, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
        double remaining = -e.Delta / 120.0 * 72.0;
        var viewers = new List<ScrollViewer>();
        for (DependencyObject? p = e.OriginalSource as DependencyObject; p is not null && p != chat; p = Parent(p))
            if (p is ScrollViewer viewer) viewers.Add(viewer);
        viewers.Add(chat);
        foreach (var viewer in viewers)
        {
            if (viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled || viewer.ScrollableHeight <= 0) continue;
            double current = viewer.VerticalOffset;
            double target = Math.Clamp(current + remaining, 0, viewer.ScrollableHeight);
            if (Math.Abs(target - current) > 0.01) viewer.ScrollToVerticalOffset(target);
            remaining -= target - current;
            if (Math.Abs(remaining) < 0.01) break;
        }
        e.Handled = true;
    }

    private static DependencyObject? Parent(DependencyObject node) => node switch
    {
        Visual or Visual3D => VisualTreeHelper.GetParent(node),
        FrameworkContentElement content => content.Parent ?? ContentOperations.GetParent(content),
        ContentElement content => ContentOperations.GetParent(content),
        _ => LogicalTreeHelper.GetParent(node)
    };
}

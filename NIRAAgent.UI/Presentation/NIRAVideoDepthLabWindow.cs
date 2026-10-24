using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Isolated WPF lab for the SAME production carousel implementation.
/// Running --video-deck-lab does not start the main chat/agent logic.
/// </summary>
public static class NIRAVideoDepthLabWindow
{
    private static readonly (string Title, string Url)[] Fixtures =
    {
        ("Starship · Fifth Flight Test", "https://www.youtube.com/watch?v=hl9HQfCAw64"),
        ("Booster Catch · Replay", "https://www.youtube.com/watch?v=6Px_b5eSzsA"),
        ("Launch · Alternate Footage", "https://www.youtube.com/watch?v=42QHFZgZs0j"),
        ("NASA · Space Broadcast", "https://www.youtube.com/watch?v=21X5lGlDOfg"),
        ("Card Slider · Design Reference", "https://www.youtube.com/watch?v=VOKVIArUJws")
    };

    public static Window Create()
    {
        var window = new Window
        {
            Title = "NIRA · Polished Overlapping Video Carousel · Production Renderer Lab",
            Width = 1190,
            Height = 765,
            MinWidth = 710,
            MinHeight = 530,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(5, 14, 28)),
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI")
        };
        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 24) };
        window.Content = root;

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        heading.Children.Add(new TextBlock
        {
            Text = "NIRA   /   PRODUCTION VIDEO CAROUSEL   /   LAB MODE",
            FontSize = 10.5, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(77, 193, 253))
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Cyclic Overlap · Refined",
            FontSize = 28, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 5)
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Wider exposed previews, live titles, soft distance fading, clear click targets. Same renderer used in the main NIRA chat.",
            FontSize = 12.5, Foreground = Brushes.LightSteelBlue,
            TextWrapping = TextWrapping.Wrap
        });

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(controls, Dock.Top);
        root.Children.Add(controls);
        var buttons = new List<Button>();
        var content = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 1060 };
        var narrow = false;
        int count = 5;

        void Draw()
        {
            content.Children.Clear();
            var deck = new NIRAVideoDepthDeck();
            for (int i = 0; i < count; i++)
                deck.TryAdd(new NIRARichBlock { Type = "video", Title = Fixtures[i].Title, Text = Fixtures[i].Url });
            content.Children.Add(deck.View);
            foreach (var button in buttons)
                button.Opacity = Equals(button.Tag, count) ? 1 : 0.6;
        }
        for (int n = 1; n <= Fixtures.Length; n++)
        {
            int chosen = n;
            var button = new Button
            {
                Tag = n,
                Content = n + (n == 1 ? " video" : " videos"),
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Background = new SolidColorBrush(Color.FromRgb(12, 35, 61)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(54, 119, 172)),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += (_, _) => { count = chosen; Draw(); };
            controls.Children.Add(button);
            buttons.Add(button);
        }
        var widthButton = new Button
        {
            Content = "Compact width",
            Padding = new Thickness(12, 8, 12, 8),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(12, 35, 61)),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        widthButton.Margin = new Thickness(8, 0, 0, 0);
        widthButton.Click += (_, _) =>
        {
            narrow = !narrow;
            content.MaxWidth = narrow ? 640 : 1060;
            widthButton.Content = narrow ? "Wide width" : "Compact width";
            Draw();
        };
        controls.Children.Add(widthButton);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
        root.Children.Add(scroll);
        Draw();
        return window;
    }
}

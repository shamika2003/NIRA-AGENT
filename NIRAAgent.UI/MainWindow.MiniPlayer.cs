using System;
using System.Windows;
using System.Windows.Controls;
using NIRAAgent.Diagnostics;

namespace NIRAAgent.UI;

// NIRA desktop-only mini player: never fetches URLs or creates a second media
// session. Only the vetted NIRAVideoPlayer can hand over its live WPF surface.
public partial class MainWindow
{
    private FrameworkElement? _miniPlayerSurface;
    private Func<bool>? _miniRestoreChat;
    private Action? _miniStop;
    private bool _miniAudioFocus;

    internal bool TryAttachMiniPlayer(FrameworkElement surface, string title,
        Func<bool> restoreChat, Action stop)
    {
        if (!Dispatcher.CheckAccess() || Dispatcher.HasShutdownStarted ||
            _leftSidebarCollapsed || _leftSidebarTransitioning ||
            MainWorkspace.Visibility != Visibility.Visible ||
            LeftSidebarExpandedHost.Visibility != Visibility.Visible)
            return false;

        // A new selected mini-player replaces the previous one, stopping its
        // actual provider iframe before the new one is adopted.
        if (_miniPlayerSurface != null && !ReferenceEquals(_miniPlayerSurface, surface))
        {
            _miniStop?.Invoke();
            if (_miniPlayerSurface != null) return false;
        }
        if (surface.Parent != null || _miniPlayerSurface != null) return false;

        _miniPlayerSurface = surface;
        _miniRestoreChat = restoreChat;
        _miniStop = stop;
        _miniAudioFocus = false;
        MiniPlayerAudioShade.Visibility = Visibility.Collapsed;
        MiniPlayerModeButton.Content = "♫ Audio focus";
        MiniPlayerNotice.Text = "Controls remain in the provider's video frame.";
        MiniPlayerTitle.Text = string.IsNullOrWhiteSpace(title) ? "Video" : title;
        MiniPlayerTitle.ToolTip = MiniPlayerTitle.Text;
        MiniPlayerVideoSlot.Children.Add(surface);
        MiniPlayerPanel.Visibility = Visibility.Visible;
        NIRADiagnosticTrace.Event("VIDEO-MINI", "State=Docked");
        return true;
    }

    internal bool HasMiniPlayerSurface(FrameworkElement surface) =>
        ReferenceEquals(_miniPlayerSurface, surface);

    internal void SuspendMiniPlayerSurface(FrameworkElement surface)
    {
        if (ReferenceEquals(_miniPlayerSurface, surface) &&
            MiniPlayerVideoSlot.Children.Contains(surface))
            MiniPlayerVideoSlot.Children.Remove(surface);
    }

    internal void ResumeMiniPlayerSurface(FrameworkElement surface)
    {
        if (ReferenceEquals(_miniPlayerSurface, surface) &&
            surface.Parent == null)
            MiniPlayerVideoSlot.Children.Add(surface);
    }

    internal void DetachMiniPlayer(FrameworkElement surface)
    {
        if (!ReferenceEquals(_miniPlayerSurface, surface)) return;
        if (MiniPlayerVideoSlot.Children.Contains(surface))
            MiniPlayerVideoSlot.Children.Remove(surface);
        _miniPlayerSurface = null;
        _miniRestoreChat = null;
        _miniStop = null;
        _miniAudioFocus = false;
        MiniPlayerPanel.Visibility = Visibility.Collapsed;
        MiniPlayerAudioShade.Visibility = Visibility.Collapsed;
        MiniPlayerModeButton.Content = "♫ Audio focus";
        NIRADiagnosticTrace.Event("VIDEO-MINI", "State=Undocked");
    }

    private void MiniPlayerMode_Click(object sender, RoutedEventArgs e)
    {
        if (_miniPlayerSurface == null) return;
        _miniAudioFocus = !_miniAudioFocus;
        MiniPlayerAudioShade.Visibility = _miniAudioFocus
            ? Visibility.Visible : Visibility.Collapsed;
        MiniPlayerModeButton.Content = _miniAudioFocus ? "▣ Show video" : "♫ Audio focus";
        MiniPlayerNotice.Text = _miniAudioFocus
            ? "Picture concealed, stream still active. Show video to pause or adjust volume."
            : "Use the video's own controls to pause or adjust volume.";
        NIRADiagnosticTrace.Event("VIDEO-MINI", "Mode=" + (_miniAudioFocus ? "AudioFocus" : "Video"));
    }

    private void MiniPlayerRestore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_miniRestoreChat?.Invoke() == false)
                MiniPlayerNotice.Text = "Chat card is no longer available. Close to stop playback.";
        }
        catch (Exception ex)
        {
            NIRADiagnosticTrace.Event("VIDEO-MINI", "Stage=RestoreFailed Kind=" + ex.GetType().Name);
            MiniPlayerNotice.Text = "Couldn't restore this video. Close to stop playback.";
        }
    }

    private void MiniPlayerClose_Click(object sender, RoutedEventArgs e)
    {
        try { _miniStop?.Invoke(); }
        catch (Exception ex)
        {
            NIRADiagnosticTrace.Event("VIDEO-MINI", "Stage=CloseFailed Kind=" + ex.GetType().Name);
        }
    }

    private void ReturnOrStopMiniPlayerBeforeSidebarCollapse()
    {
        if (_miniPlayerSurface == null) return;
        try
        {
            if (_miniRestoreChat?.Invoke() != true)
                _miniStop?.Invoke();
        }
        catch (Exception ex)
        {
            NIRADiagnosticTrace.Event("VIDEO-MINI", "Stage=SidebarCollapse Kind=" + ex.GetType().Name);
            _miniStop?.Invoke();
        }
    }
}

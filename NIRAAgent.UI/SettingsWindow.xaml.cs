using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using NIRAAgent.Settings;
using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI;

public partial class SettingsWindow : Window
{
    private readonly NIRARuntimeSettingsService _settings;
    private readonly NIRAWorkAreaWindowGuard _workAreaGuard;
    private bool _loading;
    private bool _closed;

    public SettingsWindow(NIRARuntimeSettingsService settings)
    {
        InitializeComponent();

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _workAreaGuard = new NIRAWorkAreaWindowGuard(this);

        VoiceEngineBox.ItemsSource = Enum.GetValues<NIRAVoiceEngineMode>();

        _settings.Changed += Settings_Changed;
        Closed += SettingsWindow_Closed;
        Loaded += SettingsWindow_Loaded;
        NIRAThemeManager.ThemeChanged += NIRAThemeManager_ThemeChanged;

        RefreshFromSettings();
        UpdateThemePresentation();
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        NIRAMotion.AnimateEntrance(RootShell, distance: 12.0, durationMs: 300);
        UpdateThemePresentation();
    }

    private void NIRAThemeManager_ThemeChanged(NIRAThemeMode mode)
    {
        if (_closed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(UpdateThemePresentation));
            return;
        }

        UpdateThemePresentation();
    }

    private void UpdateThemePresentation()
    {
        bool halo = NIRAThemeManager.CurrentMode == NIRAThemeMode.Halo;
        UtilityBackgroundEclipse.Opacity = halo ? 0.0 : 1.0;
        UtilityBackgroundHalo.Opacity = halo ? 1.0 : 0.0;
        ThemeToggleButton.Content = halo ? "HALO" : "ECLIPSE";
        CurrentThemeText.Text = halo ? "HALO" : "ECLIPSE";
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        NIRAThemeManager.ApplyTheme(
            NIRAThemeManager.CurrentMode == NIRAThemeMode.Halo
                ? NIRAThemeMode.Eclipse
                : NIRAThemeMode.Halo);
    }

    private void Settings_Changed(NIRARuntimeSettings before, NIRARuntimeSettings after)
    {
        if (_closed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshFromSettings));
            return;
        }

        RefreshFromSettings();
    }

    private void RefreshFromSettings()
    {
        _loading = true;

        try
        {
            NIRARuntimeSettings current = _settings.Current;

            VoiceEnabledCheck.IsChecked = current.VoiceEnabled;
            BackgroundVoiceCheck.IsChecked = current.SpeakBackgroundUpdates;
            BackgroundVoiceCheck.IsEnabled = current.VoiceEnabled;
            VoiceEngineBox.SelectedItem = current.VoiceEngine;
            VoiceEngineBox.IsEnabled = current.VoiceEnabled;
            VoiceVolumeSlider.Value = current.VoiceVolume * 100.0;
            VoiceVolumeSlider.IsEnabled = current.VoiceEnabled;
            VoiceVolumeText.Text = $"{Math.Round(current.VoiceVolume * 100.0):0}%";

            DesktopPresenceCheck.IsChecked = current.DesktopPresenceEnabled;
            IdleBehaviorCheck.IsChecked = current.IdleBehaviorEnabled;
            IdleAwarenessCheck.IsChecked = current.UserIdleAwarenessEnabled;
            IdleFadeCheck.IsChecked = current.FadeDesktopPresenceWhenIdle;

            bool idleControlsEnabled = current.IdleBehaviorEnabled;
            IdleAwarenessCheck.IsEnabled = idleControlsEnabled;
            IdleFadeCheck.IsEnabled = idleControlsEnabled && current.DesktopPresenceEnabled;
            IdleFadeDelayBox.IsEnabled = idleControlsEnabled
                && current.DesktopPresenceEnabled
                && current.FadeDesktopPresenceWhenIdle;

            SelectIdleFadeDelay(current.IdleFadeDelaySeconds);

            ProactiveCompanionCheck.IsChecked = current.ProactiveCompanionEnabled;
            PcContextReactionsCheck.IsChecked = current.PcContextReactionsEnabled;
            PermissionNotificationsCheck.IsChecked = current.PermissionNotificationsEnabled;
            CardParticlesCheck.IsChecked = current.WorkCardParticleTransitionsEnabled;
            ShowBranchesCheck.IsChecked = current.ShowBranchActivityInSidebar;
            ShowCommitmentsCheck.IsChecked = current.ShowCommitmentActivityInSidebar;

            StatusText.Text = "Changes are saved automatically";
        }
        finally
        {
            _loading = false;
        }
    }

    private void SelectIdleFadeDelay(int seconds)
    {
        ComboBoxItem? closest = null;
        int smallestDifference = int.MaxValue;

        foreach (object item in IdleFadeDelayBox.Items)
        {
            if (item is not ComboBoxItem comboItem ||
                !int.TryParse(comboItem.Tag?.ToString(), out int candidate))
            {
                continue;
            }

            int difference = Math.Abs(candidate - seconds);

            if (difference < smallestDifference)
            {
                smallestDifference = difference;
                closest = comboItem;
            }
        }

        IdleFadeDelayBox.SelectedItem = closest;
    }

    private void VoiceEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetVoiceEnabled(VoiceEnabledCheck.IsChecked == true);
    }

    private void BackgroundVoice_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetSpeakBackgroundUpdates(BackgroundVoiceCheck.IsChecked == true);
    }

    private void VoiceEngine_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || VoiceEngineBox.SelectedItem is not NIRAVoiceEngineMode mode) return;
        _settings.SetVoiceEngine(mode);
    }

    private void VoiceVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;

        double normalized = Math.Clamp(VoiceVolumeSlider.Value / 100.0, 0.0, 1.0);
        VoiceVolumeText.Text = $"{Math.Round(normalized * 100.0):0}%";
        _settings.SetVoiceVolume(normalized);
    }

    private void DesktopPresence_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetDesktopPresenceEnabled(DesktopPresenceCheck.IsChecked == true);
    }

    private void IdleBehavior_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetIdleBehaviorEnabled(IdleBehaviorCheck.IsChecked == true);
    }

    private void IdleAwareness_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetUserIdleAwarenessEnabled(IdleAwarenessCheck.IsChecked == true);
    }

    private void IdleFade_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetFadeDesktopPresenceWhenIdle(IdleFadeCheck.IsChecked == true);
    }

    private void IdleFadeDelay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || IdleFadeDelayBox.SelectedItem is not ComboBoxItem item) return;

        if (int.TryParse(item.Tag?.ToString(), out int seconds))
        {
            _settings.SetIdleFadeDelaySeconds(seconds);
        }
    }

    private void ProactiveCompanion_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetProactiveCompanionEnabled(ProactiveCompanionCheck.IsChecked == true);
    }

    private void PcContextReactions_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetPcContextReactionsEnabled(PcContextReactionsCheck.IsChecked == true);
    }

    private void PermissionNotifications_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetPermissionNotificationsEnabled(PermissionNotificationsCheck.IsChecked == true);
    }

    private void CardParticles_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetWorkCardParticleTransitionsEnabled(CardParticlesCheck.IsChecked == true);
    }

    private void ShowBranches_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetShowBranchActivityInSidebar(ShowBranchesCheck.IsChecked == true);
    }

    private void ShowCommitments_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetShowCommitmentActivityInSidebar(ShowCommitmentsCheck.IsChecked == true);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        StatusText.Text = "Settings reset to defaults";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _settings.Changed -= Settings_Changed;
        NIRAThemeManager.ThemeChanged -= NIRAThemeManager_ThemeChanged;
        Loaded -= SettingsWindow_Loaded;
        _workAreaGuard.Dispose();
        Closed -= SettingsWindow_Closed;
    }
}


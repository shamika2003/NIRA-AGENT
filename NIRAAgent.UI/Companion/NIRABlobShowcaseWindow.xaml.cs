/*
 * filename: NIRABlobShowcaseWindow.xaml.cs
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using NIRAAgent.Embodiment;
using NIRAAgent.UI.Companion.Particles;

namespace NIRAAgent.UI.Companion;

public partial class NIRABlobShowcaseWindow : Window
{
    private readonly DispatcherTimer _speakingTimer =
        new DispatcherTimer()
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };

    private ParticleEntityControl? _speakControl;

    private int _speakFrameIndex;

    private static readonly NIRAVisualIntent[] SpeakingFrames =
    {
        NIRABlobPresetLibrary.Get(NIRABlobPresetId.Speak),

        NIRABlobPresetLibrary.Get(NIRABlobPresetId.Speak) with
        {
            Energy = 0.64,
            Flow = 0.78,
            Pulse = 0.62,
            Presence = 0.86
        },

        NIRABlobPresetLibrary.Get(NIRABlobPresetId.Speak) with
        {
            Energy = 0.50,
            Flow = 0.58,
            Pulse = 0.42,
            Presence = 0.76
        },

        NIRABlobPresetLibrary.Get(NIRABlobPresetId.Speak) with
        {
            Energy = 0.70,
            Flow = 0.84,
            Pulse = 0.68,
            Presence = 0.90,
            Tension = 0.28
        }
    };

    public NIRABlobShowcaseWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Closed += OnClosed;
        _speakingTimer.Tick += OnSpeakingTick;
    }

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        CreateBlob(CoreHost, NIRABlobPresetId.Core);
        CreateBlob(FlowHost, NIRABlobPresetId.Flow);
        CreateBlob(FocusHost, NIRABlobPresetId.Focus);
        _speakControl = CreateBlob(SpeakHost, NIRABlobPresetId.Speak);
        CreateBlob(WarmHost, NIRABlobPresetId.Warm);
        CreateBlob(AlertHost, NIRABlobPresetId.Alert);
        CreateBlob(CalmHost, NIRABlobPresetId.Calm);
        CreateBlob(CustomHost, NIRABlobPresetId.Custom);

        _speakingTimer.Start();
    }

    private void OnClosed(
        object? sender,
        EventArgs e)
    {
        _speakingTimer.Stop();
        _speakingTimer.Tick -= OnSpeakingTick;
    }

    private ParticleEntityControl CreateBlob(
        ContentControl host,
        NIRABlobPresetId preset)
    {
        ParticleEntityControl control =
            new ParticleEntityControl()
            {
                Width = 220,
                Height = 220,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

        control.SetIntent(NIRABlobPresetLibrary.Get(preset));
        host.Content = control;
        return control;
    }

    private void OnSpeakingTick(
        object? sender,
        EventArgs e)
    {
        if (_speakControl == null)
        {
            return;
        }

        _speakFrameIndex = (_speakFrameIndex + 1) % SpeakingFrames.Length;

        _speakControl.SetIntent(
            SpeakingFrames[_speakFrameIndex].Normalize());
    }

    private void CycleSpeakingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_speakingTimer.IsEnabled)
        {
            _speakingTimer.Stop();
            CycleSpeakingButton.Content = "Resume Speaking";
        }
        else
        {
            _speakingTimer.Start();
            CycleSpeakingButton.Content = "Pause Speaking";
        }
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}


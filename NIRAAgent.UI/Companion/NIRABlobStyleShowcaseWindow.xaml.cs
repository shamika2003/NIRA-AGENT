/*
 * filename: NIRABlobStyleShowcaseWindow.xaml.cs
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using NIRAAgent.Embodiment;
using NIRAAgent.UI.Companion.Particles;

namespace NIRAAgent.UI.Companion;

public partial class NIRABlobStyleShowcaseWindow
    : Window
{
    private readonly DispatcherTimer _demoTimer =
        new DispatcherTimer()
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };

    private readonly List<ParticleEntityControl> _animatedControls =
        new List<ParticleEntityControl>();

    private int _frame;

    public NIRABlobStyleShowcaseWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Closed += OnClosed;
        _demoTimer.Tick += OnDemoTick;
    }

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        _animatedControls.Add(
            CreateBlob(
                EclipseHost,
                NIRABlobStyleId.EclipseGlass,
                NIRABlobPresetId.Core));

        _animatedControls.Add(
            CreateBlob(
                HaloHost,
                NIRABlobStyleId.HaloBloom,
                NIRABlobPresetId.Warm));

        _animatedControls.Add(
            CreateBlob(
                OrbitHost,
                NIRABlobStyleId.OrbitFlow,
                NIRABlobPresetId.Flow));

        _animatedControls.Add(
            CreateBlob(
                LatticeHost,
                NIRABlobStyleId.LatticeCore,
                NIRABlobPresetId.Focus));

        _animatedControls.Add(
            CreateBlob(
                NebulaHost,
                NIRABlobStyleId.NebulaPulse,
                NIRABlobPresetId.Speak));

        _animatedControls.Add(
            CreateBlob(
                MinimalHost,
                NIRABlobStyleId.QuietMinimal,
                NIRABlobPresetId.Calm));

        _demoTimer.Start();
    }

    private void OnClosed(
        object? sender,
        EventArgs e)
    {
        _demoTimer.Stop();
        _demoTimer.Tick -= OnDemoTick;
    }

    private ParticleEntityControl CreateBlob(
        ContentControl host,
        NIRABlobStyleId styleId,
        NIRABlobPresetId presetId)
    {
        ParticleEntityControl control =
            new ParticleEntityControl()
            {
                Width = 250,
                Height = 250,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

        _ = styleId;

        control.SetIntent(
            NIRABlobPresetLibrary.Get(presetId));

        host.Content = control;
        return control;
    }

    private void OnDemoTick(
        object? sender,
        EventArgs e)
    {
        _frame++;

        Apply(
            _animatedControls.ElementAtOrDefault(0),
            NIRABlobPresetId.Core,
            0.01);

        Apply(
            _animatedControls.ElementAtOrDefault(1),
            NIRABlobPresetId.Warm,
            0.03);

        Apply(
            _animatedControls.ElementAtOrDefault(2),
            NIRABlobPresetId.Flow,
            0.05);

        Apply(
            _animatedControls.ElementAtOrDefault(3),
            NIRABlobPresetId.Focus,
            0.02);

        Apply(
            _animatedControls.ElementAtOrDefault(4),
            NIRABlobPresetId.Speak,
            0.08);

        Apply(
            _animatedControls.ElementAtOrDefault(5),
            NIRABlobPresetId.Calm,
            0.01);
    }

    private void Apply(
        ParticleEntityControl? control,
        NIRABlobPresetId presetId,
        double extraPulse)
    {
        if (control == null)
        {
            return;
        }

        NIRAVisualIntent intent =
            NIRABlobPresetLibrary.Get(presetId);

        double t =
            _frame * 0.24;

        intent = intent with
        {
            Pulse = Math.Clamp(
                intent.Pulse + Math.Sin(t) * extraPulse,
                0.0,
                1.0),

            Flow = Math.Clamp(
                intent.Flow + Math.Cos(t * 0.7) * extraPulse,
                0.0,
                1.0),

            Presence = Math.Clamp(
                intent.Presence + Math.Sin(t * 0.5) * extraPulse,
                0.0,
                1.0)
        };

        control.SetIntent(intent.Normalize());
    }

    private void AnimateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_demoTimer.IsEnabled)
        {
            _demoTimer.Stop();
            AnimateButton.Content = "Resume Demo";
        }
        else
        {
            _demoTimer.Start();
            AnimateButton.Content = "Pause Demo";
        }
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}


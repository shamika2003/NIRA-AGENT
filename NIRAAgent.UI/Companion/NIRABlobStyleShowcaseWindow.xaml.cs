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
                NIRABlobStyleId.EclipseGlass));

        _animatedControls.Add(
            CreateBlob(
                HaloHost,
                NIRABlobStyleId.HaloBloom));

        _animatedControls.Add(
            CreateBlob(
                OrbitHost,
                NIRABlobStyleId.OrbitFlow));

        _animatedControls.Add(
            CreateBlob(
                LatticeHost,
                NIRABlobStyleId.LatticeCore));

        _animatedControls.Add(
            CreateBlob(
                NebulaHost,
                NIRABlobStyleId.NebulaPulse));

        _animatedControls.Add(
            CreateBlob(
                MinimalHost,
                NIRABlobStyleId.QuietMinimal));

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
        NIRABlobStyleId styleId)
    {
        ParticleEntityControl control =
            new ParticleEntityControl()
            {
                Width = 250,
                Height = 250,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

        // The catalog is the single source of truth for style -> preview preset.
        control.SetIntent(
            NIRABlobPresetLibrary.Get(
                NIRABlobStyleCatalog.Get(styleId).PreviewPreset));

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
            NIRABlobStyleId.EclipseGlass,
            0.01);

        Apply(
            _animatedControls.ElementAtOrDefault(1),
            NIRABlobStyleId.HaloBloom,
            0.03);

        Apply(
            _animatedControls.ElementAtOrDefault(2),
            NIRABlobStyleId.OrbitFlow,
            0.05);

        Apply(
            _animatedControls.ElementAtOrDefault(3),
            NIRABlobStyleId.LatticeCore,
            0.02);

        Apply(
            _animatedControls.ElementAtOrDefault(4),
            NIRABlobStyleId.NebulaPulse,
            0.08);

        Apply(
            _animatedControls.ElementAtOrDefault(5),
            NIRABlobStyleId.QuietMinimal,
            0.01);
    }

    private void Apply(
        ParticleEntityControl? control,
        NIRABlobStyleId styleId,
        double extraPulse)
    {
        if (control == null)
        {
            return;
        }

        NIRAVisualIntent intent =
            NIRABlobPresetLibrary.Get(
                NIRABlobStyleCatalog.Get(styleId).PreviewPreset);

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

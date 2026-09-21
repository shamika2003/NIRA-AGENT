/*
 * filename: NIRAVisualArtifactViewerWindow.xaml.cs
 */

using System.Windows;

using NIRAAgent.UI.ViewModels;
using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI.Visuals;

public partial class NIRAVisualArtifactViewerWindow : Window
{
    public NIRAVisualArtifactViewerWindow(
        VisualArtifactViewModel artifact)
    {
        ArgumentNullException.ThrowIfNull(
            artifact);

        InitializeComponent();

        DataContext =
            artifact;

        Loaded +=
            NIRAVisualArtifactViewerWindow_Loaded;
    }

    private void NIRAVisualArtifactViewerWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        NIRAMotion.AnimateEntrance(
            RootShell,
            distance: 14.0,
            durationMs: 300);
    }


    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}

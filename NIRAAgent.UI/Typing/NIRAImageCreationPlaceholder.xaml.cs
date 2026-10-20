using System.Windows;
using System.Windows.Controls;

namespace NIRAAgent.UI.Typing;

/// <summary>
/// Shared D4 Cascade + D1 Spectrum placeholder.
/// Production now uses the selected Prism Veil tile skin, while the Image UI Lab
/// can still preview other materials without changing the animation or spending quotas.
/// </summary>
public partial class NIRAImageCreationPlaceholder : UserControl
{
    public static readonly DependencyProperty TileSkinProperty = DependencyProperty.Register(
        nameof(TileSkin), typeof(NIRAImageTileSkin), typeof(NIRAImageCreationPlaceholder),
        new PropertyMetadata(NIRAImageTileSkin.PrismVeil, OnTileSkinChanged));

    public NIRAImageTileSkin TileSkin
    {
        get => (NIRAImageTileSkin)GetValue(TileSkinProperty);
        set => SetValue(TileSkinProperty, value);
    }

    public NIRAImageCreationPlaceholder()
    {
        InitializeComponent();
        ApplySkin();
    }

    private static void OnTileSkinChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is NIRAImageCreationPlaceholder placeholder)
            placeholder.ApplySkin();
    }

    private void ApplySkin()
    {
        if (Indicator is not null)
            Indicator.TileSkin = TileSkin;
    }
}

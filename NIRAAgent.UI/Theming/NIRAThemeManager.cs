using System.IO;
using System.Windows;
using System.Windows.Media;

namespace NIRAAgent.UI.Theming;

public enum NIRAThemeMode
{
    Eclipse,
    Halo
}

public static class NIRAThemeManager
{
    private static readonly string ThemeFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NIRAAgent",
        "ui-theme.txt");

    public static NIRAThemeMode CurrentMode { get; private set; } = NIRAThemeMode.Eclipse;

    public static event Action<NIRAThemeMode>? ThemeChanged;

    public static void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ThemeFilePath)!);
        ApplyTheme(LoadSavedMode(), persist: false);
    }

    public static void ApplyTheme(NIRAThemeMode mode, bool persist = true)
    {
        CurrentMode = mode;

        if (Application.Current is not null)
        {
            if (mode == NIRAThemeMode.Halo)
            {
                ApplyHalo();
            }
            else
            {
                ApplyEclipse();
            }
        }

        if (persist)
        {
            try
            {
                File.WriteAllText(ThemeFilePath, mode.ToString());
            }
            catch
            {
                // Theme persistence should never prevent NIRA from starting.
            }
        }

        ThemeChanged?.Invoke(mode);
    }

    private static NIRAThemeMode LoadSavedMode()
    {
        try
        {
            if (File.Exists(ThemeFilePath) &&
                Enum.TryParse(File.ReadAllText(ThemeFilePath).Trim(), true, out NIRAThemeMode mode))
            {
                return mode;
            }
        }
        catch
        {
            // Ignore theme persistence failures and fall back to default.
        }

        return NIRAThemeMode.Eclipse;
    }

    private static void ApplyEclipse()
    {
        SetBrush("NIRABackgroundBrush", "#050817");
        SetBrush("NIRAShellBackgroundBrush", "#081022");
        SetBrush("NIRAPanelBrush", "#C20B1226");
        SetBrush("NIRAPanelStrongBrush", "#D6111D38");
        SetBrush("NIRASurfaceBrush", "#CC0C1738");
        SetBrush("NIRASurfaceAltBrush", "#A0112149");
        SetBrush("NIRAElevatedBrush", "#F0132756");
        SetBrush("NIRABorderBrush", "#345A86C2");
        SetBrush("NIRABorderStrongBrush", "#6086B6FF");
        SetBrush("NIRAFocusBorderBrush", "#7DD7FFFF");
        SetBrush("NIRATextBrush", "#EAF2FF");
        SetBrush("NIRASecondaryBrush", "#9AA7C4");
        SetBrush("NIRAMutedBrush", "#6B7896");
        SetBrush("NIRACyanBrush", "#00E5FF");
        SetBrush("NIRABlueBrush", "#1468FF");
        SetBrush("NIRAVioletBrush", "#8B5CFF");
        SetBrush("NIRAGreenBrush", "#31D39A");
        SetBrush("NIRARedBrush", "#FF5B6E");
        SetBrush("NIRASelectionBrush", "#402C74FF");
        SetBrush("NIRAIconBrush", "#BFD7FF");
        SetBrush("NIRAGlassTintBrush", "#18091424");
        SetBrush("NIRAGlassStrongTintBrush", "#22101A30");
        SetBrush("NIRAGlassBorderBrush", "#667AAEF2");
        SetBrush("NIRAGlassBorderStrongBrush", "#7F93C3FF");
        SetBrush("NIRACardHoverBrush", "#30146EFF");
        SetBrush("NIRAChipBrush", "#20101B33");
        SetBrush("NIRAInputTintBrush", "#24101B33");
        SetBrush("NIRATopBarTintBrush", "#2009111E");
        SetBrush("NIRABackdropVeilBrush", "#0E020611");
        SetBrush("NIRAActivityCardBrush", "#220C1830");

        SetLinearGradient("NIRAWindowGradient", "#050817", "#09112A", "#0B1435");
        SetLinearGradient("NIRANeonLine", "#001468FF", "#801468FF", "#8000E5FF", "#0000E5FF");
        SetLinearGradient("NIRAButtonFill", "#1A1468FF", "#1A00E5FF");
        SetLinearGradient("NIRAButtonFillHover", "#2F1468FF", "#2600E5FF");
        SetLinearGradient("NIRAPrimaryButtonFill", "#1468FF", "#00A7FF");
        SetRadialGradient("NIRASoftGlow", "#341468FF", "#1800E5FF");
        SetRadialGradient("NIRAVioletGlow", "#308B5CFF", "#1600E5FF");
    }

    private static void ApplyHalo()
    {
        SetBrush("NIRABackgroundBrush", "#F4F7FD");
        SetBrush("NIRAShellBackgroundBrush", "#EDF3FB");
        SetBrush("NIRAPanelBrush", "#EAF3FBFF");
        SetBrush("NIRAPanelStrongBrush", "#F3FAFFFF");
        SetBrush("NIRASurfaceBrush", "#F2F7FFFF");
        SetBrush("NIRASurfaceAltBrush", "#E8F0FAFF");
        SetBrush("NIRAElevatedBrush", "#FFFFFFFF");
        SetBrush("NIRABorderBrush", "#C8D8ED");
        SetBrush("NIRABorderStrongBrush", "#9FC2F4");
        SetBrush("NIRAFocusBorderBrush", "#3C9BFF");
        SetBrush("NIRATextBrush", "#16233C");
        SetBrush("NIRASecondaryBrush", "#49627F");
        SetBrush("NIRAMutedBrush", "#8092AE");
        SetBrush("NIRACyanBrush", "#00A7D1");
        SetBrush("NIRABlueBrush", "#1468FF");
        SetBrush("NIRAVioletBrush", "#7C60F2");
        SetBrush("NIRAGreenBrush", "#1DAA74");
        SetBrush("NIRARedBrush", "#E75168");
        SetBrush("NIRASelectionBrush", "#453E8CFF");
        SetBrush("NIRAIconBrush", "#234B80");
        SetBrush("NIRAGlassTintBrush", "#52FFFFFF");
        SetBrush("NIRAGlassStrongTintBrush", "#72FFFFFF");
        SetBrush("NIRAGlassBorderBrush", "#8FB9DDF0");
        SetBrush("NIRAGlassBorderStrongBrush", "#A3C7EAF8");
        SetBrush("NIRACardHoverBrush", "#221468FF");
        SetBrush("NIRAChipBrush", "#7AFFFFFF");
        SetBrush("NIRAInputTintBrush", "#82FFFFFF");
        SetBrush("NIRATopBarTintBrush", "#90F5FAFF");
        SetBrush("NIRABackdropVeilBrush", "#06FFFFFF");
        SetBrush("NIRAActivityCardBrush", "#7AFFFFFF");

        SetLinearGradient("NIRAWindowGradient", "#F4F7FD", "#EEF4FC", "#E5F0FF");
        SetLinearGradient("NIRANeonLine", "#001468FF", "#401468FF", "#3000A7D1", "#0000A7D1");
        SetLinearGradient("NIRAButtonFill", "#091468FF", "#0900A7D1");
        SetLinearGradient("NIRAButtonFillHover", "#151468FF", "#1500A7D1");
        SetLinearGradient("NIRAPrimaryButtonFill", "#1468FF", "#3AB4FF");
        SetRadialGradient("NIRASoftGlow", "#241468FF", "#1200A7D1");
        SetRadialGradient("NIRAVioletGlow", "#247C60F2", "#1200A7D1");
    }

    private static void SetBrush(string key, string colorHex)
    {
        ResourceDictionary? owner = FindOwningDictionary(key);

        if (owner is null)
        {
            return;
        }

        Color color = (Color)ColorConverter.ConvertFromString(colorHex);

        // WPF may freeze Freezable resources loaded from XAML. Never mutate the
        // existing brush in place. Replace the resource with a fresh instance.
        owner[key] = new SolidColorBrush(color);
    }

    private static void SetLinearGradient(string key, params string[] colors)
    {
        ResourceDictionary? owner = FindOwningDictionary(key);

        if (owner is null || owner[key] is not LinearGradientBrush current)
        {
            return;
        }

        LinearGradientBrush replacement = current.CloneCurrentValue();

        for (int i = 0; i < replacement.GradientStops.Count && i < colors.Length; i++)
        {
            replacement.GradientStops[i].Color =
                (Color)ColorConverter.ConvertFromString(colors[i]);
        }

        owner[key] = replacement;
    }

    private static void SetRadialGradient(string key, string centerColor, string midColor)
    {
        ResourceDictionary? owner = FindOwningDictionary(key);

        if (owner is null ||
            owner[key] is not RadialGradientBrush current ||
            current.GradientStops.Count < 2)
        {
            return;
        }

        RadialGradientBrush replacement = current.CloneCurrentValue();

        replacement.GradientStops[0].Color =
            (Color)ColorConverter.ConvertFromString(centerColor);

        replacement.GradientStops[1].Color =
            (Color)ColorConverter.ConvertFromString(midColor);

        owner[key] = replacement;
    }

    private static ResourceDictionary? FindOwningDictionary(string key)
    {
        ResourceDictionary? root = Application.Current?.Resources;

        if (root is null)
        {
            return null;
        }

        return FindOwningDictionary(root, key);
    }

    private static ResourceDictionary? FindOwningDictionary(
        ResourceDictionary dictionary,
        object key)
    {
        // Later merged dictionaries have higher lookup precedence in WPF.
        for (int i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            ResourceDictionary? found =
                FindOwningDictionary(dictionary.MergedDictionaries[i], key);

            if (found is not null)
            {
                return found;
            }
        }

        return dictionary.Contains(key)
            ? dictionary
            : null;
    }
}

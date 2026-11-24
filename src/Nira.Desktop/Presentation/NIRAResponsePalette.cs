using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI.Presentation;

/// <summary>Screen-only colors. Model RGB values are data, never XAML, CSS or executable markup.</summary>
internal static class NIRAResponsePalette
{
    private const string Prefix = "NIRAResponse.";
    private static readonly Dictionary<string, Color> Custom = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (string Halo, string Eclipse)> Roles = new(StringComparer.Ordinal)
    {
        ["code.background"] = ("#F5F7FB", "#101827"),
        ["code.foreground"] = ("#202C40", "#E5EDF8"),
        ["code.keyword"] = ("#7136A8", "#C9A3FF"),
        ["code.string"] = ("#176445", "#92D9A5"),
        ["code.comment"] = ("#50647A", "#A2B0C1"),
        ["code.number"] = ("#934A16", "#FFBE80"),
        ["code.type"] = ("#006276", "#77DCE5"),
        ["code.function"] = ("#194F9A", "#9CC5FF"),
        ["code.operator"] = ("#845120", "#E8C493")
    };

    internal static void ApplyTheme(NIRAThemeMode mode)
    {
        if (Application.Current is not { } app) return;
        foreach (var pair in Roles)
            app.Resources[Prefix + pair.Key] = RoleBrush(pair.Key, mode);
        foreach (var pair in Custom)
            app.Resources[Prefix + pair.Key] = ReadableBrush(pair.Value, mode);
    }

    internal static void SetRole(TextElement element, DependencyProperty property, string role)
    {
        if (Application.Current is { } app)
        {
            string key = Prefix + role;
            if (!app.Resources.Contains(key)) app.Resources[key] = RoleBrush(role, NIRAThemeManager.CurrentMode);
            element.SetResourceReference(property, key);
        }
        else element.SetValue(property, RoleBrush(role, NIRAThemeManager.CurrentMode));
    }

    internal static void SetCustom(TextElement element, string hex)
    {
        if (hex.Length != 7 || hex[0] != '#' ||
            !uint.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb)) return;
        var color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        if (Application.Current is { } app)
        {
            string name = "rgb." + rgb.ToString("X6", CultureInfo.InvariantCulture);
            // Bound app-wide resources even when untrusted messages request thousands of colors.
            if (!Custom.ContainsKey(name) && Custom.Count >= 1024) return;
            if (Custom.TryAdd(name, color)) app.Resources[Prefix + name] = ReadableBrush(color, NIRAThemeManager.CurrentMode);
            if (!app.Resources.Contains(Prefix + name)) app.Resources[Prefix + name] = ReadableBrush(color, NIRAThemeManager.CurrentMode);
            element.SetResourceReference(TextElement.ForegroundProperty, Prefix + name);
        }
        else element.Foreground = ReadableBrush(color, NIRAThemeManager.CurrentMode);
    }

    private static SolidColorBrush RoleBrush(string role, NIRAThemeMode mode)
    {
        var pair = Roles[role];
        return Frozen((Color)ColorConverter.ConvertFromString(mode == NIRAThemeMode.Halo ? pair.Halo : pair.Eclipse));
    }

    internal static SolidColorBrush AccentBrush(Color requested) => ReadableBrush(requested, NIRAThemeManager.CurrentMode);

    private static SolidColorBrush ReadableBrush(Color requested, NIRAThemeMode mode)
    {
        // Keep the selected RGB exactly when readable. Otherwise blend towards a
        // contrasting neutral; background artwork varies, so this is a conservative reference.
        bool light = mode == NIRAThemeMode.Halo;
        Color reference = light ? Color.FromRgb(208, 219, 238) : Color.FromRgb(32, 44, 65);
        Color target = light ? Colors.Black : Colors.White;
        Color candidate = requested;
        for (int step = 0; step <= 20; step++)
        {
            double ratio = Contrast(candidate, reference);
            if (ratio >= 4.5) return Frozen(candidate);
            double t = (step + 1) / 20.0;
            if (t > 1) break;
            candidate = Color.FromRgb(Mix(requested.R, target.R, t), Mix(requested.G, target.G, t), Mix(requested.B, target.B, t));
        }
        return Frozen(target);
    }

    private static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
    private static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    private static double Luminance(Color c)
    {
        static double Linear(byte value)
        {
            double n = value / 255.0;
            return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
    }
    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }
}

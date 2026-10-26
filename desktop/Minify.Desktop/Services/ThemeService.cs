using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Minify.Desktop.Services;

/// <summary>A Minify theme (Minify/themes/*.css): its CSS custom properties that the native app understands.</summary>
public sealed record CssTheme(ThemeVariant Variant, IReadOnlyDictionary<string, Color> Colors);

public static partial class ThemeCss
{
    /// <summary>CSS variable -> app resources it controls.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BrushMap = new Dictionary<string, string[]>
    {
        ["--bg-primary"] = ["WindowBackgroundBrush"],
        ["--card-bg"] = ["CardBackgroundBrush"],
        ["--card-border"] = ["CardBorderBrush"],
        ["--card-preview-bg"] = ["PreviewPlaceholderBrush"],
        ["--text-muted"] = ["MutedTextBrush"],
        ["--terminal-bg"] = ["TerminalBackgroundBrush"],
        ["--terminal-text"] = ["TerminalTextBrush"],
        ["--log-error"] = ["LogErrorBrush"],
        ["--log-warning"] = ["LogWarningBrush"],
        ["--accent-text"] = ["AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed"],
    };

    public static CssTheme Parse(string css)
    {
        var colors = new Dictionary<string, Color>();
        var variant = ThemeVariant.Default;
        foreach (Match m in Declaration().Matches(css))
        {
            var name = m.Groups["name"].Value.Trim();
            var value = m.Groups["value"].Value.Trim();
            if (name == "color-scheme")
                variant = value.Contains("dark") ? ThemeVariant.Dark : value.Contains("light") ? ThemeVariant.Light : variant;
            else if (name.StartsWith("--") && TryParseColor(value, out var color))
                colors[name] = color;
        }
        return new CssTheme(variant, colors);
    }

    public static bool TryParseColor(string value, out Color color)
    {
        if (Color.TryParse(value, out color))
            return true;
        var rgba = Rgba().Match(value);
        if (!rgba.Success)
            return false;
        var alpha = rgba.Groups["a"].Success
            ? double.Parse(rgba.Groups["a"].Value, CultureInfo.InvariantCulture)
            : 1.0;
        color = Color.FromArgb(
            (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255),
            byte.Parse(rgba.Groups["r"].Value, CultureInfo.InvariantCulture),
            byte.Parse(rgba.Groups["g"].Value, CultureInfo.InvariantCulture),
            byte.Parse(rgba.Groups["b"].Value, CultureInfo.InvariantCulture));
        return true;
    }

    [GeneratedRegex(@"(?<name>--[\w-]+|color-scheme)\s*:\s*(?<value>[^;}]+)")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"rgba?\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*(?:,\s*(?<a>[\d.]+)\s*)?\)")]
    private static partial Regex Rgba();
}

public interface IThemeService
{
    bool SystemPrefersDark { get; }
    void Apply(CssTheme theme);
}

/// <summary>Overrides the app's theme resources with a CSS theme's colors.</summary>
public sealed class ThemeService : IThemeService
{
    private static readonly string[] AccentKeys =
    [
        "SystemAccentColor",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
    ];

    public bool SystemPrefersDark =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;

    public void Apply(CssTheme theme)
    {
        var app = Application.Current;
        if (app is null)
            return;
        app.RequestedThemeVariant = theme.Variant;

        foreach (var (cssVar, keys) in ThemeCss.BrushMap)
        {
            foreach (var key in keys)
            {
                // Without an override the light/dark defaults (App.axaml, Fluent) apply.
                if (theme.Colors.TryGetValue(cssVar, out var color))
                    app.Resources[key] = new SolidColorBrush(color);
                else
                    app.Resources.Remove(key);
            }
        }

        if (theme.Colors.TryGetValue("--accent", out var accent))
        {
            app.Resources["SystemAccentColor"] = accent;
            for (var i = 1; i <= 3; i++)
            {
                app.Resources[$"SystemAccentColorDark{i}"] = Shade(accent, -0.15 * i);
                app.Resources[$"SystemAccentColorLight{i}"] = Shade(accent, 0.15 * i);
            }
        }
        else
        {
            foreach (var key in AccentKeys)
                app.Resources.Remove(key);
        }
    }

    private static Color Shade(Color c, double amount)
    {
        byte Mix(byte v) => (byte)Math.Clamp(amount < 0 ? v * (1 + amount) : v + (255 - v) * amount, 0, 255);
        return Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
    }
}

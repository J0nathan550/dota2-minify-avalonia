using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Minify.Desktop.Services;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Tests;

public sealed class ThemeCssTests
{
    [Fact]
    public void Parses_variant_and_colors_from_the_bundled_dark_theme()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot.Path, "Minify", "themes", "dark.css"));

        var theme = ThemeCss.Parse(css);

        Assert.Equal(ThemeVariant.Dark, theme.Variant);
        Assert.Equal(Color.Parse("#121212"), theme.Colors["--bg-primary"]);
        Assert.Equal(Color.Parse("#17bebe"), theme.Colors["--accent"]);
    }

    [Fact]
    public void Understands_rgba_and_ignores_non_colors()
    {
        var theme = ThemeCss.Parse(":root { --modal-backdrop: rgba(0, 0, 0, 0.75); --font-sans: system-ui; }");

        Assert.Equal(Color.FromArgb(191, 0, 0, 0), theme.Colors["--modal-backdrop"]);
        Assert.False(theme.Colors.ContainsKey("--font-sans"));
        Assert.Equal(ThemeVariant.Default, theme.Variant);
    }
}

public sealed class ThemeServiceTests
{
    [AvaloniaFact]
    public void Apply_overrides_and_later_restores_resources()
    {
        var app = Application.Current!;
        var service = new ThemeService();

        service.Apply(ThemeCss.Parse(":root { color-scheme: dark; --bg-primary: #102030; --accent: #17bebe; --accent-text: #000000; }"));
        Assert.Equal(Colors.Black, ((SolidColorBrush)app.Resources["AccentButtonForeground"]!).Color);
        Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
        Assert.Equal(Color.Parse("#102030"), ((SolidColorBrush)app.Resources["WindowBackgroundBrush"]!).Color);
        Assert.Equal(Color.Parse("#17bebe"), app.Resources["SystemAccentColor"]);

        service.Apply(ThemeCss.Parse(":root { color-scheme: light; }"));
        Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
        Assert.False(app.Resources.ContainsKey("WindowBackgroundBrush"));
        Assert.False(app.Resources.ContainsKey("SystemAccentColor"));
        Assert.False(app.Resources.ContainsKey("AccentButtonForeground"));
    }
}

public sealed class SettingsPageTests
{
    private readonly FakeBackend _backend = new FakeBackend()
        .On("set_setting", true)
        .On("get_settings", new
        {
            schema = new object[]
            {
                new { key = "locale", value_key = "locale", text = "&setting_locale", type = "combo", @default = "en", items = new[] { "en", "ru" } },
                new { key = "theme", value_key = "theme", text = "Theme", type = "combo", @default = "light", items = new object[] { new { value = "dark", label = "Dark" }, new { value = "light", label = "Light" } } },
                new { key = "non_chunked_vpk", value_key = "non_chunked_vpk", text = "Non-chunked", type = "checkbox", @default = false, advanced = true },
                new { key = "d2pfx_filter_nsfw", value_key = "d2pfx_filter_nsfw", text = "NSFW filter", type = "checkbox", @default = true, plugin = "d2pfx" },
                new { key = "delay", value_key = "mod:Auto Accept Match:delay", text = "Delay", type = "slider", @default = 10, var_type = "int", step = 5, min = 0, max = 40, mod = "Auto Accept Match", mod_display_name = "Auto Accept" },
                new { key = "scale", value_key = "mod:Hud:scale", text = "Scale", type = "number", @default = 1.0, var_type = "float", step = 0.1, mod = "Hud" },
                new { key = "font_string", value_key = "mod:Fonts:font_string", text = "Font", type = "inputbox", @default = "Calibri", mod = "Fonts" },
                new { key = "select_font", value_key = "mod:Fonts:select_font", text = "Select font", type = "button", mod = "Fonts" },
                new { key = "color", value_key = "mod:Hud:color", text = "Color", type = "color", @default = "#000000", mod = "Hud" },
                new { key = "hidden", value_key = "mod:Hud:hidden", text = "Hidden", type = "list", @default = Array.Empty<string>(), mod = "Hud" },
                new { key = "weird", value_key = "weird", text = "Weird", type = "unknown-type" },
            },
            values = new Dictionary<string, object>
            {
                ["locale"] = "ru",
                ["theme"] = "dark",
                ["non_chunked_vpk"] = false,
                ["d2pfx_filter_nsfw"] = true,
                ["mod:Auto Accept Match:delay"] = 15,
                ["mod:Hud:scale"] = 1.5,
                ["mod:Hud:color"] = "#ff8800",
                ["mod:Hud:hidden"] = new[] { "a", "b" },
            },
        });

    private readonly List<(string Key, object? Value)> _appChanges = [];

    private async Task<SettingsPageViewModel> LoadedAsync()
    {
        var page = new SettingsPageViewModel(new AppSession(_backend, new Localizer(), new FakeDialogs()),
            (key, value) => { _appChanges.Add((key, value)); return Task.CompletedTask; });
        await page.LoadAsync();
        return page;
    }

    private static T Item<T>(SettingsPageViewModel page, string key) where T : SettingItemViewModel =>
        page.Sections.SelectMany(s => s.Items.Concat(s.AdvancedItems)).OfType<T>().Single(i => i.Spec.Key == key);

    [AvaloniaFact]
    public async Task Groups_settings_into_app_plugin_and_mod_sections()
    {
        var page = await LoadedAsync();

        Assert.Equal(["Application Settings", "Plugin: d2pfx", "Auto Accept", "Hud", "Fonts"], page.Sections.Select(s => s.Title));
        var app = page.Sections[0];
        Assert.True(app.CanReset);
        Assert.Equal("non_chunked_vpk", Assert.Single(app.AdvancedItems).Spec.Key);
        Assert.False(page.Sections[1].CanReset);
        Assert.DoesNotContain(page.Sections.SelectMany(s => s.Items), i => i.Spec.Key == "weird");
    }

    [AvaloniaFact]
    public async Task Values_are_read_by_value_key_with_defaults()
    {
        var page = await LoadedAsync();

        Assert.Equal("ru", Item<ComboSettingViewModel>(page, "locale").Selected!.Value);
        Assert.Equal("Dark", Item<ComboSettingViewModel>(page, "theme").Selected!.Label);
        Assert.Equal(15m, Item<NumberSettingViewModel>(page, "delay").Value);
        Assert.Equal("Calibri", Item<TextSettingViewModel>(page, "font_string").Text);
        Assert.Equal(["a", "b"], Item<ListSettingViewModel>(page, "hidden").Entries.Select(e => e.Text));
        Assert.Equal("setting_locale", Item<ComboSettingViewModel>(page, "locale").Label);
    }

    [AvaloniaFact]
    public async Task App_setting_changes_are_saved_and_reported()
    {
        var page = await LoadedAsync();

        var theme = Item<ComboSettingViewModel>(page, "theme");
        theme.Selected = theme.Options.Single(o => o.Value == "light");

        Assert.Equal(["theme", "light"], _backend.CallsTo("set_setting").Single());
        Assert.Equal(("theme", (object?)"light"), _appChanges.Single());
    }

    [AvaloniaFact]
    public async Task Mod_setting_changes_carry_the_mod_and_are_not_app_changes()
    {
        var page = await LoadedAsync();

        Item<TextSettingViewModel>(page, "font_string").Text = "Inter";

        Assert.Equal(["font_string", "Inter", "Fonts"], _backend.CallsTo("set_setting").Single());
        Assert.Empty(_appChanges);
    }

    [AvaloniaFact]
    public async Task Numbers_keep_their_type_and_sliders_snap_to_step()
    {
        var page = await LoadedAsync();

        var delay = Item<NumberSettingViewModel>(page, "delay");
        delay.SliderValue = 22.4;
        Assert.Equal(20m, delay.Value);
        Assert.Equal((long)20, _backend.CallsTo("set_setting").Last()[1]);

        var scale = Item<NumberSettingViewModel>(page, "scale");
        scale.Value = 1.25m;
        Assert.Equal(1.25, _backend.CallsTo("set_setting").Last()[1]);
        Assert.Equal("1.25", scale.DisplayValue);
    }

    [AvaloniaFact]
    public async Task Color_text_and_picker_stay_in_sync()
    {
        var page = await LoadedAsync();
        var color = Item<ColorSettingViewModel>(page, "color");
        Assert.Equal(Color.Parse("#ff8800"), color.Color);

        color.Color = Color.Parse("#112233");
        Assert.Equal("#112233", color.Text);
        Assert.Equal("#112233", _backend.CallsTo("set_setting").Last()[1]);

        color.Text = "#abcdef";
        Assert.Equal(Color.Parse("#abcdef"), color.Color);
        Assert.Equal(2, _backend.CallsTo("set_setting").Count());
    }

    [AvaloniaFact]
    public async Task Lists_save_the_whole_list_on_every_change()
    {
        var page = await LoadedAsync();
        var list = Item<ListSettingViewModel>(page, "hidden");

        Assert.False(list.AddCommand.CanExecute(null));
        list.NewEntry = "  c ";
        await list.AddCommand.ExecuteAsync(null);
        list.Entries[0].RemoveCommand.Execute(null);
        list.Entries[0].Text = "B";

        var saves = _backend.CallsTo("set_setting").Select(c => (List<string>)c[1]!).ToList();
        Assert.Equal(["a", "b", "c"], saves[0]);
        Assert.Equal(["b", "c"], saves[1]);
        Assert.Equal(["B", "c"], saves[2]);
        Assert.Equal("", list.NewEntry);
    }

    [AvaloniaFact]
    public async Task Buttons_run_mod_functions()
    {
        var page = await LoadedAsync();

        await Item<ButtonSettingViewModel>(page, "select_font").RunCommand.ExecuteAsync(null);

        Assert.Equal(["Fonts", "select_font"], _backend.CallsTo("run_mod_function").Single());
    }

    [AvaloniaFact]
    public async Task Resetting_the_app_section_reapplies_language_and_theme()
    {
        var page = await LoadedAsync();

        await page.Sections[0].ResetCommand.ExecuteAsync(null);

        Assert.Single(_backend.CallsTo("reset_native_settings"));
        Assert.Contains(("locale", (object?)"ru"), _appChanges);
        Assert.Contains(("theme", (object?)"dark"), _appChanges);
    }

    [AvaloniaFact]
    public async Task Resetting_a_mod_section_resets_that_mod()
    {
        var page = await LoadedAsync();

        await page.Sections.Single(s => s.Title == "Fonts").ResetCommand.ExecuteAsync(null);

        Assert.Equal(["Fonts"], _backend.CallsTo("reset_mod_settings").Single());
        Assert.Empty(_appChanges);
    }
}

/// <summary>Finds the repository root from the test binaries.</summary>
public static class RepoRoot
{
    public static readonly string Path = Find();

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Minify", "__main__.py")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found");
    }
}

public sealed class AboutTests
{
    [AvaloniaFact]
    public void About_shows_app_and_backend_versions()
    {
        var backend = new FakeBackend { BackendVersion = "2rc9" };
        var page = new SettingsPageViewModel(new AppSession(backend, new Localizer(), new FakeDialogs()), (_, _) => Task.CompletedTask);

        Assert.Equal($"Minify {Minify.Desktop.Services.AppInfo.Version} · backend 2rc9", page.AboutVersion);
        Assert.Equal(4, Minify.Desktop.Services.AppInfo.Version.ToString().Split('.').Length);
    }
}

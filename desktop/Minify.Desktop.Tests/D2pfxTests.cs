using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Tests;

public sealed class FakeImages : IImageLoader
{
    public List<(string Url, int Width)> Requests { get; } = [];

    public Task<Bitmap?> LoadAsync(string url, int decodeWidth)
    {
        Requests.Add((url, decodeWidth));
        return Task.FromResult<Bitmap?>(null);
    }
}

public sealed class D2pfxTests
{
    private readonly FakeImages _images = new();
    private readonly Dictionary<string, Func<JsonElement, object?>> _actions = new();
    private readonly List<(string Action, JsonElement Params)> _calls = [];
    private int _modsChanged;

    private static object Mod(string name, double date, string? label = null) => new
    {
        name, label, author = new[] { "hrdq", "Egezenn" }, sender = (string?)null,
        tags = new Dictionary<string, bool> { ["anime"] = true, ["nsfw"] = false },
        preview_url = $"https://example.test/{name}.webp", meta = new { date },
    };

    private D2pfxPageViewModel CreatePage()
    {
        _actions["get_categories"] = _ => new[]
        {
            new { id = "couriers", name = "Couriers", description = "Courier models" },
            new { id = "wards", name = "Wards", description = "Ward models" },
        };
        _actions["get_mods"] = p => p.GetProperty("search").GetString() == "baby"
            ? new[] { Mod("Baby Roshan", 200) }
            : new[] { Mod("Waldi", 100), Mod("Baby Roshan", 200), Mod("Old", 300) };
        _actions["get_installed_mods"] = _ => new object[]
        {
            new { name = "Waldi", category = "couriers", label = (string?)null, folder = "a", enabled = true, source_date = 100 },
            new { name = "Baby Roshan", category = "couriers", label = (string?)null, folder = "b", enabled = false, source_date = 150 },
        };
        foreach (var action in new[] { "set_mod_state", "install_mod", "update_mod", "uninstall_mod", "prune_metadata_cache" })
            _actions.TryAdd(action, _ => new { success = true });

        var backend = new FakeBackend().On("call_plugin_api", args =>
        {
            var action = (string)args[1]!;
            var parameters = JsonSerializer.SerializeToElement(args[2], BackendJson.Options);
            _calls.Add((action, parameters));
            return _actions[action](parameters);
        });
        return new D2pfxPageViewModel(new AppSession(backend, new Localizer(), new FakeDialogs()), _images,
            () => { _modsChanged++; return Task.CompletedTask; })
        { SearchDelay = TimeSpan.Zero };
    }

    private async Task<D2pfxPageViewModel> LoadedAsync()
    {
        var page = CreatePage();
        await page.ActivateAsync();
        await page.LoadModsAsync();
        return page;
    }

    private IEnumerable<JsonElement> CallsTo(string action) => _calls.Where(c => c.Action == action).Select(c => c.Params);

    [AvaloniaFact]
    public async Task First_activation_loads_categories_and_the_first_category()
    {
        var page = await LoadedAsync();

        Assert.Equal(["Couriers", "Wards"], page.Categories.Select(c => c.Name));
        Assert.Equal("couriers", page.SelectedCategory!.Id);
        Assert.Equal(["Waldi", "Baby Roshan", "Old"], page.Mods.Select(m => m.Name));
        Assert.Contains(("https://example.test/Waldi.webp", 480), _images.Requests);
    }

    [AvaloniaFact]
    public async Task Cards_show_installed_enabled_and_update_state()
    {
        var page = await LoadedAsync();
        var waldi = page.Mods.Single(m => m.Name == "Waldi");
        var baby = page.Mods.Single(m => m.Name == "Baby Roshan");
        var old = page.Mods.Single(m => m.Name == "Old");

        Assert.True(waldi.IsInstalled);
        Assert.True(waldi.IsEnabled);
        Assert.False(waldi.UpdateAvailable);
        Assert.True(baby.IsInstalled);
        Assert.False(baby.IsEnabled);
        Assert.True(baby.UpdateAvailable);
        Assert.False(old.IsInstalled);
        Assert.Equal("By: hrdq, Egezenn", waldi.Authors);
        Assert.Equal("anime", waldi.Tags);
    }

    [AvaloniaFact]
    public async Task Install_enables_then_installs_the_raw_catalogue_entry()
    {
        var page = await LoadedAsync();
        var old = page.Mods.Single(m => m.Name == "Old");

        await old.InstallCommand.ExecuteAsync(null);

        var state = CallsTo("set_mod_state").Single();
        Assert.Equal("Old", state.GetProperty("mod_name").GetString());
        Assert.True(state.GetProperty("enabled").GetBoolean());
        var install = CallsTo("install_mod").Single();
        Assert.Equal("couriers", install.GetProperty("cat_id").GetString());
        Assert.Equal(300, install.GetProperty("mod").GetProperty("meta").GetProperty("date").GetDouble());
        Assert.Equal("Successfully installed Old", page.Message);
        Assert.Equal(1, _modsChanged);
        Assert.False(old.InProgress);
    }

    [AvaloniaFact]
    public async Task Failed_install_reports_the_plugin_error()
    {
        var page = CreatePage();
        _actions["install_mod"] = _ => new { success = false, error = "disk full" };
        await page.ActivateAsync();
        await page.LoadModsAsync();

        await page.Mods.Single(m => m.Name == "Old").InstallCommand.ExecuteAsync(null);

        Assert.Equal("Failed: disk full", page.Message);
    }

    [AvaloniaFact]
    public async Task Update_and_remove_call_the_plugin()
    {
        var page = await LoadedAsync();
        var baby = page.Mods.Single(m => m.Name == "Baby Roshan");

        await baby.UpdateCommand.ExecuteAsync(null);
        await baby.RemoveCommand.ExecuteAsync(null);

        Assert.Single(CallsTo("update_mod"));
        var remove = CallsTo("uninstall_mod").Single();
        Assert.Equal("Baby Roshan", remove.GetProperty("mod_name").GetString());
        Assert.Equal(JsonValueKind.Null, remove.GetProperty("label").ValueKind);
        Assert.False(baby.IsUpdating);
    }

    [AvaloniaFact]
    public async Task Toggling_an_installed_mod_saves_its_state()
    {
        var page = await LoadedAsync();

        page.Mods.Single(m => m.Name == "Waldi").IsEnabled = false;
        await Task.Yield();

        Assert.False(CallsTo("set_mod_state").Single().GetProperty("enabled").GetBoolean());
        Assert.Equal(1, _modsChanged);
    }

    [AvaloniaFact]
    public async Task Search_filters_categories_locally_and_mods_through_the_plugin()
    {
        var page = await LoadedAsync();

        page.CategorySearch = "war";
        Assert.Equal(["Wards"], page.Categories.Select(c => c.Name));

        page.ModSearch = "baby";
        await Task.Delay(50);
        Assert.Equal(["Baby Roshan"], page.Mods.Select(m => m.Name));
    }

    [AvaloniaFact]
    public async Task Refresh_data_prunes_the_cache_and_reloads()
    {
        var page = await LoadedAsync();
        var before = CallsTo("get_categories").Count();

        await page.RefreshDataCommand.ExecuteAsync(null);

        Assert.Single(CallsTo("prune_metadata_cache"));
        Assert.Equal(before + 1, CallsTo("get_categories").Count());
        Assert.Equal("Metadata cache refreshed.", page.Message);
    }

    [AvaloniaFact]
    public async Task Preview_opens_full_size_and_closes()
    {
        var page = await LoadedAsync();

        await page.Mods[0].ShowPreviewCommand.ExecuteAsync(null);
        Assert.True(page.IsPreviewOpen);
        Assert.Contains(("https://example.test/Waldi.webp", 1600), _images.Requests);

        page.ClosePreviewCommand.Execute(null);
        Assert.False(page.IsPreviewOpen);
    }

    [AvaloniaFact]
    public async Task Plugin_errors_on_load_are_shown()
    {
        var page = CreatePage();
        _actions["get_categories"] = _ => new { error = "offline" };

        await page.ActivateAsync();

        Assert.Empty(page.Categories);
        Assert.Contains("offline", page.Message);
    }

    [Theory]
    [InlineData("1763698532", 1763698532d)]
    [InlineData("garbage", null)]
    public void Dates_parse_from_numbers_and_strings(string value, double? expected) =>
        Assert.Equal(expected, D2pfxPageViewModel.ParseDate(JsonSerializer.SerializeToElement(value)));
}

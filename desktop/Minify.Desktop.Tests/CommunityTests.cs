using System.Text.Json;
using Avalonia.Headless.XUnit;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Tests;

public sealed class CommunityTests
{
    private readonly FakeImages _images = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly List<(string Action, JsonElement Params)> _calls = [];
    private readonly List<string> _changed = [];
    private Func<string, JsonElement, object?> _respond = (_, _) => new { success = true };

    private static object Entry(string id, string name, string type, bool installed = false, bool update = false,
        bool available = true, bool compatible = true) => new
    {
        id, name, type, repo = $"https://github.com/x/{name}", preview = $"https://example.test/{name}.png",
        notes_url = "", installed, installed_tag = installed ? "v1" : null, installed_compatible = compatible,
        current_version = "2rc9", latest_tag = "v2", update_available = update, available, error = (string?)null,
    };

    private CommunityPageViewModel Create()
    {
        var catalogue = new[]
        {
            Entry("mod:Better HUD", "Better HUD", "mod", installed: true, update: true),
            Entry("theme:Nord", "Nord", "theme"),
            Entry("plugin:Stats", "Stats", "plugin", available: false),
            Entry("mod:Old Thing", "Old Thing", "mod", installed: true, compatible: false),
        };
        var backend = new FakeBackend().On("call_plugin_api", args =>
        {
            var action = (string)args[1]!;
            var p = JsonSerializer.SerializeToElement(args[2], BackendJson.Options);
            _calls.Add((action, p));
            return action == "get_catalogue" ? catalogue : _respond(action, p);
        });
        var loc = new Localizer();
        loc.Load(new Dictionary<string, string>
        {
            ["community_installed_tag"] = "Installed: {0}",
            ["community_installed_incompatible"] = "{0} is not compatible with Minify {1}",
            ["community_update_available"] = "Update available: {0}",
            ["community_error_install"] = "Install failed: {0}",
        });
        return new CommunityPageViewModel(new AppSession(backend, loc, _dialogs), _images,
            type => { _changed.Add(type); return Task.CompletedTask; });
    }

    private async Task<CommunityPageViewModel> LoadedAsync()
    {
        var page = Create();
        await page.ActivateAsync();
        return page;
    }

    private CommunityEntryViewModel Get(CommunityPageViewModel page, string name) => page.Entries.Single(e => e.Name == name);

    [AvaloniaFact]
    public async Task Loads_the_catalogue_with_previews()
    {
        var page = await LoadedAsync();

        Assert.Equal(4, page.Entries.Count);
        Assert.Contains(("https://example.test/Nord.png", 480), _images.Requests);
        Assert.Equal("B", Get(page, "Better HUD").Initial);
    }

    [AvaloniaFact]
    public async Task Filters_by_type_and_name()
    {
        var page = await LoadedAsync();

        page.SelectTypeCommand.Execute("theme");
        Assert.Equal(["Nord"], page.Entries.Select(e => e.Name));

        page.SelectedType = "mod";
        page.SearchText = "old";
        Assert.Equal(["Old Thing"], page.Entries.Select(e => e.Name));

        page.SearchText = "nothing";
        Assert.True(page.IsEmpty);
    }

    [AvaloniaFact]
    public async Task Actions_call_the_plugin_and_report_the_package_type()
    {
        var page = await LoadedAsync();

        await Get(page, "Nord").InstallCommand.ExecuteAsync(null);
        await Get(page, "Better HUD").UpdateCommand.ExecuteAsync(null);
        await Get(page, "Better HUD").RemoveCommand.ExecuteAsync(null);

        Assert.Equal(["install_item", "update_item", "uninstall_item"],
            _calls.Where(c => c.Action != "get_catalogue").Select(c => c.Action));
        Assert.Equal("theme:Nord", _calls.First(c => c.Action == "install_item").Params.GetProperty("id").GetString());
        Assert.Equal(["theme", "mod", "mod"], _changed);
        Assert.Equal(4, _calls.Count(c => c.Action == "get_catalogue"));
    }

    [AvaloniaFact]
    public async Task Unavailable_packages_cannot_be_installed()
    {
        var page = await LoadedAsync();

        Assert.False(Get(page, "Stats").InstallCommand.CanExecute(null));
        Assert.True(Get(page, "Nord").InstallCommand.CanExecute(null));
        Assert.True(Get(page, "Better HUD").CanUpdate);
        Assert.False(Get(page, "Better HUD").CanInstall);
    }

    [AvaloniaFact]
    public async Task Failed_actions_show_a_localized_error()
    {
        var page = await LoadedAsync();
        _respond = (_, _) => new { error = "rate limited" };

        await Get(page, "Nord").InstallCommand.ExecuteAsync(null);

        Assert.Equal("Install failed: rate limited", page.ErrorMessage);
        Assert.Empty(_changed);
        Assert.False(Get(page, "Nord").IsBusy);
    }

    [AvaloniaFact]
    public async Task Details_show_status_notes_and_preview()
    {
        var page = await LoadedAsync();
        _respond = (action, _) => action == "get_item_details" ? new { notes = "> [!NOTE]\n> Hi", manifest_url = "" } : null;

        await Get(page, "Better HUD").DetailsCommand.ExecuteAsync(null);

        var details = Assert.IsType<CommunityDetailsViewModel>(Assert.Single(_dialogs.DetailsShown));
        Assert.False(details.IsLoading);
        Assert.Equal("> **Note**\n> Hi", details.Notes);
        Assert.Equal("Installed: v1", details.TagLabel);
        Assert.Equal("Update available: v2", details.UpdateLabel);
        Assert.Null(details.Incompatible);
        Assert.Contains(("https://example.test/Better HUD.png", 1600), _images.Requests);
    }

    [AvaloniaFact]
    public void Incompatible_installs_are_explained()
    {
        var loc = new Localizer();
        loc.Load(new Dictionary<string, string> { ["community_installed_incompatible"] = "{0} needs another Minify than {1}" });
        var entry = new RegistryEntry("mod:x", "x", "mod", null, null, null, true, "v1", false, "2rc9", "v2", false, true, null);

        Assert.Equal("v1 needs another Minify than 2rc9", new CommunityDetailsViewModel(loc, entry).Incompatible);
    }
}

public sealed class PluginTabTests
{
    [AvaloniaFact]
    public async Task Installing_a_plugin_rebuilds_tabs_and_keeps_the_open_page()
    {
        var plugins = new List<object>
        {
            new { id = "community", name = "Community", has_web_ui = false },
        };
        var backend = FakeBackend.WithDefaults()
            .On("get_plugins", _ => plugins.ToArray())
            .On("call_plugin_api", args => (string)args[1]! == "get_catalogue"
                ? new[] { new { id = "plugin:New", name = "New", type = "plugin", installed = false, installed_compatible = true, update_available = false, available = true } }
                : new { success = true });
        var vm = new MainWindowViewModel(new FakeDialogs(), () => Task.FromResult<IBackend>(backend), images: new FakeImages());
        await vm.StartAsync();
        var community = Assert.IsType<CommunityPageViewModel>(vm.Pages.Single(p => p.Id == "community"));
        vm.SelectedPage = community;
        await community.ActivateAsync();

        plugins.Add(new { id = "new", name = "New", has_web_ui = true });
        await community.Entries.Single().InstallCommand.ExecuteAsync(null);

        Assert.Equal(["mods", "terminal", "settings", "community", "new"], vm.Pages.Select(p => p.Id));
        Assert.Same(community, vm.Pages.Single(p => p.Id == "community"));
        Assert.Same(community, vm.SelectedPage);
    }
}

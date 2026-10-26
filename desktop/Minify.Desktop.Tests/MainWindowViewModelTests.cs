using Avalonia.Headless.XUnit;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Tests;

public sealed class MainWindowViewModelTests
{
    private readonly FakeDialogs _dialogs = new();

    private MainWindowViewModel Create(FakeBackend backend) => new(_dialogs, () => Task.FromResult<IBackend>(backend));

    private async Task<(MainWindowViewModel Vm, FakeBackend Backend)> StartedAsync(FakeBackend? backend = null)
    {
        backend ??= FakeBackend.WithDefaults();
        var vm = Create(backend);
        await vm.StartAsync();
        return (vm, backend);
    }

    [AvaloniaFact]
    public async Task Start_loads_locale_mods_and_pages()
    {
        var (vm, _) = await StartedAsync();

        Assert.Equal(BackendState.Ready, vm.State);
        Assert.Equal("PATCH IT", vm.PatchButtonText);
        Assert.Equal(["Dark Terrain", "#base"], vm.ModsPage!.Mods.Select(m => m.Name));
        Assert.False(vm.ModsPage.Mods.Single(m => m.Name == "#base").CanToggle);
        Assert.Same(vm.ModsPage, vm.SelectedPage);
    }

    [AvaloniaFact]
    public async Task Web_only_plugins_get_an_explanation_page()
    {
        var (vm, _) = await StartedAsync();

        var page = Assert.IsType<WebPluginPageViewModel>(vm.Pages.Single(p => p.Id == "thirdparty"));
        Assert.Equal("Third Party", page.Title);
        Assert.Contains("web-based interface", page.Message);
    }

    [AvaloniaFact]
    public async Task Missing_dota_shows_steam_screen()
    {
        var backend = FakeBackend.WithDefaults().On("get_steam_status", new { root = "", library = "", dota_found = false });
        var (vm, _) = await StartedAsync(backend);

        Assert.Equal(BackendState.SteamMissing, vm.State);
        Assert.Empty(backend.CallsTo("get_mods"));
    }

    [AvaloniaFact]
    public async Task Backend_that_cannot_start_shows_error()
    {
        var vm = new MainWindowViewModel(_dialogs, () => throw new FileNotFoundException("Minify backend not found."));

        await vm.StartAsync();

        Assert.Equal(BackendState.Failed, vm.State);
        Assert.Equal("Minify backend not found.", vm.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task Selecting_steam_folder_restarts_backend()
    {
        var first = FakeBackend.WithDefaults()
            .On("get_steam_status", new { root = "", library = "", dota_found = false })
            .On("set_steam_root", true);
        var backends = new Queue<FakeBackend>([first, FakeBackend.WithDefaults()]);
        var vm = new MainWindowViewModel(_dialogs, () => Task.FromResult<IBackend>(backends.Dequeue()));
        _dialogs.FolderResult = "/home/me/.steam";

        await vm.StartAsync();
        await vm.LocateSteamCommand.ExecuteAsync(null);

        Assert.Equal("/home/me/.steam", first.CallsTo("set_steam_root").Single()[0]);
        Assert.True(first.Disposed);
        Assert.Equal(BackendState.Ready, vm.State);
    }

    [AvaloniaFact]
    public async Task Wrong_steam_folder_keeps_asking()
    {
        var backend = FakeBackend.WithDefaults()
            .On("get_steam_status", new { root = "", library = "", dota_found = false })
            .On("set_steam_root", false);
        var (vm, _) = await StartedAsync(backend);
        _dialogs.FolderResult = "/tmp";

        await vm.LocateSteamCommand.ExecuteAsync(null);

        Assert.Equal(BackendState.SteamMissing, vm.State);
        Assert.False(backend.Disposed);
        Assert.Contains(_dialogs.Alerts, a => a.Text.Contains("/tmp"));
    }

    [AvaloniaFact]
    public async Task Toggling_a_mod_saves_it()
    {
        var (vm, backend) = await StartedAsync(FakeBackend.WithDefaults().On("set_mods", true));

        vm.ModsPage!.Mods.Single(m => m.Name == "Dark Terrain").ToggleCommand.Execute(null);

        var saved = (Dictionary<string, bool>)backend.CallsTo("set_mods").Single()[0]!;
        Assert.False(saved["Dark Terrain"]);
    }

    [AvaloniaFact]
    public async Task Locked_mods_cannot_be_toggled()
    {
        var (vm, backend) = await StartedAsync();

        vm.ModsPage!.Mods.Single(m => m.Name == "#base").ToggleCommand.Execute(null);

        Assert.Empty(backend.CallsTo("set_mods"));
    }

    [AvaloniaFact]
    public async Task Failed_save_reverts_the_checkbox()
    {
        var (vm, _) = await StartedAsync(FakeBackend.WithDefaults().On("set_mods", _ => throw new IOException("disk full")));
        var mod = vm.ModsPage!.Mods.Single(m => m.Name == "Dark Terrain");

        mod.IsEnabled = false;
        await Task.Yield();

        Assert.True(mod.IsEnabled);
        Assert.Contains(_dialogs.Alerts, a => a.Text == "disk full");
    }

    [AvaloniaFact]
    public async Task Search_filters_by_display_or_folder_name()
    {
        var (vm, _) = await StartedAsync();

        vm.ModsPage!.SearchText = "dark";
        Assert.Equal(["Dark Terrain"], vm.ModsPage.Mods.Select(m => m.Name));

        vm.ModsPage.ClearSearchCommand.Execute(null);
        Assert.Equal(2, vm.ModsPage.Mods.Count);
    }

    [AvaloniaFact]
    public async Task Refresh_reloads_mods()
    {
        var (vm, backend) = await StartedAsync();

        await vm.ModsPage!.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, backend.CallsTo("get_mods").Count());
    }

    [AvaloniaFact]
    public async Task Log_events_append_strip_ansi_and_clear()
    {
        var (vm, backend) = await StartedAsync();

        backend.Raise("onLogReceived", new { text = "\u001b[38;2;255;0;0mPatching failed!\u001b[0m", type = "error" });
        backend.Raise("onLogReceived", new { text = "", type = "separator" });
        var logs = vm.TerminalPage!.Logs;
        Assert.Equal("Patching failed!", logs[0].Text);
        Assert.True(logs[0].IsError);
        Assert.True(logs[1].IsSeparator);

        backend.Raise("onLogReceived", new { text = "", type = "clear" });
        Assert.Empty(logs);
    }

    [AvaloniaFact]
    public async Task Existing_logs_are_shown_at_startup()
    {
        var backend = FakeBackend.WithDefaults()
            .On("get_logs", new[] { new { text = "earlier", type = "info", timestamp = "10:00:00" } });
        var (vm, _) = await StartedAsync(backend);

        Assert.Equal("earlier", vm.TerminalPage!.Logs.Single().Text);
    }

    [AvaloniaFact]
    public async Task Patch_switches_to_terminal_and_tracks_status()
    {
        var (vm, backend) = await StartedAsync(FakeBackend.WithDefaults().On("start_patch", new { status = "started" }));

        await vm.PatchCommand.ExecuteAsync(null);
        backend.Raise("onPatchStatusChange", true);

        Assert.Same(vm.TerminalPage, vm.SelectedPage);
        Assert.True(vm.IsPatching);
        Assert.False(vm.PatchCommand.CanExecute(null));
        Assert.False(vm.UninstallCommand.CanExecute(null));

        backend.Raise("onPatchStatusChange", false);
        Assert.True(vm.PatchCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Uninstall_needs_confirmation()
    {
        var (vm, backend) = await StartedAsync();

        _dialogs.Answer = _ => new DialogResult("cancel");
        await vm.UninstallCommand.ExecuteAsync(null);
        _dialogs.Answer = _ => new DialogResult(null);
        await vm.UninstallCommand.ExecuteAsync(null);
        Assert.Empty(backend.CallsTo("start_uninstall"));

        _dialogs.Answer = _ => new DialogResult("yes", OptionChecked: true);
        await vm.UninstallCommand.ExecuteAsync(null);
        Assert.Equal(true, backend.CallsTo("start_uninstall").Single()[0]);
        Assert.Equal("Remove everything", _dialogs.Shown.Last().OptionText);
    }

    [AvaloniaFact]
    public async Task Ctrl_tab_cycles_pages()
    {
        var (vm, _) = await StartedAsync();

        vm.CycleTabCommand.Execute(true);
        Assert.Same(vm.TerminalPage, vm.SelectedPage);

        vm.CycleTabCommand.Execute(false);
        vm.CycleTabCommand.Execute(false);
        Assert.Same(vm.Pages[^1], vm.SelectedPage);
    }

    [AvaloniaFact]
    public async Task Download_progress_shows_notifications()
    {
        var (vm, backend) = await StartedAsync();

        backend.Raise("onDownloadProgress", new { id = "s2v", name = "Source2Viewer", downloaded_bytes = 1048576, total_bytes = 4194304, status = "downloading", error = (string?)null });
        var item = vm.Downloads.Single();
        Assert.Equal(25, item.Percent);
        Assert.Equal("1.00 MB / 4.00 MB (25%)", item.Details);

        backend.Raise("onDownloadProgress", new { id = "s2v", name = "Source2Viewer", downloaded_bytes = 4194304, total_bytes = 4194304, status = "error", error = "timeout" });
        Assert.Single(vm.Downloads);
        Assert.Equal("Download failed: timeout", item.Details);

        vm.DismissDownloadCommand.Execute(item);
        Assert.Empty(vm.Downloads);
    }

    [AvaloniaFact]
    public async Task Links_open_in_the_browser()
    {
        var (vm, backend) = await StartedAsync();

        await vm.OpenLinkCommand.ExecuteAsync(vm.Links[0].Url);
        await vm.OpenConfigDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(vm.Links[0].Url, _dialogs.OpenedUrls.Single());
        Assert.Single(backend.CallsTo("open_config_directory"));
        Assert.Contains(vm.Links, l => l.Url.Contains("J0nathan550"));
    }

    [AvaloniaFact]
    public async Task Backend_crash_shows_error()
    {
        var (vm, backend) = await StartedAsync();

        backend.Exit(3);

        Assert.Equal(BackendState.Failed, vm.State);
        Assert.Contains("exit code 3", vm.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task Backend_requests_go_to_dialogs()
    {
        var (_, backend) = await StartedAsync();

        Assert.Equal("handled:pick_file", await backend.RequestHandler!("pick_file", default));
    }
}

using Avalonia.Headless.XUnit;
using Minify.Desktop.Backend;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Tests;

public sealed class MainWindowViewModelTests
{
    private readonly FakeDialogs _dialogs = new();

    private MainWindowViewModel Create(FakeBackend backend) => new(_dialogs, () => Task.FromResult<IBackend>(backend));

    [AvaloniaFact]
    public async Task Start_loads_locale_and_mods()
    {
        var backend = FakeBackend.WithDefaults();
        var vm = Create(backend);

        await vm.StartAsync();

        Assert.Equal(BackendState.Ready, vm.State);
        Assert.Equal("PATCH IT", vm.PatchButtonText);
        Assert.Equal(["Dark Terrain", "#base"], vm.Mods.Select(m => m.Name));
        Assert.False(vm.Mods.Single(m => m.Name == "#base").CanToggle);
    }

    [AvaloniaFact]
    public async Task Missing_dota_shows_steam_screen()
    {
        var backend = FakeBackend.WithDefaults().On("get_steam_status", new { root = "", library = "", dota_found = false });
        var vm = Create(backend);

        await vm.StartAsync();

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
        var second = FakeBackend.WithDefaults();
        var backends = new Queue<FakeBackend>([first, second]);
        var vm = new MainWindowViewModel(_dialogs, () => Task.FromResult<IBackend>(backends.Dequeue()));
        _dialogs.FolderResult = "/home/me/.steam";

        await vm.StartAsync();
        await vm.LocateSteamCommand.ExecuteAsync(null);

        Assert.Equal("/home/me/.steam", first.CallsTo("set_steam_root").Single()[0]);
        Assert.True(first.Disposed);
        Assert.Equal(BackendState.Ready, vm.State);
    }

    [AvaloniaFact]
    public async Task Toggling_a_mod_saves_it()
    {
        var backend = FakeBackend.WithDefaults().On("set_mods", true);
        var vm = Create(backend);
        await vm.StartAsync();

        vm.Mods.Single(m => m.Name == "Dark Terrain").IsEnabled = false;

        var saved = (Dictionary<string, bool>)backend.CallsTo("set_mods").Single()[0]!;
        Assert.False(saved["Dark Terrain"]);
    }

    [AvaloniaFact]
    public async Task Failed_save_reverts_the_checkbox()
    {
        var backend = FakeBackend.WithDefaults().On("set_mods", _ => throw new IOException("disk full"));
        var vm = Create(backend);
        await vm.StartAsync();
        var mod = vm.Mods.Single(m => m.Name == "Dark Terrain");

        mod.IsEnabled = false;
        await Task.Yield();

        Assert.True(mod.IsEnabled);
        Assert.Contains(_dialogs.Alerts, a => a.Text == "disk full");
    }

    [AvaloniaFact]
    public async Task Search_filters_mods()
    {
        var vm = Create(FakeBackend.WithDefaults());
        await vm.StartAsync();

        vm.SearchText = "dark";

        Assert.Equal(["Dark Terrain"], vm.Mods.Select(m => m.Name));
    }

    [AvaloniaFact]
    public async Task Log_events_append_and_clear()
    {
        var backend = FakeBackend.WithDefaults();
        var vm = Create(backend);
        await vm.StartAsync();

        backend.Raise("onLogReceived", new { text = "-> Compiling VPK", type = "info", timestamp = "12:00:00" });
        backend.Raise("onLogReceived", new { text = "Patch successful!", type = "success", timestamp = "12:00:01" });
        Assert.Equal(["-> Compiling VPK", "Patch successful!"], vm.Logs.Select(l => l.Text));

        backend.Raise("onLogReceived", new { text = "", type = "clear" });
        Assert.Empty(vm.Logs);
    }

    [AvaloniaFact]
    public async Task Patch_switches_to_terminal_and_tracks_status()
    {
        var backend = FakeBackend.WithDefaults().On("start_patch", new { status = "started" });
        var vm = Create(backend);
        await vm.StartAsync();

        await vm.PatchCommand.ExecuteAsync(null);
        backend.Raise("onPatchStatusChange", true);

        Assert.Equal(MainWindowViewModel.TerminalTab, vm.SelectedTab);
        Assert.True(vm.IsPatching);
        Assert.False(vm.PatchCommand.CanExecute(null));

        backend.Raise("onPatchStatusChange", false);
        Assert.True(vm.PatchCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Uninstall_needs_confirmation()
    {
        var backend = FakeBackend.WithDefaults();
        var vm = Create(backend);
        await vm.StartAsync();

        _dialogs.ConfirmResult = false;
        await vm.UninstallCommand.ExecuteAsync(null);
        Assert.Empty(backend.CallsTo("start_uninstall"));

        _dialogs.ConfirmResult = true;
        await vm.UninstallCommand.ExecuteAsync(null);
        Assert.Single(backend.CallsTo("start_uninstall"));
    }

    [AvaloniaFact]
    public async Task Backend_crash_shows_error()
    {
        var backend = FakeBackend.WithDefaults();
        var vm = Create(backend);
        await vm.StartAsync();

        backend.Exit(3);

        Assert.Equal(BackendState.Failed, vm.State);
        Assert.Contains("exit code 3", vm.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task Backend_requests_go_to_dialogs()
    {
        var backend = FakeBackend.WithDefaults();
        var vm = Create(backend);
        await vm.StartAsync();

        var result = await backend.RequestHandler!("pick_file", default);

        Assert.Equal("handled:pick_file", result);
    }
}

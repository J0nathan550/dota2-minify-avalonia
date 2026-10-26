using Avalonia.Headless.XUnit;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;
using Minify.Desktop.Services.Updates;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Tests;

public sealed class UpdatePromptTests
{
    private readonly FakeDialogs _dialogs = new();
    private int _exits;

    private sealed class FakeUpdates(UpdateInfo? update) : IUpdateService
    {
        public bool FailInstall { get; init; }
        public UpdateInfo? Installed { get; private set; }

        public Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default) =>
            Task.FromResult(update);

        public Task InstallAsync(UpdateInfo info, IProgress<(long Done, long Total)> progress)
        {
            if (FailInstall)
                throw new UpdateException("disk full");
            progress.Report((5, 10));
            Installed = info;
            return Task.CompletedTask;
        }
    }

    private static UpdateInfo Update(bool withAsset = true) => new(
        new Version(9, 0, 0, 1),
        new Release("v9.0.0.1", "Minify 9.0.0.1", "- Add updater (abc123, J0nathan550)", "https://example.test/r", []),
        withAsset ? new ReleaseAsset("Minify-9.0.0.1-linux-x86_64.AppImage", "https://example.test/a", 10, null) : null);

    private async Task<(MainWindowViewModel Vm, FakeBackend Backend)> StartAsync(FakeUpdates updates, FakeBackend? backend = null)
    {
        backend ??= FakeBackend.WithDefaults();
        var vm = new MainWindowViewModel(_dialogs, () => Task.FromResult<IBackend>(backend),
            updates: updates, exit: () => _exits++);
        await vm.StartAsync();
        return (vm, backend);
    }

    [AvaloniaFact]
    public async Task Startup_offers_a_newer_release_with_its_notes()
    {
        _dialogs.Answer = _ => new DialogResult("later");

        await StartAsync(new FakeUpdates(Update()));

        var prompt = Assert.Single(_dialogs.Shown);
        Assert.Equal(["ignore", "later", "update"], prompt.Buttons.Select(b => b.Id));
        Assert.Contains($"{AppInfo.Version}", prompt.Text);
        Assert.Contains("9.0.0.1", prompt.Text);
        Assert.Contains("Add updater", prompt.Details);
        Assert.Equal(0, _exits);
    }

    [AvaloniaFact]
    public async Task Update_now_installs_shows_progress_and_closes_the_app()
    {
        var updates = new FakeUpdates(Update());
        _dialogs.Answer = _ => new DialogResult("update");

        var (vm, _) = await StartAsync(updates);

        Assert.Equal(new Version(9, 0, 0, 1), updates.Installed!.Version);
        Assert.Equal("finished", vm.Downloads.Single().Status);
        Assert.Equal(1, _exits);
    }

    [AvaloniaFact]
    public async Task Failed_install_is_reported_and_the_app_stays_open()
    {
        _dialogs.Answer = r => new DialogResult(r.Buttons.Count == 1 ? "ok" : "update");

        var (vm, _) = await StartAsync(new FakeUpdates(Update()) { FailInstall = true });

        Assert.Equal("error", vm.Downloads.Single().Status);
        Assert.Contains(_dialogs.Alerts, a => a.Text == "disk full");
        Assert.Equal(0, _exits);
    }

    [AvaloniaFact]
    public async Task Ignored_versions_are_not_offered_on_startup_but_on_a_manual_check()
    {
        var backend = FakeBackend.WithDefaults()
            .On("get_state", args => args[0] as string == MainWindowViewModel.IgnoredUpdateStateKey ? "9.0.0.1" : null);
        _dialogs.Answer = _ => new DialogResult("later");

        var (vm, _) = await StartAsync(new FakeUpdates(Update()), backend);
        Assert.Empty(_dialogs.Shown);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Single(_dialogs.Shown);
    }

    [AvaloniaFact]
    public async Task Ignoring_a_version_remembers_it()
    {
        _dialogs.Answer = _ => new DialogResult("ignore");

        var (_, backend) = await StartAsync(new FakeUpdates(Update()));

        Assert.Equal([MainWindowViewModel.IgnoredUpdateStateKey, "9.0.0.1"], backend.CallsTo("set_state").Single());
    }

    [AvaloniaFact]
    public async Task Without_a_download_for_this_platform_the_release_page_opens()
    {
        _dialogs.Answer = _ => new DialogResult("open");

        await StartAsync(new FakeUpdates(Update(withAsset: false)));

        Assert.Equal("open", _dialogs.Shown.Single().Buttons.Single(b => b.IsDefault).Id);
        Assert.Equal(["https://example.test/r"], _dialogs.OpenedUrls);
    }

    [AvaloniaFact]
    public async Task Manual_check_says_when_up_to_date()
    {
        var (vm, _) = await StartAsync(new FakeUpdates(null));

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Contains("is up to date", _dialogs.Alerts.Single().Text);
        Assert.NotNull(vm.SettingsPage!.CheckForUpdatesCommand);
    }

    [AvaloniaFact]
    public async Task An_update_prompt_postpones_the_workshop_prompt()
    {
        var backend = FakeBackend.WithDefaults().On("is_workshop_installed", false);
        _dialogs.Answer = _ => new DialogResult("later");

        await StartAsync(new FakeUpdates(Update()), backend);

        Assert.Equal("Update Available", _dialogs.Shown.Single().Title);
    }
}

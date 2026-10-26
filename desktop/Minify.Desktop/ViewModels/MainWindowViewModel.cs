using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

public enum BackendState
{
    Starting,
    Ready,
    SteamMissing,
    Failed,
}

public sealed record HeaderLink(string Title, string Url, string Icon);

public sealed partial class MainWindowViewModel(
    IDialogService dialogs,
    Func<Task<IBackend>> startBackend,
    IThemeService? themes = null,
    IAnnouncementSource? announcements = null,
    IImageLoader? images = null)
    : ObservableObject, IAsyncDisposable
{
    public const string WorkshopToolsReleasesUrl = "https://github.com/Dota-Modding-Community/workshoptools/releases";

    private IBackend? _backend;
    private bool _shuttingDown;

    public Localizer Loc { get; } = new();

    public ObservableCollection<PageViewModel> Pages { get; } = [];

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = [];

    public IReadOnlyList<HeaderLink> Links { get; } =
    [
        new("Minify website", "https://egezenn.github.io/dota2-minify", "IconWeb"),
        new("Wiki", "https://github.com/egezenn/dota2-minify/wiki", "IconBook"),
        new("Discord", "https://discord.com/invite/9867CPv7cy", "IconChat"),
        new("Telegram", "https://t.me/dota2minify", "IconSend"),
        new("Buy Egezenn a coffee", "https://buymeacoffee.com/egezenn", "IconCoffee"),
        new("Minify for Avalonia on GitHub", "https://github.com/J0nathan550/dota2-minify-avalonia", "IconGithub"),
    ];

    public ModsPageViewModel? ModsPage { get; private set; }
    public TerminalPageViewModel? TerminalPage { get; private set; }
    public SettingsPageViewModel? SettingsPage { get; private set; }

    [ObservableProperty]
    public partial PageViewModel? SelectedPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsStarting), nameof(IsSteamMissing), nameof(IsFailed))]
    public partial BackendState State { get; set; } = BackendState.Starting;

    public bool IsReady => State == BackendState.Ready;
    public bool IsStarting => State == BackendState.Starting;
    public bool IsSteamMissing => State == BackendState.SteamMissing;
    public bool IsFailed => State == BackendState.Failed;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    [ObservableProperty]
    public partial string Version { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatchButtonText))]
    [NotifyCanExecuteChangedFor(nameof(PatchCommand), nameof(UninstallCommand))]
    public partial bool IsPatching { get; set; }

    public string PatchButtonText => IsPatching ? Loc["button_patching"] : Loc["button_patch"];

    /// <summary>Workshop Tools aren't installed; the header offers to download them.</summary>
    [ObservableProperty]
    public partial bool IsWorkshopMissing { get; set; }

    partial void OnSelectedPageChanged(PageViewModel? value)
    {
        if (value is not null)
            _ = value.ActivateAsync();
    }

    public async Task StartAsync()
    {
        State = BackendState.Starting;
        try
        {
            _backend = await startBackend();
            _backend.RequestHandler = dialogs.HandleBackendRequestAsync;
            _backend.EventReceived += OnBackendEvent;
            _backend.Exited += OnBackendExited;
            Version = _backend.BackendVersion;

            var locale = await _backend.CallAsync<string>("get_current_locale") ?? "en";
            await LoadLocaleAsync(locale);
            await InitThemeAsync();

            var steam = await _backend.CallAsync<SteamStatus>("get_steam_status");
            if (steam is { DotaFound: false })
            {
                State = BackendState.SteamMissing;
                return;
            }

            await BuildPagesAsync(new AppSession(_backend, Loc, dialogs));
            IsPatching = await _backend.CallAsync<bool>("is_patching");
            IsWorkshopMissing = !await _backend.CallAsync<bool>("is_workshop_installed");
            State = BackendState.Ready;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            ErrorMessage = ex.Message;
            State = BackendState.Failed;
            return;
        }
        await RunStartupPromptsAsync();
    }

    /// <summary>One prompt after another, like the web UI: old mods, announcements, then Workshop Tools.</summary>
    private async Task RunStartupPromptsAsync()
    {
        try
        {
            await OfferLegacyModsImportAsync();
            var announced = await ShowAnnouncementsAsync();
            if (!announced && IsWorkshopMissing)
                await OfferWorkshopToolsDownloadAsync(onStartup: true);
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
        }
    }

    private async Task OfferLegacyModsImportAsync()
    {
        var entries = await _backend!.CallAsync<List<string>>("get_legacy_mods") ?? [];
        if (entries.Count == 0)
            return;
        var result = await dialogs.ShowAsync(new DialogRequest(
            Loc["title_import_legacy_mods"],
            Loc["modal_import_legacy_mods"],
            [
                new DialogButton("discard", Loc["button_discard"]),
                new DialogButton("import", Loc["button_import"], IsDefault: true, IsAccent: true),
            ],
            Details: string.Join('\n', entries)));
        // Closed without choosing: ask again next time.
        if (result.ButtonId is null)
            return;
        var import = result.ButtonId == "import";
        await _backend.CallAsync("import_legacy_mods", import);
        if (import && ModsPage is not null)
            await ModsPage.LoadAsync();
    }

    private async Task<bool> ShowAnnouncementsAsync()
    {
        if (announcements is null)
            return false;
        IReadOnlyList<Announcement> all;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            all = await announcements.FetchAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Announcements unavailable: {ex.Message}");
            return false;
        }

        var seen = await _backend!.CallAsync<List<string>>("get_state", Announcements.SeenStateKey) ?? [];
        var pending = Announcements.Pending(all, seen, DateTimeOffset.UtcNow, AppInfo.Version.ToString());
        foreach (var announcement in pending)
        {
            await dialogs.ShowAsync(new DialogRequest(
                announcement.Title ?? Loc["title_announcement"],
                announcement.Text!,
                [new DialogButton("ok", Loc["button_ok"], IsDefault: true, IsAccent: true)],
                Links: announcement.AllUrls));
            seen.Add(announcement.Id);
            await _backend.CallAsync("set_state", Announcements.SeenStateKey, seen);
        }
        return pending.Count > 0;
    }

    /// <summary>Offers to download Workshop Tools; on startup it can also be ignored for good.</summary>
    private async Task OfferWorkshopToolsDownloadAsync(bool onStartup)
    {
        if (onStartup && await _backend!.CallAsync<bool?>("get_state", "ignore_workshop_download") == true)
            return;

        string? error = null;
        while (true)
        {
            List<DialogButton> buttons = error is not null
                ? [new("close", Loc["button_close"], IsCancel: true), new("download", Loc["button_retry"], IsDefault: true, IsAccent: true)]
                : onStartup
                    ? [new("ignore", Loc["button_ignore"]), new("later", Loc["button_later"], IsCancel: true), new("download", Loc["button_ok"], IsDefault: true, IsAccent: true)]
                    : [new("later", Loc["button_later"], IsCancel: true), new("download", Loc["button_ok"], IsDefault: true, IsAccent: true)];
            var result = await dialogs.ShowAsync(new DialogRequest(
                Loc["title_download_workshop_tools"],
                Loc["modal_workshop_tools_download_prompt"],
                buttons,
                error is null ? "info" : "error",
                Links: [WorkshopToolsReleasesUrl],
                Details: error));

            if (result.ButtonId == "ignore")
            {
                await _backend!.CallAsync("set_state", "ignore_workshop_download", true);
                return;
            }
            if (result.ButtonId != "download")
                return;

            if (await _backend!.CallAsync<bool>("download_workshop_tools"))
            {
                IsWorkshopMissing = false;
                if (ModsPage is not null)
                    await ModsPage.LoadAsync();
                return;
            }
            error = "Download or extraction failed. Check the Terminal for details.";
        }
    }

    [RelayCommand]
    private Task DownloadWorkshopToolsAsync() => OfferWorkshopToolsDownloadAsync(onStartup: false);

    public async Task LoadLocaleAsync(string locale)
    {
        Loc.Load(await _backend!.CallAsync<Dictionary<string, string>>("get_localization", locale) ?? [], locale);
        OnPropertyChanged(nameof(PatchButtonText));
        foreach (var page in Pages)
            page.RefreshTitle();
    }

    /// <summary>First run follows the system's light/dark preference, after that the saved theme.</summary>
    private async Task InitThemeAsync()
    {
        if (themes is null)
            return;
        if (await _backend!.CallAsync<bool?>("get_state", "system_theme_init") != true)
        {
            await _backend.CallAsync("set_setting", "theme", themes.SystemPrefersDark ? "dark" : "light");
            await _backend.CallAsync("set_state", "system_theme_init", true);
        }
        await ApplyThemeAsync(null);
    }

    private async Task ApplyThemeAsync(string? name)
    {
        if (themes is null)
            return;
        var css = await _backend!.CallAsync<string>("get_theme_css", name) ?? "";
        themes.Apply(ThemeCss.Parse(css));
    }

    /// <summary>Application settings that take effect immediately.</summary>
    private async Task OnApplicationSettingChangedAsync(string key, object? value)
    {
        switch (key)
        {
            case "theme":
                await ApplyThemeAsync(value as string);
                break;
            case "locale" when value is string locale:
                await LoadLocaleAsync(locale);
                if (SettingsPage is not null)
                    await SettingsPage.LoadAsync();
                break;
            case "output_locale" when ModsPage is not null:
                // Language-specific mods are switched on/off with the game language.
                await ModsPage.LoadAsync();
                break;
        }
    }

    private async Task BuildPagesAsync(AppSession session)
    {
        ModsPage = new ModsPageViewModel(session);
        TerminalPage = new TerminalPageViewModel(session);
        SettingsPage = new SettingsPageViewModel(session, OnApplicationSettingChangedAsync);
        await ModsPage.LoadAsync();
        foreach (var entry in await session.Backend.CallAsync<List<LogEntry>>("get_logs") ?? [])
            TerminalPage.Add(entry);

        Pages.Clear();
        Pages.Add(ModsPage);
        Pages.Add(TerminalPage);
        Pages.Add(SettingsPage);
        _session = session;
        SelectedPage = ModsPage;
        await RefreshPluginPagesAsync();
        OnPropertyChanged(nameof(ModsPage));
        OnPropertyChanged(nameof(TerminalPage));
        OnPropertyChanged(nameof(SettingsPage));
    }

    private const int FixedPageCount = 3; // Mods, Terminal, Settings
    private AppSession? _session;

    /// <summary>(Re)builds the plugin tabs, keeping pages that already exist so their state survives.</summary>
    private async Task RefreshPluginPagesAsync()
    {
        var existing = Pages.Skip(FixedPageCount).ToDictionary(p => p.Id);
        var plugins = await _session!.Backend.CallAsync<List<PluginInfo>>("get_plugins") ?? [];
        var selected = SelectedPage;
        while (Pages.Count > FixedPageCount)
            Pages.RemoveAt(Pages.Count - 1);
        foreach (var plugin in plugins)
        {
            if ((existing.GetValueOrDefault(plugin.Id) ?? CreatePluginPage(plugin)) is { } page)
                Pages.Add(page);
        }
        SelectedPage = selected is not null && Pages.Contains(selected) ? selected : ModsPage;
    }

    /// <summary>Built-in plugins have native pages; other plugins' web pages can't be shown.</summary>
    private PageViewModel? CreatePluginPage(PluginInfo plugin) => plugin.Id switch
    {
        D2pfxPageViewModel.PluginId when images is not null => new D2pfxPageViewModel(_session!, images, ModsPage!.LoadAsync),
        CommunityPageViewModel.PluginId when images is not null => new CommunityPageViewModel(_session!, images, OnPackageChangedAsync),
        _ when plugin.HasWebUi => new WebPluginPageViewModel(_session!, plugin.Id, plugin.Name),
        _ => null,
    };

    /// <summary>A community package was installed, updated or removed.</summary>
    private async Task OnPackageChangedAsync(string type)
    {
        switch (type)
        {
            case "mod" when ModsPage is not null:
                await ModsPage.LoadAsync();
                break;
            case "theme":
                await ApplyThemeAsync(null);
                break;
            case "plugin":
                await RefreshPluginPagesAsync();
                break;
        }
    }

    private bool CanRunOperation() => !IsPatching;

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task PatchAsync()
    {
        // Workshop Tools found in the game files (Linux): offer to extract them before patching.
        if (await _backend!.CallAsync<bool>("check_workshop_tools_needed"))
        {
            var result = await dialogs.ShowAsync(new DialogRequest(
                Loc["title_extract_workshop_tools"],
                $"{Loc["modal_workshop_tools_detected"]}\n\n{Loc["modal_workshop_tools_prompt"]}",
                [
                    new DialogButton("cancel", Loc["button_cancel"], IsCancel: true),
                    new DialogButton("skip", Loc["button_skip"]),
                    new DialogButton("extract", Loc["button_extract"], IsDefault: true, IsAccent: true),
                ]));
            if (result.ButtonId is not ("skip" or "extract"))
                return;
            SelectedPage = TerminalPage;
            if (result.ButtonId == "extract")
                await _backend.CallAsync("extract_workshop_tools");
        }
        SelectedPage = TerminalPage;
        await _backend.CallAsync<OperationStatus>("start_patch");
    }

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task UninstallAsync()
    {
        var result = await dialogs.ShowAsync(new DialogRequest(
            Loc["title_uninstall_mods"],
            Loc["modal_uninstall_message"],
            [
                new DialogButton("cancel", Loc["button_cancel"], IsCancel: true),
                new DialogButton("yes", Loc["button_yes"], IsDefault: true, IsAccent: true),
            ],
            "warning",
            Loc["label_remove_everything"]));
        if (result.ButtonId != "yes")
            return;
        SelectedPage = TerminalPage;
        await _backend!.CallAsync<OperationStatus>("start_uninstall", result.OptionChecked);
    }

    [RelayCommand]
    private Task OpenConfigDirectoryAsync() => _backend!.CallAsync("open_config_directory");

    [RelayCommand]
    private Task OpenLinkAsync(string url) => dialogs.OpenUrlAsync(url);

    [RelayCommand]
    private void CycleTab(bool forward)
    {
        if (Pages.Count == 0)
            return;
        var index = SelectedPage is null ? -1 : Pages.IndexOf(SelectedPage);
        SelectedPage = Pages[(index + (forward ? 1 : -1) + Pages.Count) % Pages.Count];
    }

    [RelayCommand]
    private void DismissDownload(DownloadItemViewModel item) => Downloads.Remove(item);

    [RelayCommand]
    private async Task LocateSteamAsync()
    {
        var path = await dialogs.PickFolderAsync("Select your Steam installation folder");
        if (path is null)
            return;
        if (!await _backend!.CallAsync<bool>("set_steam_root", path))
        {
            await dialogs.AlertAsync("Minify", $"Dota 2 was not found in the Steam libraries of {path}.", "error");
            return;
        }
        // Steam paths are resolved when the backend imports, so it has to start over.
        await RestartAsync();
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        await StopBackendAsync();
        await StartAsync();
    }

    private void OnBackendEvent(string name, JsonElement data)
    {
        switch (name)
        {
            case "onLogReceived":
                if (data.Deserialize<LogEntry>(BackendJson.Options) is { } entry)
                    TerminalPage?.Add(entry);
                break;
            case "onPatchStatusChange":
                IsPatching = data.ValueKind == JsonValueKind.True;
                break;
            case "onDownloadProgress":
                OnDownloadProgress(data);
                break;
        }
    }

    private void OnDownloadProgress(JsonElement data)
    {
        var id = data.GetProperty("id").GetString() ?? "";
        var item = Downloads.FirstOrDefault(d => d.Id == id);
        if (item is null)
        {
            item = new DownloadItemViewModel(id, Loc);
            Downloads.Add(item);
        }
        item.Update(data);
        if (!item.IsDownloading)
            _ = DismissLaterAsync(item);
    }

    private async Task DismissLaterAsync(DownloadItemViewModel item)
    {
        await Task.Delay(TimeSpan.FromSeconds(3.5));
        Downloads.Remove(item);
    }

    private void OnBackendExited(int exitCode)
    {
        if (_shuttingDown)
            return;
        ErrorMessage = $"The Minify backend stopped unexpectedly (exit code {exitCode}). Check logs/crashlog.txt.";
        State = BackendState.Failed;
        IsPatching = false;
    }

    private async Task StopBackendAsync()
    {
        if (_backend is null)
            return;
        _shuttingDown = true;
        _backend.EventReceived -= OnBackendEvent;
        _backend.Exited -= OnBackendExited;
        await _backend.DisposeAsync();
        _backend = null;
        _shuttingDown = false;
    }

    public ValueTask DisposeAsync() => new(StopBackendAsync());
}

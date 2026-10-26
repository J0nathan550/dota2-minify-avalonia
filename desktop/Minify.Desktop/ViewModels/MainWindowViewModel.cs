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
    IDialogService dialogs, Func<Task<IBackend>> startBackend, IThemeService? themes = null)
    : ObservableObject, IAsyncDisposable
{
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
            State = BackendState.Ready;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            ErrorMessage = ex.Message;
            State = BackendState.Failed;
        }
    }

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
        foreach (var plugin in await session.Backend.CallAsync<List<PluginInfo>>("get_plugins") ?? [])
        {
            if (plugin.HasWebUi)
                Pages.Add(new WebPluginPageViewModel(session, plugin.Id, plugin.Name));
        }
        SelectedPage = ModsPage;
        OnPropertyChanged(nameof(ModsPage));
        OnPropertyChanged(nameof(TerminalPage));
        OnPropertyChanged(nameof(SettingsPage));
    }

    private bool CanRunOperation() => !IsPatching;

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task PatchAsync()
    {
        SelectedPage = TerminalPage;
        await _backend!.CallAsync<OperationStatus>("start_patch");
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

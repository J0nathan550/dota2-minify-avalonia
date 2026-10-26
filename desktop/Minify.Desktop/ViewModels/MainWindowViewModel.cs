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

public sealed partial class MainWindowViewModel(IDialogService dialogs, Func<Task<IBackend>> startBackend)
    : ObservableObject, IAsyncDisposable
{
    public const int ModsTab = 0;
    public const int TerminalTab = 1;

    private IBackend? _backend;
    private bool _shuttingDown;
    private List<ModItemViewModel> _allMods = [];

    public Localizer Loc { get; } = new();
    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public ObservableCollection<LogEntry> Logs { get; } = [];

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

    [ObservableProperty]
    public partial int SelectedTab { get; set; } = ModsTab;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string DownloadStatus { get; set; } = "";

    partial void OnSearchTextChanged(string value) => ApplyFilter();

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
            Loc.Load(await _backend.CallAsync<Dictionary<string, string>>("get_localization", locale) ?? []);
            OnPropertyChanged(nameof(PatchButtonText));

            var steam = await _backend.CallAsync<SteamStatus>("get_steam_status");
            if (steam is { DotaFound: false })
            {
                State = BackendState.SteamMissing;
                return;
            }

            Logs.Clear();
            foreach (var entry in await _backend.CallAsync<List<LogEntry>>("get_logs") ?? [])
                AddLog(entry);
            IsPatching = await _backend.CallAsync<bool>("is_patching");
            await LoadModsAsync();
            State = BackendState.Ready;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            ErrorMessage = ex.Message;
            State = BackendState.Failed;
        }
    }

    private async Task LoadModsAsync()
    {
        var mods = await _backend!.CallAsync<List<ModInfo>>("get_mods") ?? [];
        _allMods = mods.Select(m => new ModItemViewModel(m, PersistModStateAsync)).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Mods.Clear();
        foreach (var mod in _allMods.Where(m =>
                     m.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
            Mods.Add(mod);
    }

    private async Task PersistModStateAsync(ModItemViewModel mod, bool enabled)
    {
        try
        {
            await _backend!.CallAsync("set_mods", new Dictionary<string, bool> { [mod.Name] = enabled });
        }
        catch (Exception ex)
        {
            mod.SetEnabledSilently(!enabled);
            await dialogs.AlertAsync("Minify", ex.Message, "error");
        }
    }

    private bool CanRunOperation() => !IsPatching;

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task PatchAsync()
    {
        SelectedTab = TerminalTab;
        await _backend!.CallAsync<OperationStatus>("start_patch");
    }

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private async Task UninstallAsync()
    {
        if (!await dialogs.ConfirmAsync(Loc["title_uninstall_mods"], Loc["modal_uninstall_message"]))
            return;
        SelectedTab = TerminalTab;
        await _backend!.CallAsync<OperationStatus>("start_uninstall", false);
    }

    [RelayCommand]
    private async Task ClearLogsAsync()
    {
        await _backend!.CallAsync("clear_logs");
        Logs.Clear();
    }

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
                var entry = data.Deserialize<LogEntry>(BackendJson.Options);
                if (entry is { Type: "clear" })
                    Logs.Clear();
                else if (entry is not null)
                    AddLog(entry);
                break;
            case "onPatchStatusChange":
                IsPatching = data.ValueKind == JsonValueKind.True;
                break;
            case "onDownloadProgress":
                DownloadStatus = FormatDownload(data);
                break;
        }
    }

    private void AddLog(LogEntry entry)
    {
        Logs.Add(entry);
        while (Logs.Count > 5000)
            Logs.RemoveAt(0);
    }

    private static string FormatDownload(JsonElement data)
    {
        var name = data.GetProperty("name").GetString();
        var status = data.GetProperty("status").GetString();
        if (status != "downloading")
            return "";
        var done = data.GetProperty("downloaded_bytes").GetInt64();
        var total = data.GetProperty("total_bytes").GetInt64();
        return total > 0 ? $"{name} {done * 100 / total}%" : $"{name} {done / 1024 / 1024} MB";
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

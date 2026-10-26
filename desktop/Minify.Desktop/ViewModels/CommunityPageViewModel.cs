using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

/// <summary>A package of the community registry (Minify/plugins/community/api.py).</summary>
public sealed record RegistryEntry(
    string Id,
    string Name,
    string Type,
    string? Repo,
    string? Preview,
    string? NotesUrl,
    bool Installed,
    string? InstalledTag,
    bool InstalledCompatible,
    string? CurrentVersion,
    string? LatestTag,
    bool UpdateAvailable,
    bool Available,
    string? Error);

public sealed partial class CommunityEntryViewModel(CommunityPageViewModel page, RegistryEntry entry) : ObservableObject
{
    public RegistryEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public bool IsInstalled => Entry.Installed;
    public bool CanUpdate => Entry.Installed && Entry.UpdateAvailable;
    public bool CanInstall => !Entry.Installed;

    public string Initial
    {
        get
        {
            var letters = Entry.Name.TrimStart(Entry.Name.Where(c => !char.IsLetterOrDigit(c)).ToArray());
            return letters.Length > 0 ? letters[..1].ToUpperInvariant() : "?";
        }
    }

    public Localizer Loc => page.Loc;
    public string TypeLabel => page.Loc[$"community_type_{Entry.Type}"];
    public string InstallText => IsBusy ? page.Loc["community_button_installing"] : page.Loc["community_button_install"];
    public string UpdateText => IsBusy ? page.Loc["community_button_updating"] : page.Loc["community_button_update"];
    public string RemoveText => IsBusy ? page.Loc["community_button_removing"] : page.Loc["community_button_uninstall"];

    [ObservableProperty]
    public partial Bitmap? PreviewImage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallText), nameof(UpdateText), nameof(RemoveText))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(UpdateCommand), nameof(RemoveCommand))]
    public partial bool IsBusy { get; set; }

    private bool CanInstallNow() => !IsBusy && Entry.Available;
    private bool Idle() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanInstallNow))]
    private Task InstallAsync() => page.RunActionAsync(this, "install_item");

    [RelayCommand(CanExecute = nameof(Idle))]
    private Task UpdateAsync() => page.RunActionAsync(this, "update_item");

    [RelayCommand(CanExecute = nameof(Idle))]
    private Task RemoveAsync() => page.RunActionAsync(this, "uninstall_item");

    [RelayCommand]
    private Task DetailsAsync() => page.ShowDetailsAsync(this);
}

/// <summary>Details window content for a registry package: status, links, notes and preview.</summary>
public sealed partial class CommunityDetailsViewModel : ObservableObject, IDetailsViewModel
{
    public CommunityDetailsViewModel(Localizer loc, RegistryEntry entry)
    {
        Loc = loc;
        Entry = entry;
        Title = entry.Name;
        var unknown = loc["community_unknown_tag"];
        TypeLabel = loc[$"community_type_{entry.Type}"];
        TagLabel = entry.Installed
            ? loc.Format("community_installed_tag", entry.InstalledTag ?? unknown)
            : loc.Format("community_latest_tag", entry.LatestTag ?? loc["community_unavailable_tag"]);
        Incompatible = entry.Installed && !entry.InstalledCompatible
            ? loc.Format("community_installed_incompatible", entry.InstalledTag ?? unknown, entry.CurrentVersion ?? "")
            : null;
        UpdateLabel = entry.Installed && entry.UpdateAvailable
            ? loc.Format("community_update_available", entry.LatestTag ?? "")
            : null;
    }

    public Localizer Loc { get; }
    public RegistryEntry Entry { get; }
    public string Title { get; }
    public string TypeLabel { get; }
    public string TagLabel { get; }
    public string? Incompatible { get; }
    public string? UpdateLabel { get; }
    public bool HasRepo => !string.IsNullOrEmpty(Entry.Repo);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes), nameof(IsEmpty))]
    public partial string? Notes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(IsEmpty))]
    public partial Bitmap? Preview { get; set; }

    [ObservableProperty]
    public partial bool IsLightboxOpen { get; set; }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasPreview => Preview is not null;
    public bool IsEmpty => !IsLoading && !HasNotes && !HasPreview;

    [RelayCommand]
    private void ToggleLightbox() => IsLightboxOpen = HasPreview && !IsLightboxOpen;
}

/// <summary>Native page for the community registry plugin: browse, install, update and remove packages.</summary>
public sealed partial class CommunityPageViewModel(AppSession session, IImageLoader images, Func<string, Task> packageChanged)
    : PageViewModel("community")
{
    public const string PluginId = "community";
    public static readonly string[] TypeFilters = ["all", "mod", "theme", "plugin"];

    private List<CommunityEntryViewModel> _all = [];
    private bool _loaded;

    public override string Title => session.Loc["community_title"];

    public Localizer Loc => session.Loc;

    public ObservableCollection<CommunityEntryViewModel> Entries { get; } = [];

    public IReadOnlyList<ComboOption> Types => TypeFilters
        .Select(t => new ComboOption(t, session.Loc[t == "all" ? "community_filter_all" : $"community_filter_{t}s"]))
        .ToList();

    [ObservableProperty]
    public partial string SelectedType { get; set; } = "all";

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshText), nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    public string RefreshText => IsLoading ? Loc["community_button_loading"] : Loc["community_button_refresh"];
    public bool IsEmpty => !IsLoading && Entries.Count == 0 && ErrorMessage.Length == 0;

    public override async Task ActivateAsync()
    {
        if (_loaded)
            return;
        _loaded = true;
        await LoadAsync();
    }

    partial void OnSelectedTypeChanged(string value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void SelectType(string type) => SelectedType = type;

    private bool CanRefresh() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => LoadAsync();

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var entries = (await CallAsync("get_catalogue")).Deserialize<List<RegistryEntry>>(BackendJson.Options) ?? [];
            _all = entries.Select(e => new CommunityEntryViewModel(this, e)).ToList();
            foreach (var entry in _all.Where(e => !string.IsNullOrEmpty(e.Entry.Preview)))
                _ = LoadPreviewAsync(entry);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.Format("community_error_catalogue", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPreviewAsync(CommunityEntryViewModel entry) =>
        entry.PreviewImage = await images.LoadAsync(entry.Entry.Preview!, 480);

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        Entries.Clear();
        foreach (var entry in _all.Where(e =>
                     (SelectedType == "all" || e.Entry.Type == SelectedType) &&
                     (query.Length == 0 || e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))))
            Entries.Add(entry);
        OnPropertyChanged(nameof(IsEmpty));
    }

    internal async Task RunActionAsync(CommunityEntryViewModel entry, string action)
    {
        entry.IsBusy = true;
        ErrorMessage = "";
        try
        {
            await CallAsync(action, new { id = entry.Entry.Id });
            await packageChanged(entry.Entry.Type);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            var key = action switch
            {
                "install_item" => "community_error_install",
                "update_item" => "community_error_update",
                _ => "community_error_uninstall",
            };
            ErrorMessage = Loc.Format(key, ex.Message);
        }
        finally
        {
            entry.IsBusy = false;
        }
    }

    internal async Task ShowDetailsAsync(CommunityEntryViewModel entry)
    {
        var details = new CommunityDetailsViewModel(Loc, entry.Entry);
        await Task.WhenAll(LoadDetailsAsync(details), session.Dialogs.ShowDetailsAsync(details));
    }

    private async Task LoadDetailsAsync(CommunityDetailsViewModel details)
    {
        try
        {
            if (!string.IsNullOrEmpty(details.Entry.Preview))
                details.Preview = await images.LoadAsync(details.Entry.Preview, 1600);
            var result = await CallAsync("get_item_details", new { id = details.Entry.Id });
            var notes = result.TryGetProperty("notes", out var n) ? n.GetString() : null;
            details.Notes = string.IsNullOrWhiteSpace(notes) ? null : MarkdownText.ConvertAlerts(notes);
        }
        catch (Exception ex)
        {
            details.Notes = Loc.Format("community_error_notes", ex.Message);
        }
        finally
        {
            details.IsLoading = false;
        }
    }

    /// <summary>Plugin API call; the plugin reports failures as {"error": ...}.</summary>
    private async Task<JsonElement> CallAsync(string action, object? parameters = null)
    {
        var result = await session.Backend.CallAsync("call_plugin_api", PluginId, action, parameters ?? new { });
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.String)
            throw new BackendException(error.GetString()!);
        return result;
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

public sealed record D2pfxCategory(string Id, string Name, string Description);

public sealed record D2pfxInstalled(string Name, string Category, string? Label, string? Folder, bool? Enabled, JsonElement SourceDate);

/// <summary>A mod from the D2PFX catalogue. The raw entry is passed back to install/update as-is.</summary>
public sealed partial class D2pfxModViewModel : ObservableObject
{

    private readonly D2pfxPageViewModel _page;
    private bool _syncing;

    public D2pfxModViewModel(D2pfxPageViewModel page, string categoryId, JsonElement raw)
    {
        _page = page;
        CategoryId = categoryId;
        Raw = raw.Clone();
        Name = Text(raw, "name") ?? "";
        Label = Text(raw, "label");
        PreviewUrl = Text(raw, "preview_url");
        Authors = FormatPeople(raw);
        Tags = FormatTags(raw);
        CatalogueDate = raw.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object &&
                        meta.TryGetProperty("date", out var date) ? D2pfxPageViewModel.ParseDate(date) : null;
    }

    public JsonElement Raw { get; }
    public string CategoryId { get; }
    public string Name { get; }
    public string? Label { get; }
    public string Title => string.IsNullOrEmpty(Label) ? Name : $"{Name} ({Label})";
    public string? PreviewUrl { get; }
    public string Authors { get; }
    public string Tags { get; }
    public bool HasAuthors => Authors.Length > 0;
    public bool HasTags => Tags.Length > 0;
    public double? CatalogueDate { get; }

    public Localizer Loc => _page.Loc;

    [ObservableProperty]
    public partial Bitmap? Preview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveText))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(UpdateCommand), nameof(RemoveCommand))]
    public partial bool InProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    public partial bool IsUpdating { get; set; }

    [ObservableProperty]
    public partial bool IsInstalled { get; set; }

    [ObservableProperty]
    public partial bool UpdateAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public string UpdateText => IsUpdating ? Loc["button_updating"] : Loc["button_update"];
    public string RemoveText => InProgress && !IsUpdating ? Loc["button_removing"] : Loc["button_remove"];
    public string InstallText => InProgress ? Loc["button_installing"] : Loc["button_install"];

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_syncing)
            _ = _page.SetEnabledAsync(this, value);
    }

    internal void SetEnabledSilently(bool value)
    {
        _syncing = true;
        IsEnabled = value;
        _syncing = false;
    }

    private bool Idle() => !InProgress;

    [RelayCommand(CanExecute = nameof(Idle))]
    private Task InstallAsync() => _page.InstallAsync(this);

    [RelayCommand(CanExecute = nameof(Idle))]
    private Task UpdateAsync() => _page.UpdateAsync(this);

    [RelayCommand(CanExecute = nameof(Idle))]
    private Task RemoveAsync() => _page.RemoveAsync(this);

    [RelayCommand]
    private Task ShowPreviewAsync() => _page.ShowPreviewAsync(this);

    internal void NotifyTexts()
    {
        OnPropertyChanged(nameof(InstallText));
        OnPropertyChanged(nameof(RemoveText));
        OnPropertyChanged(nameof(UpdateText));
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Join(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(v => v.ToString())),
        _ => "",
    };

    internal static string FormatPeople(JsonElement raw)
    {
        var parts = new List<string>();
        if (raw.TryGetProperty("author", out var author) && Join(author) is { Length: > 0 } a)
            parts.Add($"By: {a}");
        if (raw.TryGetProperty("sender", out var sender) && Join(sender) is { Length: > 0 } s)
            parts.Add($"Sender: {s}");
        return string.Join(" | ", parts);
    }

    internal static string FormatTags(JsonElement raw)
    {
        if (!raw.TryGetProperty("tags", out var tags))
            return "";
        return tags.ValueKind == JsonValueKind.Object
            ? string.Join(", ", tags.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.True).Select(p => p.Name))
            : Join(tags);
    }
}

/// <summary>Native browser for the D2PFX catalogue, backed by the d2pfx plugin's Python API.</summary>
public sealed partial class D2pfxPageViewModel(AppSession session, IImageLoader images, Func<Task> modsChanged)
    : PageViewModel("d2pfx")
{
    public const string PluginId = "d2pfx";

    private List<D2pfxCategory> _categories = [];
    private List<D2pfxInstalled> _installed = [];
    private CancellationTokenSource? _searchDelay;
    private CancellationTokenSource? _messageDelay;
    private bool _loaded;

    public override string Title => session.Loc["tab_d2pfx"];

    public Localizer Loc => session.Loc;

    public ObservableCollection<D2pfxCategory> Categories { get; } = [];

    public ObservableCollection<D2pfxModViewModel> Mods { get; } = [];

    [ObservableProperty]
    public partial D2pfxCategory? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string CategorySearch { get; set; } = "";

    [ObservableProperty]
    public partial string ModSearch { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoadingCategories { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoadingMods { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewOpen))]
    public partial D2pfxModViewModel? PreviewMod { get; set; }

    [ObservableProperty]
    public partial Bitmap? PreviewImage { get; set; }

    public bool IsPreviewOpen => PreviewMod is not null;
    public bool IsEmpty => !IsLoadingMods && Mods.Count == 0;

    /// <summary>Delay before a typed search hits the catalogue; zero in tests.</summary>
    public TimeSpan SearchDelay { get; init; } = TimeSpan.FromMilliseconds(300);

    public override async Task ActivateAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            await LoadCategoriesAsync();
        }
        else
        {
            await RefreshInstalledAsync();
        }
    }

    partial void OnCategorySearchChanged(string value) => FilterCategories();

    partial void OnSelectedCategoryChanged(D2pfxCategory? value)
    {
        if (value is not null)
            _ = LoadModsAsync();
    }

    partial void OnModSearchChanged(string value) => _ = SearchLaterAsync();

    public async Task LoadCategoriesAsync()
    {
        IsLoadingCategories = true;
        try
        {
            _categories = await CallAsync<List<D2pfxCategory>>("get_categories") ?? [];
            FilterCategories();
            SelectedCategory ??= _categories.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ShowMessage($"Error loading categories: {ex.Message}");
        }
        finally
        {
            IsLoadingCategories = false;
        }
    }

    private void FilterCategories()
    {
        Categories.Clear();
        foreach (var c in _categories.Where(c => c.Name.Contains(CategorySearch.Trim(), StringComparison.OrdinalIgnoreCase)))
            Categories.Add(c);
    }

    private async Task SearchLaterAsync()
    {
        _searchDelay?.Cancel();
        var cts = _searchDelay = new CancellationTokenSource();
        try
        {
            if (SearchDelay > TimeSpan.Zero)
                await Task.Delay(SearchDelay, cts.Token);
            await LoadModsAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task LoadModsAsync()
    {
        if (SelectedCategory is not { } category)
            return;
        IsLoadingMods = true;
        try
        {
            var mods = await CallAsync("get_mods", new { cat_id = category.Id, search = ModSearch });
            await RefreshInstalledAsync(applyToCards: false);
            Mods.Clear();
            if (mods.ValueKind == JsonValueKind.Array)
                foreach (var raw in mods.EnumerateArray())
                    Mods.Add(CreateCard(category.Id, raw));
            ApplyInstalledState();
        }
        catch (Exception ex)
        {
            Mods.Clear();
            ShowMessage($"Error loading mods: {ex.Message}");
        }
        finally
        {
            IsLoadingMods = false;
        }
    }

    private D2pfxModViewModel CreateCard(string categoryId, JsonElement raw)
    {
        var card = new D2pfxModViewModel(this, categoryId, raw);
        if (card.PreviewUrl is { } url)
            _ = LoadPreviewAsync(card, url);
        return card;
    }

    private async Task LoadPreviewAsync(D2pfxModViewModel card, string url) =>
        card.Preview = await images.LoadAsync(url, 480);

    public async Task RefreshInstalledAsync(bool applyToCards = true)
    {
        try
        {
            _installed = await CallAsync<List<D2pfxInstalled>>("get_installed_mods") ?? [];
        }
        catch (Exception)
        {
            _installed = [];
        }
        if (applyToCards)
            ApplyInstalledState();
    }

    private D2pfxInstalled? InstalledFor(D2pfxModViewModel mod) =>
        _installed.FirstOrDefault(i => i.Name == mod.Name && i.Category == mod.CategoryId && (i.Label ?? "") == (mod.Label ?? ""));

    private void ApplyInstalledState()
    {
        foreach (var mod in Mods)
        {
            var installed = InstalledFor(mod);
            mod.IsInstalled = installed is not null;
            mod.SetEnabledSilently(installed?.Enabled ?? false);
            var installedDate = installed is null ? null : ParseDate(installed.SourceDate);
            mod.UpdateAvailable = installed is not null && mod.CatalogueDate is { } latest &&
                                  (installedDate is null || latest > installedDate);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    internal async Task InstallAsync(D2pfxModViewModel mod)
    {
        await RunAsync(mod, $"Installing {mod.Name}...", async () =>
        {
            await SetStateAsync(mod, true);
            var result = await CallAsync("install_mod", new { mod = mod.Raw, cat_id = mod.CategoryId });
            return Succeeded(result, out var error) ? $"Successfully installed {mod.Name}" : $"Failed: {error}";
        });
    }

    internal async Task UpdateAsync(D2pfxModViewModel mod)
    {
        mod.IsUpdating = true;
        try
        {
            await RunAsync(mod, $"Updating {mod.Name}...", async () =>
            {
                var result = await CallAsync("update_mod", new { mod = mod.Raw, cat_id = mod.CategoryId });
                return Succeeded(result, out var error) ? $"Successfully updated {mod.Name}" : $"Failed: {error}";
            });
        }
        finally
        {
            mod.IsUpdating = false;
        }
    }

    internal async Task RemoveAsync(D2pfxModViewModel mod)
    {
        await RunAsync(mod, $"Removing {mod.Name}...", async () =>
        {
            var result = await CallAsync("uninstall_mod", new { mod_name = mod.Name, cat_id = mod.CategoryId, label = mod.Label });
            return Succeeded(result, out var error) ? $"Successfully removed {mod.Name}" : $"Failed: {error}";
        });
    }

    internal async Task SetEnabledAsync(D2pfxModViewModel mod, bool enabled)
    {
        try
        {
            await SetStateAsync(mod, enabled);
            await modsChanged();
        }
        catch (Exception ex)
        {
            mod.SetEnabledSilently(!enabled);
            ShowMessage(ex.Message);
        }
    }

    private Task SetStateAsync(D2pfxModViewModel mod, bool enabled) =>
        CallAsync("set_mod_state", new { mod_name = mod.Name, cat_id = mod.CategoryId, label = mod.Label, enabled });

    private async Task RunAsync(D2pfxModViewModel mod, string progress, Func<Task<string>> action)
    {
        mod.InProgress = true;
        mod.NotifyTexts();
        ShowMessage(progress, autoClear: false);
        try
        {
            ShowMessage(await action());
            await RefreshInstalledAsync();
            await modsChanged();
        }
        catch (Exception ex)
        {
            ShowMessage($"Error: {ex.Message}");
        }
        finally
        {
            mod.InProgress = false;
            mod.NotifyTexts();
        }
    }

    [RelayCommand]
    private async Task RefreshDataAsync()
    {
        ShowMessage("Refreshing metadata cache...", autoClear: false);
        try
        {
            await CallAsync("prune_metadata_cache");
            await LoadCategoriesAsync();
            await LoadModsAsync();
            ShowMessage("Metadata cache refreshed.");
        }
        catch (Exception ex)
        {
            ShowMessage($"Refresh error: {ex.Message}");
        }
    }

    internal async Task ShowPreviewAsync(D2pfxModViewModel mod)
    {
        PreviewMod = mod;
        PreviewImage = mod.Preview;
        if (mod.PreviewUrl is { } url && await images.LoadAsync(url, 1600) is { } full && PreviewMod == mod)
            PreviewImage = full;
    }

    [RelayCommand]
    private void ClosePreview()
    {
        PreviewMod = null;
        PreviewImage = null;
    }

    private void ShowMessage(string message, bool autoClear = true)
    {
        _messageDelay?.Cancel();
        Message = message;
        if (!autoClear)
            return;
        var cts = _messageDelay = new CancellationTokenSource();
        _ = Task.Delay(TimeSpan.FromSeconds(4), cts.Token).ContinueWith(
            _ => Message = "", cts.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static bool Succeeded(JsonElement result, out string error)
    {
        var ok = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        error = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("error", out var e) ? e.ToString() : "Unknown error";
        return ok;
    }

    /// <summary>Plugin API call; the plugin reports failures as {"error": ...}.</summary>
    private async Task<JsonElement> CallAsync(string action, object? parameters = null)
    {
        var result = await session.Backend.CallAsync("call_plugin_api", PluginId, action, parameters ?? new { });
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("error", out var error) &&
            !result.TryGetProperty("success", out _))
            throw new BackendException(error.ToString());
        return result;
    }

    private async Task<T?> CallAsync<T>(string action, object? parameters = null) =>
        (await CallAsync(action, parameters)).Deserialize<T>(BackendJson.Options);

    /// <summary>Catalogue and install dates are unix seconds, numeric strings or date strings.</summary>
    public static double? ParseDate(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
        JsonValueKind.String when DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) =>
            d.ToUnixTimeMilliseconds(),
        _ => null,
    };
}

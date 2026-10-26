using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

public sealed partial class ModsPageViewModel(AppSession session) : PageViewModel("mods")
{
    private List<ModItemViewModel> _allMods = [];

    public override string Title => session.Loc["tab_mods"];

    public Localizer Loc => session.Loc;

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public async Task LoadAsync()
    {
        var mods = await session.Backend.CallAsync<List<ModInfo>>("get_mods") ?? [];
        _allMods = mods.Select(m => new ModItemViewModel(m, PersistAsync)).ToList();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await LoadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        Mods.Clear();
        foreach (var mod in _allMods.Where(m =>
                     m.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            Mods.Add(mod);
    }

    private async Task PersistAsync(ModItemViewModel mod, bool enabled)
    {
        try
        {
            await session.Backend.CallAsync("set_mods", new Dictionary<string, bool> { [mod.Name] = enabled });
        }
        catch (Exception ex)
        {
            mod.SetEnabledSilently(!enabled);
            await session.Dialogs.AlertAsync("Minify", ex.Message, "error");
        }
    }
}

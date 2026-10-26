using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

/// <summary>A group of settings: the application's own, one mod's, or one plugin's.</summary>
public sealed partial class SettingsSectionViewModel(string title, string? mod, bool isApplication, Func<SettingsSectionViewModel, Task> reset)
    : ObservableObject
{
    public string Title { get; } = title;
    public string? Mod { get; } = mod;
    public bool IsApplication { get; } = isApplication;
    public ObservableCollection<SettingItemViewModel> Items { get; } = [];
    public ObservableCollection<SettingItemViewModel> AdvancedItems { get; } = [];
    public bool HasAdvanced => AdvancedItems.Count > 0;
    public bool CanReset => IsApplication || Mod is not null;

    [RelayCommand]
    private Task ResetAsync() => reset(this);
}

public sealed partial class SettingsPageViewModel(AppSession session, Func<string, object?, Task> onApplicationSettingChanged)
    : PageViewModel("settings")
{
    public override string Title => session.Loc["tab_settings"];

    public Localizer Loc => session.Loc;

    public ObservableCollection<SettingsSectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public string AboutVersion => $"Minify {AppInfo.Version} · backend {session.Backend.BackendVersion}";

    [RelayCommand]
    private Task OpenUrlAsync(string url) => session.Dialogs.OpenUrlAsync(url);

    // Mod settings depend on which mods are enabled, so reload every time the tab is opened.
    public override Task ActivateAsync() => LoadAsync();

    /// <summary>Rebuilds the sections; returns the current values keyed by value_key.</summary>
    public async Task<Dictionary<string, JsonElement>> LoadAsync()
    {
        IsLoading = true;
        try
        {
            var data = await session.Backend.CallAsync("get_settings");
            var values = data.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object
                ? v.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
                : [];

            var sections = new List<SettingsSectionViewModel>();
            foreach (var item in data.GetProperty("schema").EnumerateArray())
            {
                var spec = SettingSpec.Parse(item, session.Loc);
                var section = SectionFor(sections, spec);
                var vm = SettingItemViewModel.Create(spec, values.GetValueOrDefault(spec.ValueKey), session, OnChangedAsync);
                if (vm is null)
                    continue;
                (spec.Advanced && section.IsApplication ? section.AdvancedItems : section.Items).Add(vm);
            }

            Sections.Clear();
            foreach (var section in sections)
                Sections.Add(section);
            return values;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private SettingsSectionViewModel SectionFor(List<SettingsSectionViewModel> sections, SettingSpec spec)
    {
        var title = spec.Plugin is not null
            ? $"Plugin: {spec.Plugin}"
            : spec.Mod is not null
                ? spec.ModDisplayName ?? spec.Mod
                : session.Loc["section_application_settings"];
        var section = sections.FirstOrDefault(s => s.Title == title);
        if (section is null)
        {
            section = new SettingsSectionViewModel(title, spec.Mod, spec.Mod is null && spec.Plugin is null, ResetAsync);
            sections.Add(section);
        }
        return section;
    }

    private async Task ResetAsync(SettingsSectionViewModel section)
    {
        if (section.IsApplication)
            await session.Backend.CallAsync("reset_native_settings");
        else if (section.Mod is not null)
            await session.Backend.CallAsync("reset_mod_settings", section.Mod);

        var values = await LoadAsync();
        if (!section.IsApplication)
            return;
        // Language and theme apply immediately, so tell the app about their defaults.
        foreach (var key in new[] { "theme", "locale", "output_locale" })
            if (values.TryGetValue(key, out var value))
                await onApplicationSettingChanged(key, SettingItemViewModel.AsString(value));
    }

    private Task OnChangedAsync(SettingSpec spec, object? value) =>
        spec.Mod is null && spec.Plugin is null ? onApplicationSettingChanged(spec.Key, value) : Task.CompletedTask;
}

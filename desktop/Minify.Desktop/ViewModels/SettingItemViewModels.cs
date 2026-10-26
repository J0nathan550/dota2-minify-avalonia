using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

/// <summary>One entry of a settings schema from ConfigService.get_settings (Minify/bin/settings.json and manifests).</summary>
public sealed record SettingSpec(
    string Key,
    string ValueKey,
    string Type,
    string Label,
    JsonElement Default,
    string? Mod,
    string? ModDisplayName,
    string? Plugin,
    bool Advanced,
    JsonElement Raw)
{
    public static SettingSpec Parse(JsonElement item, Localizer loc)
    {
        string? Str(string name) =>
            item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var key = Str("key") ?? "";
        var text = Str("text");
        var label = text switch
        {
            null or "" => loc[$"setting_{key}"],
            _ when text.StartsWith('&') => loc[text[1..]],
            _ => text,
        };
        return new SettingSpec(
            key,
            Str("value_key") ?? key,
            Str("type") ?? "",
            label,
            item.TryGetProperty("default", out var d) ? d.Clone() : default,
            Str("mod"),
            Str("mod_display_name"),
            Str("plugin"),
            item.TryGetProperty("advanced", out var a) && a.ValueKind == JsonValueKind.True,
            item.Clone());
    }

    public double? Number(string name) =>
        Raw.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

/// <summary>Base for a settings control; writes changes back through set_setting.</summary>
public abstract class SettingItemViewModel(SettingSpec spec, AppSession session, Func<SettingSpec, object?, Task> onChanged)
    : ObservableObject
{
    public SettingSpec Spec { get; } = spec;
    public string Label => Spec.Label;

    protected AppSession Session { get; } = session;

    protected async Task SaveAsync(object? value)
    {
        try
        {
            if (Spec.Mod is not null)
                await Session.Backend.CallAsync("set_setting", Spec.Key, value, Spec.Mod);
            else
                await Session.Backend.CallAsync("set_setting", Spec.Key, value);
            await onChanged(Spec, value);
        }
        catch (Exception ex)
        {
            await Session.Dialogs.AlertAsync("Minify", ex.Message, "error");
        }
    }

    public static SettingItemViewModel? Create(
        SettingSpec spec, JsonElement value, AppSession session, Func<SettingSpec, object?, Task> onChanged)
    {
        var current = value.ValueKind == JsonValueKind.Undefined ? spec.Default : value;
        return spec.Type switch
        {
            "checkbox" => new CheckboxSettingViewModel(spec, session, onChanged, current.ValueKind == JsonValueKind.True),
            "inputbox" or "text" => new TextSettingViewModel(spec, session, onChanged, AsString(current)),
            "combo" => new ComboSettingViewModel(spec, session, onChanged, AsString(current)),
            "number" => new NumberSettingViewModel(spec, session, onChanged, AsNumber(current), isSlider: false),
            "slider" => new NumberSettingViewModel(spec, session, onChanged, AsNumber(current), isSlider: true),
            "color" => new ColorSettingViewModel(spec, session, onChanged, AsString(current)),
            "list" => new ListSettingViewModel(spec, session, onChanged, current),
            "button" => new ButtonSettingViewModel(spec, session, onChanged),
            _ => null,
        };
    }

    internal static string AsString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Undefined or JsonValueKind.Null => "",
        _ => value.GetRawText(),
    };

    internal static double AsNumber(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String when double.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var n) => n,
        _ => 0,
    };
}

public sealed partial class CheckboxSettingViewModel : SettingItemViewModel
{
    private readonly bool _loading;

    public CheckboxSettingViewModel(SettingSpec spec, AppSession session, Func<SettingSpec, object?, Task> onChanged, bool value)
        : base(spec, session, onChanged)
    {
        _loading = true;
        IsChecked = value;
        _loading = false;
    }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_loading)
            _ = SaveAsync(value);
    }
}

/// <summary>Free text; saved when the box loses focus (bound with UpdateSourceTrigger=LostFocus).</summary>
public sealed partial class TextSettingViewModel : SettingItemViewModel
{
    private readonly bool _loading;

    public TextSettingViewModel(SettingSpec spec, AppSession session, Func<SettingSpec, object?, Task> onChanged, string value)
        : base(spec, session, onChanged)
    {
        _loading = true;
        Text = value;
        _loading = false;
    }

    [ObservableProperty]
    public partial string Text { get; set; }

    partial void OnTextChanged(string value)
    {
        if (!_loading)
            _ = SaveAsync(value);
    }
}

public sealed record ComboOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class ComboSettingViewModel : SettingItemViewModel
{
    private readonly bool _loading;

    public ComboSettingViewModel(SettingSpec spec, AppSession session, Func<SettingSpec, object?, Task> onChanged, string value)
        : base(spec, session, onChanged)
    {
        if (spec.Raw.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var v = AsString(item.GetProperty("value"));
                    var label = item.TryGetProperty("label", out var l) ? AsString(l) : v;
                    Options.Add(new ComboOption(v, label));
                }
                else
                {
                    var v = AsString(item);
                    Options.Add(new ComboOption(v, v));
                }
            }
        }
        // Keep a saved value that is no longer offered visible instead of silently showing another one.
        if (value.Length > 0 && Options.All(o => o.Value != value))
            Options.Insert(0, new ComboOption(value, value));
        _loading = true;
        Selected = Options.FirstOrDefault(o => o.Value == value);
        _loading = false;
    }

    public List<ComboOption> Options { get; } = [];

    [ObservableProperty]
    public partial ComboOption? Selected { get; set; }

    partial void OnSelectedChanged(ComboOption? value)
    {
        if (!_loading && value is not null)
            _ = SaveAsync(value.Value);
    }
}

public sealed partial class NumberSettingViewModel : SettingItemViewModel
{
    private readonly bool _loading;

    public NumberSettingViewModel(
        SettingSpec spec, AppSession session, Func<SettingSpec, object?, Task> onChanged, double value, bool isSlider)
        : base(spec, session, onChanged)
    {
        IsSlider = isSlider;
        IsFloat = spec.Raw.TryGetProperty("var_type", out var t) && t.GetString() == "float";
        Step = spec.Number("step") ?? (IsFloat ? 0.1 : 1);
        Minimum = spec.Number("min") ?? (isSlider ? 0 : double.MinValue);
        Maximum = spec.Number("max") ?? (isSlider ? 100 : double.MaxValue);
        _loading = true;
        Value = (decimal)Math.Clamp(value, Minimum, Maximum);
        _loading = false;
    }

    public bool IsSlider { get; }
    public bool IsFloat { get; }
    public double Step { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public string FormatString => IsFloat ? "0.##" : "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SliderValue), nameof(DisplayValue))]
    public partial decimal? Value { get; set; }

    /// <summary>Slider works in doubles and snaps to the step.</summary>
    public double SliderValue
    {
        get => (double)(Value ?? 0);
        set => Value = (decimal)(Math.Round(value / Step) * Step);
    }

    public string DisplayValue => (Value ?? 0).ToString(FormatString, CultureInfo.InvariantCulture);

    partial void OnValueChanged(decimal? value)
    {
        if (_loading || value is null)
            return;
        _ = SaveAsync(IsFloat ? (object)(double)value.Value : (long)Math.Round(value.Value));
    }
}

public sealed partial class ColorSettingViewModel : SettingItemViewModel
{
    private bool _syncing;

    public ColorSettingViewModel(SettingSpec spec, AppSession session, Func<SettingSpec, object?, Task> onChanged, string value)
        : base(spec, session, onChanged)
    {
        _syncing = true;
        Text = value;
        Color = Color.TryParse(value, out var c) ? c : Colors.Black;
        _syncing = false;
    }

    /// <summary>The stored string, e.g. "#ff8800" (some mods store more than a hex code, so it stays editable).</summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial Color Color { get; set; }

    partial void OnTextChanged(string value)
    {
        if (_syncing)
            return;
        _syncing = true;
        if (Color.TryParse(value, out var c))
            Color = c;
        _syncing = false;
        _ = SaveAsync(value);
    }

    partial void OnColorChanged(Color value)
    {
        if (_syncing)
            return;
        _syncing = true;
        Text = $"#{value.R:x2}{value.G:x2}{value.B:x2}";
        _syncing = false;
        _ = SaveAsync(Text);
    }
}

public sealed partial class ListEntryViewModel(ListSettingViewModel owner, string text) : ObservableObject
{
    [ObservableProperty]
    public partial string Text { get; set; } = text;

    partial void OnTextChanged(string value) => owner.EntryChanged();

    [RelayCommand]
    private void Remove() => owner.Remove(this);
}

public sealed partial class ListSettingViewModel : SettingItemViewModel
{
    public ListSettingViewModel(SettingSpec spec, AppSession session, Func<SettingSpec, object?, Task> onChanged, JsonElement value)
        : base(spec, session, onChanged)
    {
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                Entries.Add(new ListEntryViewModel(this, AsString(item)));
    }

    public ObservableCollection<ListEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewEntry { get; set; } = "";

    public Localizer Loc => Session.Loc;

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewEntry);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddAsync()
    {
        Entries.Add(new ListEntryViewModel(this, NewEntry.Trim()));
        NewEntry = "";
        return SaveListAsync();
    }

    internal void Remove(ListEntryViewModel entry)
    {
        Entries.Remove(entry);
        _ = SaveListAsync();
    }

    internal void EntryChanged() => _ = SaveListAsync();

    private Task SaveListAsync() => SaveAsync(Entries.Select(e => e.Text).ToList());
}

/// <summary>Runs a mod's script_utility.py function or a plugin API action.</summary>
public sealed partial class ButtonSettingViewModel(SettingSpec spec, AppSession session, Func<SettingSpec, object?, Task> onChanged)
    : SettingItemViewModel(spec, session, onChanged)
{
    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            if (Spec.Mod is not null)
                await Session.Backend.CallAsync("run_mod_function", Spec.Mod, Spec.Key);
            else if (Spec.Plugin is not null)
                await Session.Backend.CallAsync("call_plugin_api", Spec.Plugin, Spec.Key, null);
        }
        catch (Exception ex)
        {
            await Session.Dialogs.AlertAsync("Minify", ex.Message, "error");
        }
    }
}

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minify.Desktop.Backend;

namespace Minify.Desktop.ViewModels;

public sealed partial class ModItemViewModel : ObservableObject
{
    private readonly Func<ModItemViewModel, bool, Task> _persist;
    private bool _syncing;

    public ModItemViewModel(ModInfo info, Func<ModItemViewModel, bool, Task> persist)
    {
        _persist = persist;
        Name = info.Name;
        DisplayName = info.DisplayName;
        CanToggle = !info.Always && !info.Untickable;
        Untickable = info.Untickable;
        Preview = DecodePreview(info.Preview);
        _syncing = true;
        IsEnabled = info.Enabled;
        _syncing = false;
    }

    public string Name { get; }
    public string DisplayName { get; }
    public bool CanToggle { get; }
    public bool Untickable { get; }
    public Bitmap? Preview { get; }
    public bool HasPreview => Preview is not null;

    /// <summary>Shown in place of a missing preview, e.g. "D" for "#DevTools".</summary>
    public string Initial
    {
        get
        {
            var trimmed = DisplayName.TrimStart('#', ' ', '_', '-');
            var source = trimmed.Length > 0 ? trimmed : DisplayName;
            return source.Length > 0 ? source[..1].ToUpperInvariant() : "?";
        }
    }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_syncing)
            _ = _persist(this, value);
    }

    [RelayCommand]
    private void Toggle()
    {
        if (CanToggle)
            IsEnabled = !IsEnabled;
    }

    /// <summary>Reverts the checkbox without writing back, e.g. after the backend rejected a change.</summary>
    public void SetEnabledSilently(bool value)
    {
        _syncing = true;
        IsEnabled = value;
        _syncing = false;
    }

    private static Bitmap? DecodePreview(string? dataUri)
    {
        // Backend sends previews as data:image/...;base64,...
        var comma = dataUri?.IndexOf(',') ?? -1;
        if (comma < 0)
            return null;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(dataUri![(comma + 1)..]));
            return Bitmap.DecodeToWidth(stream, 480);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

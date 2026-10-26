using System.ComponentModel;

namespace Minify.Desktop.Services;

/// <summary>
/// Strings from Minify/locales via the backend, bindable as <c>{Binding Loc[button_patch]}</c>.
/// Missing keys fall back to the built-in English text, then to the key itself.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private static readonly Dictionary<string, string> Fallback = new()
    {
        ["tab_mods"] = "Mods",
        ["tab_terminal"] = "Terminal",
        ["button_patch"] = "PATCH",
        ["button_patching"] = "PATCHING...",
        ["button_uninstall"] = "UNINSTALL",
        ["button_clear"] = "Clear",
        ["placeholder_search_mods"] = "Search mods...",
        ["modal_uninstall_message"] = "This will uninstall all the mods related to minify, are you sure?",
        ["title_uninstall_mods"] = "Uninstall Mods",
        ["tab_settings"] = "Settings",
        ["title_mods"] = "Mods",
        ["title_terminal"] = "Terminal",
        ["button_refresh"] = "Refresh",
        ["button_cancel"] = "Cancel",
        ["button_yes"] = "Yes",
        ["button_dismiss"] = "Dismiss",
        ["label_autoscroll"] = "Autoscroll",
        ["label_remove_everything"] = "Remove everything",
        ["status_download_complete"] = "Download complete ({0})",
        ["status_download_failed"] = "Download failed: {0}",
    };

    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] =>
        _strings.TryGetValue(key, out var text) || Fallback.TryGetValue(key, out text) ? text : key;

    /// <summary>Python-style <c>{0}</c> placeholders, as used in Minify/locales.</summary>
    public string Format(string key, params object?[] args)
    {
        var text = this[key];
        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    public void Load(IReadOnlyDictionary<string, string> strings)
    {
        _strings = strings;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}

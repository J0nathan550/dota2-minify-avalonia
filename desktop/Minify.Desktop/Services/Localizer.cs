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
        ["button_close"] = "Close",
        ["loading"] = "Loading...",
        ["label_empty_file"] = "Empty file",
        ["label_no_mod_details"] = "No details available for this mod.",
        ["title_click_to_preview"] = "Click to preview",
        ["section_advanced_settings"] = "Advanced Settings",
        ["button_reset"] = "Reset",
        ["button_add"] = "Add",
        ["button_remove"] = "Remove",
        ["placeholder_add_item"] = "Add item...",
        ["button_run"] = "Run",
        ["section_application_settings"] = "Application Settings",
        ["title_announcement"] = "Announcement",
        ["button_ok"] = "OK",
        ["button_later"] = "Later",
        ["button_ignore"] = "Ignore",
        ["button_retry"] = "Retry",
        ["button_skip"] = "No, Skip",
        ["button_extract"] = "Yes, Extract",
        ["button_import"] = "Import",
        ["button_discard"] = "Discard",
        ["title_download_workshop_tools"] = "Download Workshop Tools",
        ["modal_workshop_tools_download_prompt"] = "Dota 2 Workshop Tools are not installed. Would you like to download it?",
        ["title_extract_workshop_tools"] = "Extract Workshop Tools",
        ["modal_workshop_tools_detected"] = "Dota 2 Workshop Tools is detected in your game files.",
        ["modal_workshop_tools_prompt"] = "Would you like to extract them for later use?",
        ["title_import_legacy_mods"] = "Import old mods",
        ["community_title"] = "Community",
        ["title_update_available"] = "Update Available",
        ["modal_update_message"] = "A new version of Dota 2 Minify is available to download.",
        ["label_current_version"] = "Current:",
        ["label_new_version"] = "New:",
        ["label_release_notes"] = "Release Notes:",
        ["button_update_now"] = "Update Now",
        ["button_ignore_version"] = "Ignore This Version",
        ["modal_import_legacy_mods"] = "Old mods from a previous installation were found. Import them now? Entries that are not migrated will be deleted.",
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

    /// <summary>The locale the strings were loaded for, e.g. "en".</summary>
    public string Locale { get; private set; } = "en";

    public void Load(IReadOnlyDictionary<string, string> strings, string locale = "en")
    {
        _strings = strings;
        Locale = locale;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}

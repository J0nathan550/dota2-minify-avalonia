using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Minify.Desktop.Views;

namespace Minify.Desktop.Services;

/// <summary>Native dialogs, used by the UI and to answer the backend's ui.pick_* / ui.alert requests.</summary>
public sealed partial class DialogService(Func<Window> owner)
{
    public async Task<object?> HandleBackendRequestAsync(string method, JsonElement p)
    {
        var title = Str(p, "title");
        var initialDir = Str(p, "initial_dir");
        var fileTypes = ParseFileTypes(p);

        switch (method)
        {
            case "pick_file":
                return (await PickFilesAsync(title, fileTypes, initialDir, allowMultiple: false)).FirstOrDefault();
            case "pick_files":
                return await PickFilesAsync(title, fileTypes, initialDir, allowMultiple: true);
            case "pick_folder":
                return await PickFolderAsync(title, initialDir);
            case "pick_save_file":
                return await PickSaveFileAsync(title, Str(p, "default_filename"), fileTypes, initialDir);
            case "alert":
                await AlertAsync(Str(p, "title") ?? "Minify", Str(p, "text") ?? "", Str(p, "msg_type") ?? "info");
                return null;
            default:
                throw new NotSupportedException($"Unknown backend request: {method}");
        }
    }

    public async Task<string[]> PickFilesAsync(
        string? title, IReadOnlyList<FilePickerFileType>? fileTypes, string? initialDir, bool allowMultiple)
    {
        var storage = owner().StorageProvider;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = fileTypes,
            SuggestedStartLocation = await TryGetFolderAsync(storage, initialDir),
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
    }

    public async Task<string?> PickFolderAsync(string? title, string? initialDir = null)
    {
        var storage = owner().StorageProvider;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = await TryGetFolderAsync(storage, initialDir),
        });
        return folders.Select(f => f.TryGetLocalPath()).OfType<string>().FirstOrDefault();
    }

    public async Task<string?> PickSaveFileAsync(
        string? title, string? defaultFileName, IReadOnlyList<FilePickerFileType>? fileTypes, string? initialDir)
    {
        var storage = owner().StorageProvider;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            FileTypeChoices = fileTypes,
            SuggestedStartLocation = await TryGetFolderAsync(storage, initialDir),
        });
        return file?.TryGetLocalPath();
    }

    public Task AlertAsync(string title, string text, string msgType = "info") =>
        new MessageDialog(title, text, msgType, confirm: false).ShowDialog<bool>(owner());

    public Task<bool> ConfirmAsync(string title, string text) =>
        new MessageDialog(title, text, "warning", confirm: true).ShowDialog<bool>(owner());

    private static async Task<IStorageFolder?> TryGetFolderAsync(IStorageProvider storage, string? path) =>
        string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : await storage.TryGetFolderFromPathAsync(path);

    private static string? Str(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>pywebview-style filters: <c>"Font Files (*.ttf;*.otf)"</c>.</summary>
    private static List<FilePickerFileType>? ParseFileTypes(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("file_types", out var types) ||
            types.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<FilePickerFileType>();
        foreach (var entry in types.EnumerateArray())
        {
            var match = FileTypePattern().Match(entry.GetString() ?? "");
            if (!match.Success)
                continue;
            var patterns = match.Groups[2].Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x == "*.*" ? "*" : x)
                .ToArray();
            result.Add(new FilePickerFileType(match.Groups[1].Value.Trim()) { Patterns = patterns });
        }
        return result.Count > 0 ? result : null;
    }

    [GeneratedRegex(@"^(.*?)\s*\((.*)\)\s*$")]
    private static partial Regex FileTypePattern();
}

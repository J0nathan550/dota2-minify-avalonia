using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Minify.Desktop.ViewModels;
using Minify.Desktop.Views;

namespace Minify.Desktop.Services;

public sealed record DialogButton(string Id, string Label, bool IsDefault = false, bool IsCancel = false, bool IsAccent = false);

public sealed record DialogRequest(
    string Title,
    string Text,
    IReadOnlyList<DialogButton> Buttons,
    string MsgType = "info",
    string? OptionText = null);

/// <summary>Which button closed the dialog (null when closed from the title bar) and the option checkbox state.</summary>
public sealed record DialogResult(string? ButtonId, bool OptionChecked = false);

public interface IDialogService
{
    Task<DialogResult> ShowAsync(DialogRequest request);
    Task<object?> HandleBackendRequestAsync(string method, JsonElement parameters);
    Task<string?> PickFolderAsync(string? title, string? initialDir = null);
    Task OpenUrlAsync(string url);
    Task ShowModDetailsAsync(ModDetailsViewModel details);
}

public static class DialogServiceExtensions
{
    public static Task AlertAsync(this IDialogService dialogs, string title, string text, string msgType = "info") =>
        dialogs.ShowAsync(new DialogRequest(title, text, [new DialogButton("ok", "OK", IsDefault: true, IsAccent: true)], msgType));

    public static async Task<bool> ConfirmAsync(
        this IDialogService dialogs, string title, string text, string yes = "OK", string cancel = "Cancel")
    {
        var result = await dialogs.ShowAsync(new DialogRequest(title, text,
        [
            new DialogButton("cancel", cancel, IsCancel: true),
            new DialogButton("yes", yes, IsDefault: true, IsAccent: true),
        ], "warning"));
        return result.ButtonId == "yes";
    }
}

/// <summary>Native dialogs, used by the UI and to answer the backend's ui.pick_* / ui.alert requests.</summary>
public sealed partial class DialogService(Func<Window> owner) : IDialogService
{
    public async Task<DialogResult> ShowAsync(DialogRequest request) =>
        await new DialogWindow(request).ShowDialog<DialogResult?>(owner()) ?? new DialogResult(null);

    public Task ShowModDetailsAsync(ModDetailsViewModel details) =>
        new ModDetailsWindow { DataContext = details }.ShowDialog(owner());

    public async Task OpenUrlAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await owner().Launcher.LaunchUriAsync(uri);
    }

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
                await this.AlertAsync(Str(p, "title") ?? "Minify", Str(p, "text") ?? "", Str(p, "msg_type") ?? "info");
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

    private static async Task<IStorageFolder?> TryGetFolderAsync(IStorageProvider storage, string? path) =>
        string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : await storage.TryGetFolderFromPathAsync(path);

    private static string? Str(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>pywebview-style filters: <c>"Font Files (*.ttf;*.otf)"</c>.</summary>
    internal static List<FilePickerFileType>? ParseFileTypes(JsonElement p)
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

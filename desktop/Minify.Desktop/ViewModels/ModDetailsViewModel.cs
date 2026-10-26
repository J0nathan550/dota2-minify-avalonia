using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

public sealed record CodeLine(int Number, string Text);

/// <summary>A file or folder in a mod's file tree, with a human-readable size.</summary>
public sealed record FileNodeViewModel(string Name, bool IsDirectory, string Size, IReadOnlyList<FileNodeViewModel> Children)
{
    public static FileNodeViewModel From(FileNode node) => new(
        node.Name,
        node.Type == "directory",
        FormatSize(node.Size),
        (node.Children ?? []).Select(From).ToList());

    public static string FormatSize(long? bytes) => bytes switch
    {
        null or 0 => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.0} MB",
    };
}

public sealed class ModMethodViewModel
{
    public ModMethodViewModel(ModMethod method)
    {
        Name = method.Name;
        Type = method.Type;
        Badge = method.Badge;
        if (method.Type == "tree" && method.Tree is not null)
        {
            Tree = method.Tree.Children?.Select(FileNodeViewModel.From).ToList() ?? [];
            return;
        }
        var content = method.Type == "json" ? PrettyJson(method.Content ?? "") : method.Content ?? "";
        Lines = content.Length == 0
            ? []
            : content.Replace("\r\n", "\n").Split('\n').Select((text, i) => new CodeLine(i + 1, text)).ToList();
    }

    public string Name { get; }
    public string Type { get; }
    public string? Badge { get; }
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    public IReadOnlyList<FileNodeViewModel>? Tree { get; }
    public IReadOnlyList<CodeLine> Lines { get; } = [];
    public bool IsTree => Tree is not null;
    public bool HasLines => Lines.Count > 0;
    public bool IsEmpty => !IsTree && !HasLines;

    public string Icon => Type switch
    {
        "tree" => "📁",
        "blacklist" => "🚫",
        "json" => "{ }",
        "css" => "🎨",
        "python" => "🐍",
        "xml" => "</>",
        _ => "📄",
    };

    /// <summary>Re-indents plain JSON; JSON with comments (jsonc) is shown as written.</summary>
    public static string PrettyJson(string content)
    {
        if (content.Contains("//") || content.Contains("/*"))
            return content;
        try
        {
            using var document = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return content;
        }
    }
}

public sealed partial class ModDetailsViewModel(AppSession session, string modName) : ObservableObject, IDetailsViewModel
{
    public Localizer Loc => session.Loc;

    [ObservableProperty]
    public partial string Title { get; set; } = modName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(IsEmpty))]
    public partial Bitmap? Preview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes), nameof(IsEmpty))]
    public partial string? Notes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<ModMethodViewModel> Methods { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLightboxOpen { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public bool HasPreview => Preview is not null;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool IsEmpty => !IsLoading && !HasPreview && !HasNotes && Methods.Count == 0;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var details = await session.Backend.CallAsync<ModDetails>("get_mod_details", modName, session.Loc.Locale);
            if (details is null)
                return;
            Title = string.IsNullOrEmpty(details.DisplayName) ? details.Name : details.DisplayName;
            Notes = details.HasNotes && details.Notes is not null ? MarkdownText.ConvertAlerts(details.Notes) : null;
            Preview = details.HasPreview ? ModItemViewModel.DecodePreview(details.Preview, 1600) : null;
            Methods = (details.Methods ?? []).Select(m => new ModMethodViewModel(m)).ToList();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleLightbox() => IsLightboxOpen = HasPreview && !IsLightboxOpen;
}

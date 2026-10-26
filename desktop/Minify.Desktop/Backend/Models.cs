namespace Minify.Desktop.Backend;

// Shapes returned by the Python Api (Minify/ui/app.py); snake_case on the wire.

public sealed record ModInfo(
    string Name,
    string DisplayName,
    bool Enabled,
    bool Always,
    bool Untickable,
    string? Preview);

public sealed record LogEntry(string Text, string Type, string? Timestamp);

public sealed record SteamStatus(string Root, string Library, bool DotaFound);

public sealed record OperationStatus(string Status);

public sealed record PluginInfo(string Id, string Name, bool HasWebUi);

public sealed record ModDetails(
    string Name,
    string? DisplayName,
    string? Notes,
    string? Preview,
    bool HasNotes,
    bool HasPreview,
    List<ModMethod>? Methods);

/// <summary>How a mod patches the game: a file tree, or a file's text (json, css, xml, python, blacklist...).</summary>
public sealed record ModMethod(string Name, string Type, string? Content, FileNode? Tree, string? Badge);

public sealed record FileNode(string Name, string Type, string? Path, long? Size, List<FileNode>? Children);

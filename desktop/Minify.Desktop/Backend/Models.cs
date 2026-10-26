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

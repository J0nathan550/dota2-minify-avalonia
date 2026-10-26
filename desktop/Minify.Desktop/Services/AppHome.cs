namespace Minify.Desktop.Services;

/// <summary>
/// Where an installed Minify keeps user data (mods, plugins, themes, config, cache, logs, downloaded tools).
/// The install itself (an AppImage, or the app folder that updates replace) is treated as read-only.
/// </summary>
public static class AppHome
{
    public const string HomeVariable = "MINIFY_HOME";
    public const string LauncherVariable = "MINIFY_LAUNCHER";
    public const string SeedMarker = ".seeded-version";

    /// <summary>Folders shipped with the app that the backend also writes to, so they live in the data home.</summary>
    public static readonly string[] SeededFolders = ["mods", "plugins", "themes"];

    /// <summary>
    /// <c>MINIFY_HOME</c> if set; for an AppImage the XDG data folder; otherwise <c>data/</c> next to the app.
    /// </summary>
    public static string Resolve(IReadOnlyDictionary<string, string?> environment, string appDirectory)
    {
        if (environment.GetValueOrDefault(HomeVariable) is { Length: > 0 } explicitHome)
            return Path.GetFullPath(explicitHome);

        if (environment.GetValueOrDefault("APPIMAGE") is { Length: > 0 })
        {
            var dataHome = environment.GetValueOrDefault("XDG_DATA_HOME") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(environment.GetValueOrDefault("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(dataHome, "dota2-minify");
        }

        return Path.Combine(appDirectory, "data");
    }

    /// <summary>The program Steam should call for `prelaunch`: the AppImage file, or this executable.</summary>
    public static string Launcher(IReadOnlyDictionary<string, string?> environment) =>
        environment.GetValueOrDefault("APPIMAGE") is { Length: > 0 } appImage
            ? appImage
            : Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Minify");

    public static IReadOnlyDictionary<string, string?> CurrentEnvironment() =>
        Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value);

    /// <summary>
    /// Copies the bundled mods/plugins/themes into the data home once per app version. Bundled files are
    /// overwritten with the new version's; anything the user added is left alone.
    /// </summary>
    public static bool SeedIfNeeded(string seedDirectory, string home, string version)
    {
        Directory.CreateDirectory(home);
        var marker = Path.Combine(home, SeedMarker);
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == version)
            return false;

        foreach (var folder in SeededFolders)
        {
            var source = Path.Combine(seedDirectory, folder);
            if (Directory.Exists(source))
                CopyTree(source, Path.Combine(home, folder));
        }
        File.WriteAllText(marker, version);
        return true;
    }

    public static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }
}

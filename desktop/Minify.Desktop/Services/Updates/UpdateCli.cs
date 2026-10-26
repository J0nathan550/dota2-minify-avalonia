using System.Diagnostics;

namespace Minify.Desktop.Services.Updates;

/// <summary>
/// Update commands that run without a window:
/// <c>--app-version</c>, <c>--update-now [--feed SOURCE] [--no-restart]</c>, and the internal
/// <c>--apply-update TARGET --wait-pid PID [--no-restart]</c> used by Windows updates.
/// </summary>
public static class UpdateCli
{
    public const int Updated = 0;
    public const int Failed = 1;
    public const int UpToDate = 3;

    public static bool IsUpdateCommand(IReadOnlyList<string> args) =>
        args.Count > 0 && args[0] is "--app-version" or "--update-now" or "--apply-update";

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        try
        {
            return args[0] switch
            {
                "--app-version" => PrintVersion(output),
                "--update-now" => UpdateNowAsync(args, output).GetAwaiter().GetResult(),
                _ => ApplyStaged(args, output),
            };
        }
        catch (Exception ex)
        {
            output.WriteLine($"Update failed: {ex.Message}");
            return Failed;
        }
    }

    private static int PrintVersion(TextWriter output)
    {
        output.WriteLine(AppInfo.Version);
        return Updated;
    }

    private static async Task<int> UpdateNowAsync(IReadOnlyList<string> args, TextWriter output)
    {
        var restart = !args.Contains("--no-restart");
        var updater = new Updater(Http.Client, GitHubReleaseFeed.FromEnvironment(Http.Client, Option(args, "--feed")), Installation.Detect());
        var update = await updater.CheckAsync(AppInfo.Version);
        if (update is null)
        {
            output.WriteLine($"Minify {AppInfo.Version} is up to date.");
            return UpToDate;
        }
        output.WriteLine($"Updating Minify {AppInfo.Version} -> {update.Version}...");
        var download = await updater.DownloadAsync(update);
        var next = updater.Install(download, update, Environment.ProcessId, restart);
        // AppImage: the file is already replaced, starting it is only a restart. Windows: this finishes the update.
        if (restart || updater.Installation.Kind == InstallKind.WindowsFolder)
            Process.Start(next)?.Dispose();
        output.WriteLine($"Updated to {update.Version}.");
        return Updated;
    }

    private static int ApplyStaged(IReadOnlyList<string> args, TextWriter output)
    {
        var target = Option(args, "--apply-update") ?? throw new UpdateException("--apply-update needs the app folder.");
        var pid = int.TryParse(Option(args, "--wait-pid"), out var p) ? p : (int?)null;
        Updater.ApplyStaged(AppContext.BaseDirectory, target, pid);
        if (!args.Contains("--no-restart"))
            Process.Start(new ProcessStartInfo(Path.Combine(target, "Minify.exe")) { UseShellExecute = false })?.Dispose();
        output.WriteLine($"Installed Minify {AppInfo.Version} into {target}.");
        return Updated;
    }

    private static string? Option(IReadOnlyList<string> args, string name)
    {
        var index = args.ToList().IndexOf(name);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }
}

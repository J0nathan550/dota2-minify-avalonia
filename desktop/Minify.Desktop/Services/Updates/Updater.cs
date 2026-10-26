using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Minify.Desktop.Services.Updates;

/// <summary>How this copy of Minify was installed, which decides how it can update itself.</summary>
public enum InstallKind
{
    /// <summary>Running from source or an unknown layout: updates are downloaded by hand.</summary>
    Unsupported,

    /// <summary>A Linux AppImage: the file is replaced.</summary>
    AppImage,

    /// <summary>A Windows app folder (the release zip): the folder's files are replaced.</summary>
    WindowsFolder,
}

/// <summary>Where this copy runs from.</summary>
public sealed record Installation(InstallKind Kind, string Location)
{
    /// <summary>Release asset names end with this, e.g. Minify-1.0.0.2-linux-x86_64.AppImage.</summary>
    public string? AssetSuffix => Kind switch
    {
        InstallKind.AppImage => "-linux-x86_64.AppImage",
        InstallKind.WindowsFolder => "-win-x64.zip",
        _ => null,
    };

    public static Installation Detect() => Detect(
        Environment.GetEnvironmentVariable("APPIMAGE"),
        OperatingSystem.IsWindows(),
        AppContext.BaseDirectory);

    public static Installation Detect(string? appImage, bool isWindows, string appDirectory)
    {
        if (!string.IsNullOrEmpty(appImage))
            return new Installation(InstallKind.AppImage, appImage);
        // A release folder has the frozen backend next to the app; a dev build doesn't.
        if (isWindows && File.Exists(Path.Combine(appDirectory, "backend", "minify-backend.exe")))
            return new Installation(InstallKind.WindowsFolder, appDirectory);
        return new Installation(InstallKind.Unsupported, appDirectory);
    }
}

/// <summary>An available newer release and the download that fits this installation.</summary>
public sealed record UpdateInfo(Version Version, Release Release, ReleaseAsset? Asset)
{
    public string Notes => Release.Body ?? "";
    public string? PageUrl => Release.HtmlUrl;
    public bool CanInstall => Asset is not null;
}

public sealed class UpdateException(string message) : Exception(message);

/// <summary>Checks for, downloads and installs new releases of the desktop app.</summary>
public sealed class Updater(HttpClient http, IReleaseFeed feed, Installation installation)
{
    public const string UpdateFolder = ".update";
    public const string DataFolder = "data";

    public Installation Installation { get; } = installation;

    public static Version? ParseTag(string tag) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var version) ? version : null;

    /// <summary>The latest release if it is newer than <paramref name="current"/>.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        var release = await feed.GetLatestAsync(cancellationToken);
        if (release is null || ParseTag(release.TagName) is not { } latest || latest <= current)
            return null;
        var asset = Installation.AssetSuffix is { } suffix
            ? release.Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            : null;
        return new UpdateInfo(latest, release, asset);
    }

    /// <summary>Downloads the update next to where it will be installed and verifies its checksum.</summary>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<(long Done, long Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var asset = update.Asset ?? throw new UpdateException("This release has no download for this platform.");
        // Same directory as the target, so the final move is a rename on the same disk.
        var directory = Installation.Kind == InstallKind.AppImage
            ? Path.GetDirectoryName(Installation.Location)!
            : Path.Combine(Installation.Location, UpdateFolder);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $".{asset.Name}.download");

        using (var response = await http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? asset.Size;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                progress?.Report((done, total));
            }
        }

        if (asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            await using var file = File.OpenRead(target);
            var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
            if (!string.Equals(actual, digest["sha256:".Length..], StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                throw new UpdateException($"The download of {asset.Name} is corrupted (checksum mismatch).");
            }
        }
        return target;
    }

    /// <summary>
    /// Installs a downloaded update. Returns the program to start to finish: the new AppImage, or on Windows
    /// the new version's exe, which replaces the app folder once this process has exited.
    /// </summary>
    public ProcessStartInfo Install(string download, UpdateInfo update, int currentProcessId, bool restart = true)
    {
        switch (Installation.Kind)
        {
            case InstallKind.AppImage:
                ReplaceAppImage(download, Installation.Location);
                var relaunch = new ProcessStartInfo(Installation.Location) { UseShellExecute = false };
                return relaunch;

            case InstallKind.WindowsFolder:
                var staging = Path.Combine(Installation.Location, UpdateFolder, update.Version.ToString());
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
                ZipFile.ExtractToDirectory(download, staging);
                File.Delete(download);
                var stagedExe = Path.Combine(staging, "Minify.exe");
                if (!File.Exists(stagedExe))
                    throw new UpdateException("The update package has no Minify.exe.");
                var apply = new ProcessStartInfo(stagedExe) { UseShellExecute = false, WorkingDirectory = staging };
                apply.ArgumentList.Add("--apply-update");
                apply.ArgumentList.Add(Installation.Location);
                apply.ArgumentList.Add("--wait-pid");
                apply.ArgumentList.Add(currentProcessId.ToString());
                if (!restart)
                    apply.ArgumentList.Add("--no-restart");
                return apply;

            default:
                throw new UpdateException("This copy of Minify can't update itself; download the new release instead.");
        }
    }

    /// <summary>Swaps the AppImage file; a rename, so a failure never leaves a half-written app behind.</summary>
    public static void ReplaceAppImage(string download, string appImage)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(download, File.GetUnixFileMode(download) |
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        File.Move(download, appImage, overwrite: true);
    }

    /// <summary>
    /// Runs from the new version's staging folder on Windows: waits for the old app to exit, then copies the new
    /// files over the app folder. User data (<c>data/</c>) and the staging area itself are never touched.
    /// </summary>
    public static void ApplyStaged(string staging, string target, int? waitForProcessId, TimeSpan? timeout = null)
    {
        if (waitForProcessId is { } pid)
            WaitForExit(pid, timeout ?? TimeSpan.FromMinutes(1));

        foreach (var source in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(staging, source);
            var top = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (top is DataFolder or UpdateFolder)
                continue;
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            CopyWithRetry(source, destination);
        }
    }

    /// <summary>Removes the staging area left by an update (called by the updated app on its next start).</summary>
    public static void CleanUp(string appDirectory)
    {
        var folder = Path.Combine(appDirectory, UpdateFolder);
        for (var attempt = 0; attempt < 10 && Directory.Exists(folder); attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void WaitForExit(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(timeout))
                throw new UpdateException("Minify is still running; close it and try again.");
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    // Files of the exiting app can stay locked for a moment on Windows.
    private static void CopyWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(250);
            }
        }
    }
}

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;
using Minify.Desktop.Services.Updates;

namespace Minify.Desktop.Tests;

public sealed class UpdaterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("minify-update").FullName;
    private readonly Dictionary<string, byte[]> _files = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private HttpClient Http() => new(new StubHandler(request =>
        _files.TryGetValue(request.RequestUri!.AbsolutePath, out var bytes)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound)));

    private sealed class FixedFeed(Release? release) : IReleaseFeed
    {
        public Task<Release?> GetLatestAsync(CancellationToken cancellationToken) => Task.FromResult(release);
    }

    private static string Sha256(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    private Release Publish(string tag, params (string Name, byte[] Bytes)[] assets)
    {
        foreach (var (name, bytes) in assets)
            _files[$"/download/{name}"] = bytes;
        return new Release(tag, $"Minify {tag}", "- Add updater (abc123, J0nathan550)", "https://example.test/release",
            assets.Select(a => new ReleaseAsset(a.Name, $"https://example.test/download/{a.Name}", a.Bytes.Length, Sha256(a.Bytes))).ToList());
    }

    private Updater Create(Installation installation, Release? release) => new(Http(), new FixedFeed(release), installation);

    [Theory]
    [InlineData("v1.0.0.2", "1.0.0.2")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("Minify-v2.0", null)]
    public void Tags_parse_as_versions(string tag, string? expected) =>
        Assert.Equal(expected is null ? null : Version.Parse(expected), Updater.ParseTag(tag));

    [Fact]
    public void Installation_is_detected_from_how_the_app_runs()
    {
        var app = Path.Combine(_dir, "app");
        Directory.CreateDirectory(Path.Combine(app, "backend"));

        Assert.Equal(InstallKind.AppImage, Installation.Detect("/home/me/Minify.AppImage", false, app).Kind);
        Assert.Equal(InstallKind.Unsupported, Installation.Detect(null, true, app).Kind);
        File.WriteAllText(Path.Combine(app, "backend", "minify-backend.exe"), "");
        Assert.Equal(InstallKind.WindowsFolder, Installation.Detect(null, true, app).Kind);
        Assert.Equal(InstallKind.Unsupported, Installation.Detect(null, false, app).Kind);
    }

    [Fact]
    public async Task Only_newer_releases_are_offered_with_the_platform_download()
    {
        var release = Publish("v1.0.1.0",
            ("Minify-1.0.1.0-linux-x86_64.AppImage", [1]),
            ("Minify-1.0.1.0-win-x64.zip", [2]));
        var appImage = Create(new Installation(InstallKind.AppImage, "/x/Minify.AppImage"), release);

        var update = await appImage.CheckAsync(new Version(1, 0, 0, 9));
        Assert.Equal(new Version(1, 0, 1, 0), update!.Version);
        Assert.Equal("Minify-1.0.1.0-linux-x86_64.AppImage", update.Asset!.Name);
        Assert.Contains("Add updater", update.Notes);

        Assert.Null(await appImage.CheckAsync(new Version(1, 0, 1, 0)));
        Assert.Null(await appImage.CheckAsync(new Version(1, 0, 2, 0)));

        var windows = await Create(new Installation(InstallKind.WindowsFolder, _dir), release).CheckAsync(new Version(1, 0, 0, 1));
        Assert.Equal("Minify-1.0.1.0-win-x64.zip", windows!.Asset!.Name);

        var source = await Create(new Installation(InstallKind.Unsupported, _dir), release).CheckAsync(new Version(1, 0, 0, 1));
        Assert.False(source!.CanInstall);
        Assert.Equal("https://example.test/release", source.PageUrl);
    }

    [Fact]
    public async Task No_published_release_means_no_update()
    {
        Assert.Null(await Create(new Installation(InstallKind.AppImage, "/x"), null).CheckAsync(new Version(1, 0, 0, 1)));
        Assert.Null(await new GitHubReleaseFeed(Http(), "https://example.test/releases/latest").GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Feed_can_be_a_local_file_like_the_github_api()
    {
        var path = Path.Combine(_dir, "latest.json");
        await File.WriteAllTextAsync(path, """
            {"tag_name":"v1.0.0.5","name":"Minify 1.0.0.5","body":"notes","html_url":"https://x",
             "assets":[{"name":"Minify-1.0.0.5-win-x64.zip","browser_download_url":"https://x/a.zip","size":3,"digest":null}]}
            """);

        var release = await GitHubReleaseFeed.FromEnvironment(Http(), path).GetLatestAsync(CancellationToken.None);

        Assert.Equal("v1.0.0.5", release!.TagName);
        Assert.Equal("https://x/a.zip", release.Assets.Single().BrowserDownloadUrl);
        Assert.Equal(GitHubReleaseFeed.DefaultSource, GitHubReleaseFeed.FromEnvironment(Http()).Source);
    }

    [Fact]
    public async Task Corrupted_downloads_are_rejected()
    {
        var appImage = Path.Combine(_dir, "Minify.AppImage");
        File.WriteAllText(appImage, "old");
        var release = Publish("v1.0.0.2", ("Minify-1.0.0.2-linux-x86_64.AppImage", "new"u8.ToArray()));
        _files["/download/Minify-1.0.0.2-linux-x86_64.AppImage"] = "tampered"u8.ToArray();
        var updater = Create(new Installation(InstallKind.AppImage, appImage), release);

        var update = await updater.CheckAsync(new Version(1, 0, 0, 1));
        await Assert.ThrowsAsync<UpdateException>(() => updater.DownloadAsync(update!));

        Assert.Equal("old", File.ReadAllText(appImage));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task AppImage_update_replaces_the_file_and_keeps_it_executable()
    {
        var appImage = Path.Combine(_dir, "Minify.AppImage");
        File.WriteAllText(appImage, "#!/bin/sh\necho 1.0.0.1\n");
        var newBytes = Encoding.UTF8.GetBytes("#!/bin/sh\necho 1.0.0.2\n");
        var release = Publish("v1.0.0.2", ("Minify-1.0.0.2-linux-x86_64.AppImage", newBytes));
        var updater = Create(new Installation(InstallKind.AppImage, appImage), release);
        var progress = new List<(long, long)>();

        var update = await updater.CheckAsync(new Version(1, 0, 0, 1));
        var download = await updater.DownloadAsync(update!, new SyncProgress<(long, long)>(progress.Add));
        var restart = updater.Install(download, update!, Environment.ProcessId);

        Assert.Equal(newBytes, File.ReadAllBytes(appImage));
        Assert.Equal(appImage, restart.FileName);
        Assert.Equal((newBytes.Length, newBytes.Length), progress[^1]);
        Assert.False(File.Exists(download));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(appImage).HasFlag(UnixFileMode.UserExecute));
            var run = Process.Start(new ProcessStartInfo(appImage) { RedirectStandardOutput = true })!;
            Assert.Equal("1.0.0.2", run.StandardOutput.ReadToEnd().Trim());
        }
    }

    [Fact]
    public async Task Windows_update_is_staged_then_applied_without_touching_user_data()
    {
        // Installed version.
        var app = Path.Combine(_dir, "Minify");
        Write(Path.Combine(app, "Minify.exe"), "old app");
        Write(Path.Combine(app, "backend", "minify-backend.exe"), "old backend");
        Write(Path.Combine(app, "backend", "seed", "mods", "Removed Mod", "manifest.json"), "{}");
        Write(Path.Combine(app, "data", "config", "minify_config.json"), "user settings");

        // Release zip, as packaged by the pipeline.
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text)
            {
                using var entry = new StreamWriter(archive.CreateEntry(name).Open());
                entry.Write(text);
            }
            Add("Minify.exe", "new app");
            Add("backend/minify-backend.exe", "new backend");
            Add("backend/seed/themes/dark.css", "new theme");
            Add("data/config/minify_config.json", "must not overwrite user data");
        }
        var release = Publish("v1.0.0.2", ("Minify-1.0.0.2-win-x64.zip", zip.ToArray()));
        var updater = Create(new Installation(InstallKind.WindowsFolder, app), release);

        var update = await updater.CheckAsync(new Version(1, 0, 0, 1));
        var download = await updater.DownloadAsync(update!);
        var apply = updater.Install(download, update!, currentProcessId: 4242, restart: false);

        var staging = Path.Combine(app, ".update", "1.0.0.2");
        Assert.Equal(Path.Combine(staging, "Minify.exe"), apply.FileName);
        Assert.Equal(["--apply-update", app, "--wait-pid", "4242", "--no-restart"], apply.ArgumentList);
        Assert.Equal("old app", File.ReadAllText(Path.Combine(app, "Minify.exe")));

        // What the staged Minify.exe does once the old one has exited.
        using var oldApp = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true })!;
        Updater.ApplyStaged(staging, app, oldApp.Id);
        Assert.True(oldApp.HasExited);

        Assert.Equal("new app", File.ReadAllText(Path.Combine(app, "Minify.exe")));
        Assert.Equal("new backend", File.ReadAllText(Path.Combine(app, "backend", "minify-backend.exe")));
        Assert.Equal("new theme", File.ReadAllText(Path.Combine(app, "backend", "seed", "themes", "dark.css")));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(app, "data", "config", "minify_config.json")));

        Updater.CleanUp(app);
        Assert.False(Directory.Exists(Path.Combine(app, ".update")));
        Assert.True(Directory.Exists(Path.Combine(app, "data")));
    }

    [Fact]
    public async Task Source_checkouts_cannot_install_updates()
    {
        var release = Publish("v1.0.0.2", ("Minify-1.0.0.2-win-x64.zip", [1]));
        var updater = Create(new Installation(InstallKind.Unsupported, _dir), release);

        var update = await updater.CheckAsync(new Version(1, 0, 0, 1));

        await Assert.ThrowsAsync<UpdateException>(() => updater.DownloadAsync(update!));
    }

    [Fact]
    public void Cli_reports_the_app_version_and_up_to_date_feeds()
    {
        var output = new StringWriter();
        Assert.Equal(UpdateCli.Updated, UpdateCli.Run(["--app-version"], output));
        Assert.Equal(AppInfo.Version.ToString(), output.ToString().Trim());

        var feed = Path.Combine(_dir, "latest.json");
        File.WriteAllText(feed, JsonSerializer.Serialize(new { tag_name = "v0.0.0.1", assets = Array.Empty<object>() }));
        output = new StringWriter();
        Assert.Equal(UpdateCli.UpToDate, UpdateCli.Run(["--update-now", "--feed", feed, "--no-restart"], output));
        Assert.Contains("is up to date", output.ToString());

        File.WriteAllText(feed, JsonSerializer.Serialize(new { tag_name = "v999.0.0.0", assets = Array.Empty<object>() }));
        output = new StringWriter();
        Assert.Equal(UpdateCli.Failed, UpdateCli.Run(["--update-now", "--feed", feed, "--no-restart"], output));
        Assert.Contains("Update failed", output.ToString());
    }

    [Theory]
    [InlineData(new[] { "--app-version" }, true)]
    [InlineData(new[] { "--update-now", "--no-restart" }, true)]
    [InlineData(new[] { "--apply-update", "C:\\Minify", "--wait-pid", "1" }, true)]
    [InlineData(new[] { "patch" }, false)]
    public void Update_commands_are_recognized(string[] args, bool expected) =>
        Assert.Equal(expected, UpdateCli.IsUpdateCommand(args));

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Progress&lt;T&gt; posts to a sync context; tests need the callbacks immediately.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

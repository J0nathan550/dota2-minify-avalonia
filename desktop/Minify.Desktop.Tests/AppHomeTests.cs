using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.Tests;

public sealed class AppHomeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("minify-home").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Dictionary<string, string?> Env(params (string, string)[] values) =>
        values.ToDictionary(v => v.Item1, v => (string?)v.Item2);

    [Fact]
    public void Home_is_explicit_then_xdg_for_appimages_then_next_to_the_app()
    {
        Assert.Equal(Path.GetFullPath("/custom/home"), AppHome.Resolve(Env(("MINIFY_HOME", "/custom/home"), ("APPIMAGE", "/a.AppImage")), "/opt/app"));
        Assert.Equal(Path.Combine("/x/data", "dota2-minify"), AppHome.Resolve(Env(("APPIMAGE", "/a.AppImage"), ("XDG_DATA_HOME", "/x/data")), "/opt/app"));
        Assert.Equal(Path.Combine("/home/me", ".local", "share", "dota2-minify"), AppHome.Resolve(Env(("APPIMAGE", "/a.AppImage"), ("HOME", "/home/me")), "/opt/app"));
        Assert.Equal(Path.Combine("/opt/app", "data"), AppHome.Resolve(Env(), "/opt/app"));
    }

    [Fact]
    public void Launcher_is_the_appimage_when_running_from_one()
    {
        Assert.Equal("/home/me/Minify.AppImage", AppHome.Launcher(Env(("APPIMAGE", "/home/me/Minify.AppImage"))));
        Assert.Equal(Environment.ProcessPath, AppHome.Launcher(Env()));
    }

    [Fact]
    public void Seeding_copies_bundled_content_once_per_version_and_keeps_user_files()
    {
        var seed = Path.Combine(_dir, "seed");
        var home = Path.Combine(_dir, "home");
        Write(Path.Combine(seed, "mods", "Dark Terrain", "manifest.json"), "v1");
        Write(Path.Combine(seed, "themes", "dark.css"), "dark v1");
        Write(Path.Combine(home, "mods", "My Mod", "styling.css"), "mine");

        Assert.True(AppHome.SeedIfNeeded(seed, home, "1.0.0.1"));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(home, "mods", "Dark Terrain", "manifest.json")));
        Assert.Equal("dark v1", File.ReadAllText(Path.Combine(home, "themes", "dark.css")));

        // Same version: nothing is copied again (the user may have edited bundled files).
        Write(Path.Combine(home, "themes", "dark.css"), "user edit");
        Assert.False(AppHome.SeedIfNeeded(seed, home, "1.0.0.1"));
        Assert.Equal("user edit", File.ReadAllText(Path.Combine(home, "themes", "dark.css")));

        // New version: bundled files are refreshed, the user's own mod survives.
        Write(Path.Combine(seed, "mods", "Dark Terrain", "manifest.json"), "v2");
        Assert.True(AppHome.SeedIfNeeded(seed, home, "1.0.0.2"));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(home, "mods", "Dark Terrain", "manifest.json")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(home, "mods", "My Mod", "styling.css")));
        Assert.Equal("1.0.0.2", File.ReadAllText(Path.Combine(home, AppHome.SeedMarker)));
    }

    [Fact]
    public void Bundled_backend_runs_with_a_seeded_data_home()
    {
        var app = Path.Combine(_dir, "app");
        var backend = Path.Combine(app, "backend", BackendCommand.BundledName);
        Write(backend, "");
        Write(Path.Combine(app, "backend", "seed", "plugins", "d2pfx", "manifest.json"), "{}");

        var command = BackendCommand.Resolve(Env(), app);

        Assert.Equal(backend, command.FileName);
        Assert.Equal(["rpc"], command.Arguments);
        var home = command.Environment![AppHome.HomeVariable];
        Assert.Equal(Path.Combine(app, "data"), home);
        Assert.True(File.Exists(Path.Combine(home, "plugins", "d2pfx", "manifest.json")));
        Assert.Equal(Environment.ProcessPath, command.Environment[AppHome.LauncherVariable]);
        Assert.Equal("1", command.Environment["MINIFY_NONINTERACTIVE"]);
    }

    [Fact]
    public void Override_pointing_to_a_source_folder_runs_python_in_place()
    {
        var source = Directory.CreateDirectory(Path.Combine(_dir, "Minify")).FullName;

        var command = BackendCommand.Resolve(Env(("MINIFY_BACKEND", source)), _dir);

        Assert.Equal(source, command.Arguments[^2]);
        Assert.Equal("rpc", command.Arguments[^1]);
        Assert.Equal(_dir, command.WorkingDirectory);
        Assert.Equal(new Dictionary<string, string> { ["MINIFY_NONINTERACTIVE"] = "1" }, command.Environment);
    }

    [Fact]
    public void Override_pointing_nowhere_fails_loudly() =>
        Assert.Throws<FileNotFoundException>(() => BackendCommand.Resolve(Env(("MINIFY_BACKEND", Path.Combine(_dir, "missing"))), _dir));

    [Fact]
    public void Cli_arguments_replace_the_rpc_mode()
    {
        var bundled = new BackendCommand("/app/backend/minify-backend", ["rpc"]);
        var source = new BackendCommand("uv", ["run", "--project", "/repo", "python", "/repo/Minify", "rpc"]);

        Assert.Equal(["prelaunch", "/games/dota2"], bundled.WithArguments(["prelaunch", "/games/dota2"]).Arguments);
        Assert.Equal(["run", "--project", "/repo", "python", "/repo/Minify", "patch"], source.WithArguments(["patch"]).Arguments);
    }

    [Theory]
    [InlineData(new[] { "prelaunch", "/games/dota2" }, true)]
    [InlineData(new[] { "patch" }, true)]
    [InlineData(new[] { "--version" }, true)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--some-avalonia-flag" }, false)]
    public void Cli_commands_are_recognized(string[] args, bool expected) =>
        Assert.Equal(expected, CliForwarder.IsCliCommand(args));

    [Fact]
    public void Cli_forwarding_runs_the_real_backend_and_returns_its_exit_code()
    {
        BackendCommand command;
        try
        {
            command = BackendCommand.Resolve(new Dictionary<string, string?>(), AppContext.BaseDirectory);
        }
        catch (FileNotFoundException ex)
        {
            Assert.Skip($"No Python backend available: {ex.Message}");
            return;
        }

        Assert.Equal(0, CliForwarder.Run(command.WithArguments(["--version"])));
        Assert.NotEqual(0, CliForwarder.Run(command.WithArguments(["patch", "--no-such-option"])));
    }

    [Fact]
    public void Cli_forwarding_can_relay_backend_output()
    {
        // How the Windows app forwards: it has no console, so the backend's output is relayed explicitly.
        BackendCommand command;
        try
        {
            command = BackendCommand.Resolve(new Dictionary<string, string?>(), AppContext.BaseDirectory);
        }
        catch (FileNotFoundException ex)
        {
            Assert.Skip($"No Python backend available: {ex.Message}");
            return;
        }
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, CliForwarder.Run(command.WithArguments(["--version"]), output, error));
        Assert.NotEqual(0, CliForwarder.Run(command.WithArguments(["patch", "--no-such-option"]), output, error));

        Assert.Matches(@"^\S+\s*$", output.ToString().Split('\n')[0]);
        // Rich colors Typer's errors on CI (GITHUB_ACTIONS), so compare without ANSI escape codes.
        var errorText = System.Text.RegularExpressions.Regex.Replace(error.ToString(), @"\x1b\[[0-9;]*m", "");
        Assert.Contains("Usage: Minify patch", errorText);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}

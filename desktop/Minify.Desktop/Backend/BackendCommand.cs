using Minify.Desktop.Services;

namespace Minify.Desktop.Backend;

/// <summary>How to launch the Python backend.</summary>
public sealed record BackendCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    private const string OverrideVariable = "MINIFY_BACKEND";

    /// <summary>The backend never opens its own (tkinter) dialogs when started by the app.</summary>
    private static readonly Dictionary<string, string> NonInteractive = new() { ["MINIFY_NONINTERACTIVE"] = "1" };

    /// <summary>Name of the frozen backend inside the app's <c>backend/</c> folder.</summary>
    public static string BundledName => OperatingSystem.IsWindows() ? "minify-backend.exe" : "minify-backend";

    public override string ToString() => string.Join(' ', [FileName, .. Arguments]);

    /// <summary>The same program with other arguments, e.g. a CLI command instead of `rpc`.</summary>
    public BackendCommand WithArguments(IEnumerable<string> arguments)
    {
        // Source runs pass the script before the mode: [... python <Minify dir> rpc]
        var prefix = Arguments.Take(Arguments.Count - 1);
        return this with { Arguments = [.. prefix, .. arguments] };
    }

    /// <summary>
    /// 1. <c>MINIFY_BACKEND</c>: path to a backend executable or to a Minify source folder.
    /// 2. Release layout: the frozen backend in <c>backend/</c> next to this app, with user data in <see cref="AppHome"/>.
    /// 3. Development: the repository's <c>Minify/</c> folder, run through uv.
    /// </summary>
    public static BackendCommand Resolve() => Resolve(AppHome.CurrentEnvironment(), AppContext.BaseDirectory);

    public static BackendCommand Resolve(IReadOnlyDictionary<string, string?> environment, string appDirectory)
    {
        var overridePath = environment.GetValueOrDefault(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (Directory.Exists(overridePath))
                return FromSource(Path.GetFullPath(overridePath));
            if (File.Exists(overridePath))
                return new BackendCommand(Path.GetFullPath(overridePath), ["rpc"], Environment: InstalledEnvironment(environment, appDirectory));
            throw new FileNotFoundException($"{OverrideVariable} points to nothing: {overridePath}");
        }

        var bundled = Path.Combine(appDirectory, "backend", BundledName);
        if (File.Exists(bundled))
            return new BackendCommand(bundled, ["rpc"], Environment: InstalledEnvironment(environment, appDirectory));

        for (var dir = new DirectoryInfo(appDirectory); dir is not null; dir = dir.Parent)
        {
            var source = Path.Combine(dir.FullName, "Minify");
            if (File.Exists(Path.Combine(source, "__main__.py")))
                return FromSource(source);
        }

        throw new FileNotFoundException(
            $"Minify backend not found. Expected {bundled}, a source checkout, or {OverrideVariable}.");
    }

    /// <summary>An installed backend gets a writable data home, seeded with the bundled content.</summary>
    private static Dictionary<string, string> InstalledEnvironment(IReadOnlyDictionary<string, string?> environment, string appDirectory)
    {
        var home = AppHome.Resolve(environment, appDirectory);
        AppHome.SeedIfNeeded(Path.Combine(appDirectory, "backend", "seed"), home, AppInfo.Version.ToString());
        return new Dictionary<string, string>(NonInteractive)
        {
            [AppHome.HomeVariable] = home,
            [AppHome.LauncherVariable] = AppHome.Launcher(environment),
        };
    }

    private static BackendCommand FromSource(string sourceDir)
    {
        var projectRoot = Path.GetDirectoryName(sourceDir)!;
        var uv = FindOnPath("uv") ?? FindInHome(".local", "bin", "uv") ?? FindInHome(".cargo", "bin", "uv");
        if (uv is not null)
            return new BackendCommand(uv, ["run", "--project", projectRoot, "python", sourceDir, "rpc"], projectRoot, NonInteractive);

        var python = FindOnPath(OperatingSystem.IsWindows() ? "python" : "python3")
            ?? throw new FileNotFoundException("Neither uv nor Python was found to run the Minify backend from source.");
        return new BackendCommand(python, [sourceDir, "rpc"], projectRoot, NonInteractive);
    }

    private static string? FindOnPath(string name)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd" } : [""];
        var paths = (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths
            .SelectMany(dir => extensions.Select(ext => Path.Combine(dir, name + ext)))
            .FirstOrDefault(File.Exists);
    }

    private static string? FindInHome(params string[] parts)
    {
        var path = Path.Combine([System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), .. parts]);
        if (OperatingSystem.IsWindows())
            path += ".exe";
        return File.Exists(path) ? path : null;
    }
}

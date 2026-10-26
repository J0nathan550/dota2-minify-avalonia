namespace Minify.Desktop.Backend;

/// <summary>How to launch the Python backend in `rpc` mode.</summary>
public sealed record BackendCommand(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory = null)
{
    private const string OverrideVariable = "MINIFY_BACKEND";

    public override string ToString() => string.Join(' ', [FileName, .. Arguments]);

    /// <summary>
    /// 1. <c>MINIFY_BACKEND</c>: path to a backend executable or to a Minify source folder.
    /// 2. Release layout: the frozen backend in <c>backend/</c> next to this app.
    /// 3. Development: the repository's <c>Minify/</c> folder, run through uv.
    /// </summary>
    public static BackendCommand Resolve()
    {
        var overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (Directory.Exists(overridePath))
                return FromSource(Path.GetFullPath(overridePath));
            if (File.Exists(overridePath))
                return new BackendCommand(Path.GetFullPath(overridePath), ["rpc"]);
            throw new FileNotFoundException($"{OverrideVariable} points to nothing: {overridePath}");
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "backend", OperatingSystem.IsWindows() ? "Minify.exe" : "Minify");
        if (File.Exists(bundled))
            return new BackendCommand(bundled, ["rpc"]);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var source = Path.Combine(dir.FullName, "Minify");
            if (File.Exists(Path.Combine(source, "__main__.py")))
                return FromSource(source);
        }

        throw new FileNotFoundException(
            $"Minify backend not found. Expected {bundled}, a source checkout, or {OverrideVariable}.");
    }

    private static BackendCommand FromSource(string sourceDir)
    {
        var projectRoot = Path.GetDirectoryName(sourceDir)!;
        var uv = FindOnPath("uv") ?? FindInHome(".local", "bin", "uv") ?? FindInHome(".cargo", "bin", "uv");
        if (uv is not null)
            return new BackendCommand(uv, ["run", "--project", projectRoot, "python", sourceDir, "rpc"], projectRoot);

        var python = FindOnPath(OperatingSystem.IsWindows() ? "python" : "python3")
            ?? throw new FileNotFoundException("Neither uv nor Python was found to run the Minify backend from source.");
        return new BackendCommand(python, [sourceDir, "rpc"], projectRoot);
    }

    private static string? FindOnPath(string name)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd" } : [""];
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths
            .SelectMany(dir => extensions.Select(ext => Path.Combine(dir, name + ext)))
            .FirstOrDefault(File.Exists);
    }

    private static string? FindInHome(params string[] parts)
    {
        var path = Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), .. parts]);
        if (OperatingSystem.IsWindows())
            path += ".exe";
        return File.Exists(path) ? path : null;
    }
}

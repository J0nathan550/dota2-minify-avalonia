using System.Diagnostics;

namespace Minify.Desktop.Backend;

/// <summary>
/// Runs the backend's command line (patch, prelaunch, uninstall, ...) when the app is started with one,
/// so Steam's launch option can call the app itself: <c>Minify prelaunch %command%</c>.
/// </summary>
public static class CliForwarder
{
    private static readonly HashSet<string> Commands = ["patch", "prelaunch", "uninstall", "config", "mods", "-v", "--version"];

    public static bool IsCliCommand(IReadOnlyList<string> args) => args.Count > 0 && Commands.Contains(args[0]);

    public static int Run(IReadOnlyList<string> args) => Run(BackendCommand.Resolve().WithArguments(args));

    public static int Run(BackendCommand command)
    {
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            // The app has no console on Windows; don't flash one for the backend.
            CreateNoWindow = OperatingSystem.IsWindows(),
        };
        foreach (var argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);
        if (command.WorkingDirectory is not null)
            startInfo.WorkingDirectory = command.WorkingDirectory;
        foreach (var (name, value) in command.Environment ?? new Dictionary<string, string>())
            startInfo.Environment[name] = value;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {command}");
        process.WaitForExit();
        return process.ExitCode;
    }
}

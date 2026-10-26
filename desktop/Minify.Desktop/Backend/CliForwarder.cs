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

    /// <summary>
    /// On Windows the app has no console of its own, and a backend started without one would print into a hidden
    /// console of its own; its output is relayed instead. Elsewhere the backend shares the terminal (e.g. for
    /// `config` opening an editor).
    /// </summary>
    public static int Run(IReadOnlyList<string> args) => OperatingSystem.IsWindows()
        ? Run(BackendCommand.Resolve().WithArguments(args), Console.Out, Console.Error)
        : Run(BackendCommand.Resolve().WithArguments(args));

    /// <summary>Runs the command; with writers, its output is relayed to them instead of inherited.</summary>
    public static int Run(BackendCommand command, TextWriter? output = null, TextWriter? error = null)
    {
        var relay = output is not null;
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            // The app has no console on Windows; don't flash one for the backend.
            CreateNoWindow = OperatingSystem.IsWindows(),
            RedirectStandardOutput = relay,
            RedirectStandardError = relay,
        };
        foreach (var argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);
        if (command.WorkingDirectory is not null)
            startInfo.WorkingDirectory = command.WorkingDirectory;
        foreach (var (name, value) in command.Environment ?? new Dictionary<string, string>())
            startInfo.Environment[name] = value;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {command}");
        if (relay)
        {
            var copyOut = process.StandardOutput.BaseStream.CopyToAsync(Stream(output!));
            var copyErr = process.StandardError.BaseStream.CopyToAsync(Stream(error ?? output!));
            process.WaitForExit();
            Task.WaitAll(copyOut, copyErr);
            output!.Flush();
            error?.Flush();
        }
        else
        {
            process.WaitForExit();
        }
        return process.ExitCode;
    }

    // Console writers have a raw stream underneath; others (tests) get the bytes as text.
    private static Stream Stream(TextWriter writer) =>
        writer == Console.Out ? Console.OpenStandardOutput()
        : writer == Console.Error ? Console.OpenStandardError()
        : new TextWriterStream(writer);

    private sealed class TextWriterStream(TextWriter writer) : Stream
    {
        private readonly System.Text.Decoder _decoder = System.Text.Encoding.UTF8.GetDecoder();

        public override void Write(byte[] buffer, int offset, int count)
        {
            var chars = new char[_decoder.GetCharCount(buffer, offset, count)];
            _decoder.GetChars(buffer, offset, count, chars, 0);
            lock (writer)
                writer.Write(chars);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace Minify.Desktop.Backend;

/// <summary>Runs the Python backend (`Minify rpc`) as a child process and talks to it over stdio.</summary>
public sealed class BackendClient : IBackend
{
    private readonly Process _process;
    private readonly JsonLineConnection _connection;
    private readonly IUiDispatcher _dispatcher;

    private BackendClient(Process process, IUiDispatcher dispatcher)
    {
        _process = process;
        _dispatcher = dispatcher;
        _connection = new JsonLineConnection(process.StandardOutput, process.StandardInput, dispatcher);
    }

    public event Action<string, JsonElement>? EventReceived
    {
        add => _connection.EventReceived += value;
        remove => _connection.EventReceived -= value;
    }

    public event Action<int>? Exited;

    public Func<string, JsonElement, Task<object?>>? RequestHandler
    {
        get => _connection.RequestHandler;
        set => _connection.RequestHandler = value;
    }

    public string BackendVersion => _connection.Ready.IsCompletedSuccessfully ? _connection.Ready.Result : "";

    /// <summary>Starts the backend and waits for its handshake; returns once calls can be made.</summary>
    public static async Task<BackendClient> StartAsync(
        BackendCommand command, IUiDispatcher? dispatcher = null, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);
        if (command.WorkingDirectory is not null)
            startInfo.WorkingDirectory = command.WorkingDirectory;
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start backend: {command}");

        var client = new BackendClient(process, dispatcher ?? AvaloniaUiDispatcher.Instance);
        process.Exited += (_, _) => client.OnExited();
        if (process.HasExited)
            client.OnExited();

        _ = Task.Run(client._connection.RunAsync);
        _ = Task.Run(client.DrainStderrAsync);

        var version = await client._connection.Ready.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
        Trace.WriteLine($"Minify backend {version} ready");
        return client;
    }

    public Task<JsonElement> CallAsync(string method, params object?[] args) => _connection.CallAsync(method, args);

    private async Task DrainStderrAsync()
    {
        // Must be read continuously or the backend blocks once the pipe buffer fills up.
        while (await _process.StandardError.ReadLineAsync() is { } line)
            Trace.WriteLine($"[backend] {line}");
    }

    private void OnExited()
    {
        var exitCode = _process.ExitCode;
        _connection.Fail(new BackendException($"Backend exited with code {exitCode}"));
        _dispatcher.Post(() => Exited?.Invoke(exitCode));
    }

    public async ValueTask DisposeAsync()
    {
        if (_process.HasExited)
        {
            _process.Dispose();
            return;
        }
        try
        {
            // Closing stdin ends the backend's read loop, which lets it exit on its own.
            _process.StandardInput.Close();
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            _process.Kill(entireProcessTree: true);
        }
        _process.Dispose();
    }
}

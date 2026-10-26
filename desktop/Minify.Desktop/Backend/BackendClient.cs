using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;

namespace Minify.Desktop.Backend;

/// <summary>
/// Runs the Python backend (`Minify rpc`) and speaks its newline-delimited JSON protocol over stdio.
/// See Minify/ui/bridge.py for the message shapes.
/// </summary>
public sealed class BackendClient : IAsyncDisposable
{
    public const int ProtocolVersion = 1;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Process _process;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource<string> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _nextId;

    /// <summary>Raised on the UI thread for backend events such as onLogReceived.</summary>
    public event Action<string, JsonElement>? EventReceived;

    /// <summary>Raised on the UI thread when the backend process ends.</summary>
    public event Action<int>? Exited;

    /// <summary>Answers backend requests (file pickers, alerts). Invoked on the UI thread.</summary>
    public Func<string, JsonElement, Task<object?>>? RequestHandler { get; set; }

    private BackendClient(Process process)
    {
        _process = process;
    }

    /// <summary>Starts the backend and waits for its handshake; returns once calls can be made.</summary>
    public static async Task<BackendClient> StartAsync(BackendCommand command, CancellationToken cancellationToken = default)
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
        var client = new BackendClient(process);
        process.Exited += (_, _) => client.OnExited();

        if (!process.Start())
            throw new InvalidOperationException($"Could not start backend: {command}");

        _ = Task.Run(client.ReadStdoutAsync);
        _ = Task.Run(client.DrainStderrAsync);

        var version = await client._ready.Task.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
        Trace.WriteLine($"Minify backend {version} ready");
        return client;
    }

    public string BackendVersion => _ready.Task.IsCompletedSuccessfully ? _ready.Task.Result : "";

    public async Task<T?> CallAsync<T>(string method, params object?[] args)
    {
        var result = await CallAsync(method, args);
        return result.Deserialize<T>(Json);
    }

    public async Task<JsonElement> CallAsync(string method, params object?[] args)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var message = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["args"] = JsonSerializer.SerializeToNode(args, Json),
        };
        try
        {
            await SendAsync(message);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
        return await completion.Task;
    }

    private async Task SendAsync(JsonNode message)
    {
        var line = message.ToJsonString();
        await _writeLock.WaitAsync();
        try
        {
            await _process.StandardInput.WriteLineAsync(line);
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadStdoutAsync()
    {
        while (await _process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonElement message;
            try
            {
                message = JsonDocument.Parse(line).RootElement;
            }
            catch (JsonException)
            {
                Trace.WriteLine($"[backend] non-protocol output: {line}");
                continue;
            }

            if (message.TryGetProperty("event", out var eventName))
                HandleEvent(eventName.GetString() ?? "", message.TryGetProperty("data", out var data) ? data : default);
            else if (message.TryGetProperty("method", out var method))
                _ = HandleBackendRequestAsync(message.GetProperty("id").Clone(), method.GetString() ?? "", message);
            else
                HandleResponse(message);
        }
    }

    private void HandleEvent(string name, JsonElement data)
    {
        if (name == "ready")
        {
            var protocol = data.GetProperty("protocol").GetInt32();
            if (protocol != ProtocolVersion)
                _ready.TrySetException(new InvalidOperationException(
                    $"Backend speaks protocol {protocol}, this app expects {ProtocolVersion}."));
            else
                _ready.TrySetResult(data.GetProperty("version").GetString() ?? "");
            return;
        }
        Dispatcher.UIThread.Post(() => EventReceived?.Invoke(name, data));
    }

    private void HandleResponse(JsonElement message)
    {
        if (!message.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
            return;
        if (!_pending.TryRemove(id, out var completion))
            return;

        if (message.TryGetProperty("error", out var error))
            completion.TrySetException(new BackendException(error.GetString() ?? "Unknown backend error"));
        else
            completion.TrySetResult(message.TryGetProperty("result", out var result) ? result : default);
    }

    private async Task HandleBackendRequestAsync(JsonElement id, string method, JsonElement message)
    {
        var parameters = message.TryGetProperty("params", out var p) ? p : default;
        var response = new JsonObject { ["id"] = JsonValue.Create(id) };
        try
        {
            var handler = RequestHandler ?? throw new InvalidOperationException($"No handler for {method}");
            var result = await Dispatcher.UIThread.InvokeAsync(() => handler(method, parameters));
            response["result"] = JsonSerializer.SerializeToNode(result, Json);
        }
        catch (Exception ex)
        {
            response["error"] = ex.Message;
        }
        await SendAsync(response);
    }

    private async Task DrainStderrAsync()
    {
        // Must be read continuously or the backend blocks once the pipe buffer fills up.
        while (await _process.StandardError.ReadLineAsync() is { } line)
            Trace.WriteLine($"[backend] {line}");
    }

    private void OnExited()
    {
        var exitCode = _process.ExitCode;
        var failure = new BackendException($"Backend exited with code {exitCode}");
        _ready.TrySetException(failure);
        foreach (var id in _pending.Keys)
            if (_pending.TryRemove(id, out var completion))
                completion.TrySetException(failure);
        Dispatcher.UIThread.Post(() => Exited?.Invoke(exitCode));
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

public sealed class BackendException(string message) : Exception(message);

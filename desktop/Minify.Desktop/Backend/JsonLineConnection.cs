using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Minify.Desktop.Backend;

/// <summary>
/// The newline-delimited JSON protocol of Minify/ui/bridge.py over a reader/writer pair.
/// Independent of how the backend runs, so it can be tested over in-memory streams.
/// </summary>
public sealed class JsonLineConnection
{
    public const int ProtocolVersion = 1;

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly IUiDispatcher _dispatcher;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource<string> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _nextId;

    public JsonLineConnection(TextReader input, TextWriter output, IUiDispatcher dispatcher)
    {
        _input = input;
        _output = output;
        _dispatcher = dispatcher;
    }

    public event Action<string, JsonElement>? EventReceived;

    public Func<string, JsonElement, Task<object?>>? RequestHandler { get; set; }

    /// <summary>Completes with the backend version once its handshake arrives.</summary>
    public Task<string> Ready => _ready.Task;

    public async Task<JsonElement> CallAsync(string method, params object?[] args)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var message = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["args"] = JsonSerializer.SerializeToNode(args, BackendJson.Options),
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

    /// <summary>Reads messages until the backend closes its output.</summary>
    public async Task RunAsync()
    {
        while (await _input.ReadLineAsync() is { } line)
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
                _ = HandleRequestAsync(message.GetProperty("id").Clone(), method.GetString() ?? "", message);
            else
                HandleResponse(message);
        }
    }

    /// <summary>Fails the handshake and every outstanding call, e.g. when the backend died.</summary>
    public void Fail(Exception error)
    {
        _ready.TrySetException(error);
        foreach (var id in _pending.Keys)
            if (_pending.TryRemove(id, out var completion))
                completion.TrySetException(error);
    }

    private async Task SendAsync(JsonNode message)
    {
        var line = message.ToJsonString();
        await _writeLock.WaitAsync();
        try
        {
            await _output.WriteLineAsync(line);
            await _output.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void HandleEvent(string name, JsonElement data)
    {
        if (name == "ready")
        {
            var protocol = data.GetProperty("protocol").GetInt32();
            if (protocol != ProtocolVersion)
                _ready.TrySetException(new BackendException(
                    $"Backend speaks protocol {protocol}, this app expects {ProtocolVersion}."));
            else
                _ready.TrySetResult(data.GetProperty("version").GetString() ?? "");
            return;
        }
        _dispatcher.Post(() => EventReceived?.Invoke(name, data));
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

    private async Task HandleRequestAsync(JsonElement id, string method, JsonElement message)
    {
        var parameters = message.TryGetProperty("params", out var p) ? p : default;
        var response = new JsonObject { ["id"] = JsonValue.Create(id) };
        try
        {
            var handler = RequestHandler ?? throw new InvalidOperationException($"No handler for {method}");
            var result = await _dispatcher.InvokeAsync(() => handler(method, parameters));
            response["result"] = JsonSerializer.SerializeToNode(result, BackendJson.Options);
        }
        catch (Exception ex)
        {
            response["error"] = ex.Message;
        }
        await SendAsync(response);
    }
}

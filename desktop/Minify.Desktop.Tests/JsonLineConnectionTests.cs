using System.Text.Json;
using Minify.Desktop.Backend;

namespace Minify.Desktop.Tests;

public sealed class JsonLineConnectionTests
{
    private readonly PipePair _pipes = new();
    private readonly JsonLineConnection _connection;

    public JsonLineConnectionTests()
    {
        _connection = new JsonLineConnection(_pipes.FrontendInput, _pipes.FrontendOutput, InlineUiDispatcher.Instance);
        _ = Task.Run(_connection.RunAsync);
    }

    [Fact]
    public async Task Handshake_reports_backend_version()
    {
        await _pipes.SendToAppAsync(new { @event = "ready", data = new { version = "2rc9", protocol = 1 } });
        Assert.Equal("2rc9", await _connection.Ready.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Handshake_rejects_other_protocol_versions()
    {
        await _pipes.SendToAppAsync(new { @event = "ready", data = new { version = "x", protocol = 99 } });
        var error = await Assert.ThrowsAsync<BackendException>(() => _connection.Ready.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("99", error.Message);
    }

    [Fact]
    public async Task Call_sends_request_and_returns_result()
    {
        var call = _connection.CallAsync("add", 2, "x");

        var request = await _pipes.ReadFromAppAsync();
        Assert.Equal("add", request.GetProperty("method").GetString());
        Assert.Equal("[2,\"x\"]", request.GetProperty("args").GetRawText());

        await _pipes.SendToAppAsync(new { id = request.GetProperty("id").GetInt64(), result = 5 });
        Assert.Equal(5, (await call.WaitAsync(TimeSpan.FromSeconds(5))).GetInt32());
    }

    [Fact]
    public async Task Concurrent_calls_are_matched_by_id()
    {
        var first = _connection.CallAsync("a");
        var second = _connection.CallAsync("b");
        var requestA = await _pipes.ReadFromAppAsync();
        var requestB = await _pipes.ReadFromAppAsync();

        // Answer out of order.
        await _pipes.SendToAppAsync(new { id = requestB.GetProperty("id").GetInt64(), result = "B" });
        await _pipes.SendToAppAsync(new { id = requestA.GetProperty("id").GetInt64(), result = "A" });

        Assert.Equal("A", (await first.WaitAsync(TimeSpan.FromSeconds(5))).GetString());
        Assert.Equal("B", (await second.WaitAsync(TimeSpan.FromSeconds(5))).GetString());
    }

    [Fact]
    public async Task Error_response_throws_backend_exception()
    {
        var call = _connection.CallAsync("boom");
        var request = await _pipes.ReadFromAppAsync();
        await _pipes.SendToAppAsync(new { id = request.GetProperty("id").GetInt64(), error = "kaboom" });

        var error = await Assert.ThrowsAsync<BackendException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("kaboom", error.Message);
    }

    [Fact]
    public async Task Events_are_raised_with_their_data()
    {
        var received = new TaskCompletionSource<(string, JsonElement)>();
        _connection.EventReceived += (name, data) => received.TrySetResult((name, data));

        await _pipes.SendToAppAsync(new { @event = "onLogReceived", data = new { text = "hi", type = "info" } });

        var (name, data) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("onLogReceived", name);
        Assert.Equal("hi", data.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Backend_requests_are_answered_by_the_handler()
    {
        _connection.RequestHandler = (method, p) =>
            Task.FromResult<object?>($"{method}:{p.GetProperty("title").GetString()}");

        await _pipes.SendToAppAsync(new { id = "b1", method = "pick_file", @params = new { title = "Fonts" } });

        var response = await _pipes.ReadFromAppAsync();
        Assert.Equal("b1", response.GetProperty("id").GetString());
        Assert.Equal("pick_file:Fonts", response.GetProperty("result").GetString());
    }

    [Fact]
    public async Task Failing_backend_request_handler_returns_error()
    {
        _connection.RequestHandler = (_, _) => throw new InvalidOperationException("no dialogs here");

        await _pipes.SendToAppAsync(new { id = "b2", method = "alert", @params = new { } });

        var response = await _pipes.ReadFromAppAsync();
        Assert.Equal("no dialogs here", response.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Fail_rejects_pending_calls()
    {
        var call = _connection.CallAsync("slow");
        await _pipes.ReadFromAppAsync();

        _connection.Fail(new BackendException("Backend exited with code 1"));

        await Assert.ThrowsAsync<BackendException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Non_protocol_lines_are_ignored()
    {
        var call = _connection.CallAsync("x");
        var request = await _pipes.ReadFromAppAsync();
        await _pipes.BackendWriter.WriteLineAsync("Traceback (most recent call last):");
        await _pipes.SendToAppAsync(new { id = request.GetProperty("id").GetInt64(), result = true });

        Assert.True((await call.WaitAsync(TimeSpan.FromSeconds(5))).GetBoolean());
    }
}

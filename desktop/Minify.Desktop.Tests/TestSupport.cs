using System.IO.Pipelines;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Minify.Desktop;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;
using Minify.Desktop.ViewModels;

[assembly: AvaloniaTestApplication(typeof(Minify.Desktop.Tests.TestAppBuilder))]

namespace Minify.Desktop.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>In-memory stand-in for the Python backend, scripted per test.</summary>
public sealed class FakeBackend : IBackend
{
    private readonly Dictionary<string, Func<object?[], object?>> _handlers = new();

    public List<(string Method, object?[] Args)> Calls { get; } = [];
    public string BackendVersion { get; set; } = "test";
    public bool Disposed { get; private set; }

    public event Action<string, JsonElement>? EventReceived;
    public event Action<int>? Exited;
    public Func<string, JsonElement, Task<object?>>? RequestHandler { get; set; }

    public FakeBackend On(string method, Func<object?[], object?> handler)
    {
        _handlers[method] = handler;
        return this;
    }

    public FakeBackend On(string method, object? result) => On(method, _ => result);

    public Task<JsonElement> CallAsync(string method, params object?[] args)
    {
        Calls.Add((method, args));
        if (!_handlers.TryGetValue(method, out var handler))
            return Task.FromResult(JsonSerializer.SerializeToElement<object?>(null));
        try
        {
            return Task.FromResult(JsonSerializer.SerializeToElement(handler(args), BackendJson.Options));
        }
        catch (Exception ex)
        {
            return Task.FromException<JsonElement>(new BackendException(ex.Message));
        }
    }

    public void Raise(string name, object? data) =>
        EventReceived?.Invoke(name, JsonSerializer.SerializeToElement(data, BackendJson.Options));

    public void Exit(int code) => Exited?.Invoke(code);

    public IEnumerable<object?[]> CallsTo(string method) => Calls.Where(c => c.Method == method).Select(c => c.Args);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>A backend that looks like a normal install with two mods.</summary>
    public static FakeBackend WithDefaults() => new FakeBackend()
        .On("get_current_locale", "en")
        .On("get_localization", new Dictionary<string, string> { ["button_patch"] = "PATCH IT" })
        .On("get_steam_status", new { root = "/steam", library = "/steam", dota_found = true })
        .On("get_logs", Array.Empty<object>())
        .On("is_patching", false)
        .On("is_workshop_installed", true)
        .On("check_workshop_tools_needed", false)
        .On("get_legacy_mods", Array.Empty<string>())
        .On("get_plugins", new object[]
        {
            new { id = "d2pfx", name = "&tab_d2pfx", has_web_ui = false },
            new { id = "thirdparty", name = "Third Party", has_web_ui = true },
        })
        .On("get_mods", new object[]
        {
            new { name = "Dark Terrain", display_name = "Dark Terrain", enabled = true, always = false, untickable = false, preview = (string?)null },
            new { name = "#base", display_name = "#base", enabled = true, always = true, untickable = false, preview = (string?)null },
        });
}

public sealed class FakeDialogs : IDialogService
{
    /// <summary>Decides how each dialog is answered; by default the default button is pressed.</summary>
    public Func<DialogRequest, DialogResult> Answer { get; set; } =
        r => new DialogResult(r.Buttons.FirstOrDefault(b => b.IsDefault)?.Id);

    public string? FolderResult { get; set; }
    public List<DialogRequest> Shown { get; } = [];
    public List<string> OpenedUrls { get; } = [];

    public IEnumerable<DialogRequest> Alerts => Shown.Where(r => r.Buttons.Count == 1);

    public Task<DialogResult> ShowAsync(DialogRequest request)
    {
        Shown.Add(request);
        return Task.FromResult(Answer(request));
    }

    public Task<object?> HandleBackendRequestAsync(string method, JsonElement parameters) =>
        Task.FromResult<object?>($"handled:{method}");

    public Task<string?> PickFolderAsync(string? title, string? initialDir = null) => Task.FromResult(FolderResult);

    public Task OpenUrlAsync(string url)
    {
        OpenedUrls.Add(url);
        return Task.CompletedTask;
    }

    public List<ModDetailsViewModel> DetailsShown { get; } = [];

    public Task ShowModDetailsAsync(ModDetailsViewModel details)
    {
        DetailsShown.Add(details);
        return Task.CompletedTask;
    }
}

/// <summary>Two in-memory pipes: what the app writes to the backend, and what the backend writes back.</summary>
public sealed class PipePair
{
    private readonly Pipe _toBackend = new();
    private readonly Pipe _toFrontend = new();

    public PipePair()
    {
        FrontendInput = new StreamReader(_toFrontend.Reader.AsStream());
        FrontendOutput = new StreamWriter(_toBackend.Writer.AsStream()) { AutoFlush = true };
        BackendReader = new StreamReader(_toBackend.Reader.AsStream());
        BackendWriter = new StreamWriter(_toFrontend.Writer.AsStream()) { AutoFlush = true };
    }

    public TextReader FrontendInput { get; }
    public TextWriter FrontendOutput { get; }
    public StreamReader BackendReader { get; }
    public StreamWriter BackendWriter { get; }

    public async Task<JsonElement> ReadFromAppAsync()
    {
        var line = await BackendReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return JsonDocument.Parse(line!).RootElement;
    }

    public Task SendToAppAsync(object message) => BackendWriter.WriteLineAsync(JsonSerializer.Serialize(message));

    public void CloseBackend() => _toFrontend.Writer.Complete();
}

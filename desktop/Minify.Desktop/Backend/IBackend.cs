using System.Text.Json;

namespace Minify.Desktop.Backend;

/// <summary>The Python backend as seen by the UI; implemented by <see cref="BackendClient"/> and test fakes.</summary>
public interface IBackend : IAsyncDisposable
{
    string BackendVersion { get; }

    /// <summary>Backend events such as onLogReceived, raised on the UI thread.</summary>
    event Action<string, JsonElement>? EventReceived;

    /// <summary>Raised on the UI thread when the backend stops.</summary>
    event Action<int>? Exited;

    /// <summary>Answers backend requests (file pickers, alerts); invoked on the UI thread.</summary>
    Func<string, JsonElement, Task<object?>>? RequestHandler { get; set; }

    Task<JsonElement> CallAsync(string method, params object?[] args);
}

public static class BackendJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static async Task<T?> CallAsync<T>(this IBackend backend, string method, params object?[] args)
    {
        var result = await backend.CallAsync(method, args);
        return result.ValueKind == JsonValueKind.Undefined ? default : result.Deserialize<T>(Options);
    }
}

public sealed class BackendException(string message) : Exception(message);

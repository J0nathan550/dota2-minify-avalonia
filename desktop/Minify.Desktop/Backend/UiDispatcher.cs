using Avalonia.Threading;

namespace Minify.Desktop.Backend;

/// <summary>Where backend events and requests are handled; the Avalonia UI thread in the app, inline in tests.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
    Task<T> InvokeAsync<T>(Func<Task<T>> action);
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public static readonly AvaloniaUiDispatcher Instance = new();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public Task<T> InvokeAsync<T>(Func<Task<T>> action) => Dispatcher.UIThread.InvokeAsync(action);
}

public sealed class InlineUiDispatcher : IUiDispatcher
{
    public static readonly InlineUiDispatcher Instance = new();

    public void Post(Action action) => action();

    public Task<T> InvokeAsync<T>(Func<Task<T>> action) => action();
}

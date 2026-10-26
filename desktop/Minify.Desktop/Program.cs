using Avalonia;
using Minify.Desktop.Backend;

namespace Minify.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (CliForwarder.IsCliCommand(args))
            return CliForwarder.Run(args);
#if DEBUG
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener(useErrorStream: true));
#endif
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Native Wayland when a compositor is available (no XWayland upscaling), X11 otherwise.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseWaylandWithFallback()
            .With(new WaylandPlatformOptions { AppId = "dota2-minify" })
            .WithInterFont()
            .LogToTrace();
}

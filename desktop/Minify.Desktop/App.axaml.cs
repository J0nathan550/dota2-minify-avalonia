using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;
using Minify.Desktop.Services.Updates;
using Minify.Desktop.ViewModels;
using Minify.Desktop.Views;

namespace Minify.Desktop;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Leftovers of a finished Windows update.
            if (Installation.Detect().Kind == InstallKind.WindowsFolder)
                _ = Task.Run(() => Updater.CleanUp(AppContext.BaseDirectory));

            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(
                new DialogService(() => window),
                async () => await BackendClient.StartAsync(BackendCommand.Resolve()),
                new ThemeService(),
                new HttpAnnouncementSource(Http.Client, HttpAnnouncementSource.DefaultUrl),
                new RemoteImageLoader(Http.Client));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            var backendStopped = false;
            window.Closing += async (_, e) =>
            {
                // Hold the close until the Python backend has exited, so it never outlives the window.
                if (backendStopped)
                    return;
                e.Cancel = true;
                await viewModel.DisposeAsync();
                backendStopped = true;
                window.Close();
            };
            _ = viewModel.StartAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

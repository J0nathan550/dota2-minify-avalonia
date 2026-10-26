using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Views;

public sealed partial class ModDetailsWindow : Window
{
    public ModDetailsWindow() => InitializeComponent();

    private ModDetailsViewModel? ViewModel => DataContext as ModDetailsViewModel;

    // Escape closes the lightbox first, then the window.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { IsLightboxOpen: true } vm)
        {
            vm.IsLightboxOpen = false;
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OnLightboxTapped(object? sender, TappedEventArgs e) => ViewModel?.ToggleLightboxCommand.Execute(null);

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { IsLightboxOpen: true } vm)
            vm.IsLightboxOpen = false;
        else
            Close();
    }
}

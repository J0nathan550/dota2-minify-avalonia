using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Views;

public sealed partial class DetailsWindow : Window
{
    public DetailsWindow() => InitializeComponent();

    // Escape and Close shut the full-size preview first, then the window.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseLightboxOrWindow();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => CloseLightboxOrWindow();

    private void CloseLightboxOrWindow()
    {
        if (DataContext is IDetailsViewModel { IsLightboxOpen: true } vm)
            vm.IsLightboxOpen = false;
        else
            Close();
    }
}

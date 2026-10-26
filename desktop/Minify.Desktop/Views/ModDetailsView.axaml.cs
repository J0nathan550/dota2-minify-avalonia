using Avalonia.Controls;
using Avalonia.Input;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Views;

public sealed partial class ModDetailsView : UserControl
{
    public ModDetailsView() => InitializeComponent();

    private void OnLightboxTapped(object? sender, TappedEventArgs e) =>
        (DataContext as ModDetailsViewModel)?.ToggleLightboxCommand.Execute(null);
}

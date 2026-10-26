using Avalonia.Controls;
using Avalonia.Input;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Views;

public sealed partial class CommunityDetailsView : UserControl
{
    public CommunityDetailsView() => InitializeComponent();

    private void OnLightboxTapped(object? sender, TappedEventArgs e) =>
        (DataContext as CommunityDetailsViewModel)?.ToggleLightboxCommand.Execute(null);
}

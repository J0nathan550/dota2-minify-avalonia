using Avalonia.Controls;
using Avalonia.Input;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Views;

public sealed partial class D2pfxPage : UserControl
{
    public D2pfxPage() => InitializeComponent();

    private void OnPreviewTapped(object? sender, TappedEventArgs e) =>
        (DataContext as D2pfxPageViewModel)?.ClosePreviewCommand.Execute(null);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is D2pfxPageViewModel { IsPreviewOpen: true } vm)
        {
            vm.ClosePreviewCommand.Execute(null);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}

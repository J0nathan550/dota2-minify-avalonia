using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Minify.Desktop.Views;

/// <summary>Minimal modal message box; Avalonia does not ship one.</summary>
public sealed class MessageDialog : Window
{
    public MessageDialog(string title, string text, string msgType, bool confirm)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var accent = msgType switch
        {
            "error" => Brushes.IndianRed,
            "warning" => Brushes.Goldenrod,
            "success" => Brushes.MediumSeaGreen,
            _ => null,
        };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        if (confirm)
        {
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }
        buttons.Children.Add(ok);

        var message = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (accent is not null)
            message.Foreground = accent;

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 20,
            Children = { message, buttons },
        };
    }
}

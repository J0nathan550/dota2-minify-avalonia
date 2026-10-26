using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Minify.Desktop.Services;

namespace Minify.Desktop.Views;

/// <summary>Modal message box with custom buttons and an optional checkbox; Avalonia does not ship one.</summary>
public sealed class DialogWindow : Window
{
    private readonly CheckBox? _option;

    public DialogWindow(DialogRequest request)
    {
        Title = request.Title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var message = new SelectableTextBlock { Text = request.Text, TextWrapping = TextWrapping.Wrap };
        var accent = request.MsgType switch
        {
            "error" => Brushes.IndianRed,
            "warning" => Brushes.Goldenrod,
            "success" => Brushes.MediumSeaGreen,
            _ => null,
        };
        if (accent is not null)
            message.Foreground = accent;

        var body = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        body.Children.Add(message);

        if (request.OptionText is not null)
        {
            _option = new CheckBox { Content = request.OptionText };
            body.Children.Add(_option);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        foreach (var spec in request.Buttons)
        {
            var button = new Button
            {
                Content = spec.Label,
                IsDefault = spec.IsDefault,
                IsCancel = spec.IsCancel,
                MinWidth = 80,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            if (spec.IsAccent)
                button.Classes.Add("accent");
            button.Click += (_, _) => Close(new DialogResult(spec.Id, _option?.IsChecked == true));
            buttons.Children.Add(button);
        }
        body.Children.Add(buttons);
        Content = body;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.VisualTree;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Views;

public sealed partial class ModsPage : UserControl
{
    public static readonly IValueConverter EnabledOpacity =
        new FuncValueConverter<bool, double>(enabled => enabled ? 1.0 : 0.6);

    public ModsPage() => InitializeComponent();

    // Clicking anywhere on a card toggles it, except on its own controls.
    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is null &&
            source.FindAncestorOfType<CheckBox>(includeSelf: true) is null &&
            sender is Control { DataContext: ModItemViewModel mod })
            mod.ToggleCommand.Execute(null);
    }
}

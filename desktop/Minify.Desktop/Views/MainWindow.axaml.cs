using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Minify.Desktop.Views;

public sealed partial class MainWindow : Window
{
    /// <summary>Looks up an icon geometry from Views/Icons.axaml by its key.</summary>
    public static readonly IValueConverter IconByKey = new FuncValueConverter<string, Geometry?>(key =>
        key is not null && Application.Current!.TryGetResource(key, null, out var icon) ? icon as Geometry : null);

    public MainWindow() => InitializeComponent();
}

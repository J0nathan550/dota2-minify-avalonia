using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Minify.Desktop.Views;

/// <summary>Terminal colors for backend log types (error / warning / success / info).</summary>
public sealed class LogBrushConverter : IValueConverter
{
    public static readonly LogBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "error" => Brushes.IndianRed,
        "warning" => Brushes.Goldenrod,
        "success" => Brushes.MediumSeaGreen,
        _ => Brushes.Gainsboro,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

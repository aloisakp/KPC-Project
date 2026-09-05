using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using KpcLauncher.Core;

namespace KpcLauncher;

public sealed class LogLevelBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Info = Frozen("#D3D8E4");
    private static readonly SolidColorBrush Good = Frozen("#5BD98A");
    private static readonly SolidColorBrush Warn = Frozen("#E8C15C");
    private static readonly SolidColorBrush Bad = Frozen("#EC6A62");
    private static readonly SolidColorBrush Dim = Frozen("#78809A");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is LogLevel level
            ? level switch
            {
                LogLevel.Good => Good,
                LogLevel.Warn => Warn,
                LogLevel.Error => Bad,
                LogLevel.Dim => Dim,
                _ => Info,
            }
            : Info;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}



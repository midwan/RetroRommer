using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RetroRommer.Core;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = parameter is string s && string.Equals(s, "True", StringComparison.OrdinalIgnoreCase);

        if (value is not bool b) return Visibility.Collapsed;
        if (invert) b = !b;

        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility v) return v == Visibility.Visible;
        return false;
    }
}

using System;
using System.Globalization;
using System.Windows.Data;

namespace VRASDesktopApp;

public class WidthToColumnsConverter : IValueConverter
{
    public double TwoColumnThreshold { get; set; } = 520;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double w && w >= TwoColumnThreshold) return 2;
        return 1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

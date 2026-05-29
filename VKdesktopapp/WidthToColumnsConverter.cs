using System;
using System.Globalization;
using System.Windows.Data;

namespace VRASDesktopApp;

// Binds a UniformGrid's Columns to the available width so the dashboard menu
// tiles show 1 per row when the menu is narrow and reflow to 2 per row once
// the user drags the menu panel wider (past ~520px). Pure layout helper.
public class WidthToColumnsConverter : IValueConverter
{
    // Width at/above which we switch from 1 column to 2.
    public double TwoColumnThreshold { get; set; } = 520;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double w && w >= TwoColumnThreshold) return 2;
        return 1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ava.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.Black : Brushes.DarkGray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false ? Brushes.Black : Brushes.DarkGray;
}

public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeight.Black : FontWeight.Medium;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → 宽度：展开 170（NavMenu 160 + Border Padding 10），折叠 58（图标 48 + Padding 10）</summary>
public class BoolToWidthConverter : IValueConverter
{
    public double ExpandWidth { get; set; } = 170;
    public double CollapseWidth { get; set; } = 58;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ExpandWidth : CollapseWidth;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → 颜色：选中→蓝，未选中→灰</summary>
public class BoolToBlueBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.DodgerBlue : Brushes.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
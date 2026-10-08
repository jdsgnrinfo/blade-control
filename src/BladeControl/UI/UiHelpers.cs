using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace BladeControl.UI;

/// <summary>True becomes false and false true: a control enabled while something is not under way.</summary>
public sealed class NotConverter : IValueConverter
{
    public static NotConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>True when every value is true: a control enabled only while all its conditions hold.</summary>
public sealed class AllTrueConverter : IMultiValueConverter
{
    public static AllTrueConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) => values.All(value => value is true);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True hides, false shows: the other side of BooleanToVisibilityConverter.</summary>
public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The color of a note box's icon (see the NoteBox style).</summary>
public static class Note
{
    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.RegisterAttached(
        "IconBrush", typeof(Brush), typeof(Note), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits));

    public static Brush GetIconBrush(DependencyObject element) => (Brush)element.GetValue(IconBrushProperty);

    public static void SetIconBrush(DependencyObject element, Brush value) => element.SetValue(IconBrushProperty, value);
}


using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace BladeControl.UI;

/// <summary>
/// A number in a format taken from the second value: the slider bubble's
/// "{0}%" or "{0} RPM", with the value rounded to a whole number.
/// </summary>
public sealed class FormatConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = values.Length > 1 && values[1] is string text ? text : "{0}";
        var value = values.Length > 0 && values[0] is double number ? Math.Round(number) : 0;
        return string.Format(CultureInfo.InvariantCulture, format, value);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A width that is a fraction (first value, 0 to 1) of another (second value): a bar's fill.</summary>
public sealed class FractionWidthConverter : IMultiValueConverter
{
    public static FractionWidthConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [double fraction, double width] ? Math.Clamp(fraction, 0, 1) * width : 0.0;

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

/// <summary>
/// Keeps an element on a Canvas centred over the Canvas, however wide its
/// text makes it: the slider's value bubble over the thumb.
/// </summary>
public static class Centered
{
    public static readonly DependencyProperty HorizontallyProperty = DependencyProperty.RegisterAttached(
        "Horizontally", typeof(bool), typeof(Centered), new PropertyMetadata(false, OnChanged));

    public static bool GetHorizontally(DependencyObject element) => (bool)element.GetValue(HorizontallyProperty);

    public static void SetHorizontally(DependencyObject element, bool value) => element.SetValue(HorizontallyProperty, value);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement framework || e.NewValue is not true)
            return;

        framework.SizeChanged += (_, _) => Center(framework);
        framework.Loaded += (_, _) => Center(framework);
    }

    private static void Center(FrameworkElement element)
    {
        if (VisualTreeHelper.GetParent(element) is Canvas canvas)
            Canvas.SetLeft(element, (canvas.ActualWidth - element.ActualWidth) / 2);
    }
}

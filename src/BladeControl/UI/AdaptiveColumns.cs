using System.Windows;
using System.Windows.Controls;

namespace BladeControl.UI;

/// <summary>
/// A page's cards side by side while there is room for them, one under the
/// other once the window is narrower than <see cref="MinimumColumnsWidth"/>.
/// Side by side, every column gets the same width and the cards keep their own
/// heights; stacked, each takes the full width. The cards' own bottom margin
/// keeps them apart when stacked.
/// </summary>
public sealed class AdaptiveColumns : Panel
{
    public static readonly DependencyProperty MinimumColumnsWidthProperty = DependencyProperty.Register(
        nameof(MinimumColumnsWidth), typeof(double), typeof(AdaptiveColumns),
        new FrameworkPropertyMetadata(940.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnGapProperty = DependencyProperty.Register(
        nameof(ColumnGap), typeof(double), typeof(AdaptiveColumns),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Below this width the cards stack.</summary>
    public double MinimumColumnsWidth
    {
        get => (double)GetValue(MinimumColumnsWidthProperty);
        set => SetValue(MinimumColumnsWidthProperty, value);
    }

    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    private bool SideBySide(double width) => InternalChildren.Count > 1 && width >= MinimumColumnsWidth;

    private double ColumnWidth(double width) =>
        (width - ColumnGap * (InternalChildren.Count - 1)) / InternalChildren.Count;

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? MinimumColumnsWidth : available.Width;

        if (SideBySide(width))
        {
            var column = ColumnWidth(width);
            var tallest = 0.0;

            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(column, double.PositiveInfinity));
                tallest = Math.Max(tallest, child.DesiredSize.Height);
            }

            return new Size(width, tallest);
        }

        var height = 0.0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            height += child.DesiredSize.Height;
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (SideBySide(final.Width))
        {
            var column = ColumnWidth(final.Width);
            var x = 0.0;

            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new Rect(x, 0, column, child.DesiredSize.Height));
                x += column + ColumnGap;
            }

            return final;
        }

        var y = 0.0;

        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }

        return final;
    }
}

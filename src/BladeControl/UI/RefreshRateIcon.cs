using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace BladeControl.UI;

/// <summary>
/// The refresh-rate buttons' icon, as Razer Helper's: the rate in square,
/// blocky digits ("60", "120"; "A" for Auto) over a row of three thin bars
/// and a wide block, a sign of motion. Built from rectangles, so any rate a
/// panel offers gets one and it stays sharp at every size.
/// </summary>
public static class RefreshRateIcon
{
    // Design units: a digit is 10 by 14 with strokes 3.4 thick.
    private const double DigitWidth = 10;
    private const double DigitHeight = 14;
    private const double Stroke = 3.4;
    private const double DigitGap = 3;
    private const double BarsGap = 5;
    private const double BarsHeight = 6;
    private const double Corner = 0.8;

    // The thin bars and their gaps, as shares of the icon's width; the block takes the rest.
    private const int ThinBars = 3;
    private const double ThinBarShare = 0.08;
    private const double BarGapShare = 0.075;

    // A one is a stem with a short flag to its left, narrower than the rest.
    private const double OneFlag = 2.6;

    /// <summary>The icon for a rate, or for Auto when <paramref name="hertz"/> is null.</summary>
    public static Geometry For(int? hertz)
    {
        var text = hertz?.ToString(CultureInfo.InvariantCulture) ?? "A";

        // The bars are never narrower than two digits, so Auto's lone A sits
        // centred over a row as wide as 60's.
        var textWidth = text.Sum(WidthOf) + DigitGap * Math.Max(0, text.Length - 1);
        var width = Math.Max(textWidth, DigitWidth * 2 + DigitGap);

        var icon = new GeometryGroup { FillRule = FillRule.Nonzero };
        var x = (width - textWidth) / 2;

        foreach (var character in text)
            x += AddCharacter(icon, character, x) + DigitGap;

        AddBars(icon, width);
        icon.Freeze();
        return icon;
    }

    // Seven blocky strokes, as on a digital clock but solid and square.
    [Flags]
    private enum Strokes
    {
        Top = 1,
        Middle = 2,
        Bottom = 4,
        UpperLeft = 8,
        UpperRight = 16,
        LowerLeft = 32,
        LowerRight = 64
    }

    private static Strokes StrokesOf(char character) => character switch
    {
        '0' => Strokes.Top | Strokes.Bottom | Strokes.UpperLeft | Strokes.UpperRight | Strokes.LowerLeft | Strokes.LowerRight,
        '2' => Strokes.Top | Strokes.UpperRight | Strokes.Middle | Strokes.LowerLeft | Strokes.Bottom,
        '3' => Strokes.Top | Strokes.UpperRight | Strokes.Middle | Strokes.LowerRight | Strokes.Bottom,
        '4' => Strokes.UpperLeft | Strokes.UpperRight | Strokes.Middle | Strokes.LowerRight,
        '5' => Strokes.Top | Strokes.UpperLeft | Strokes.Middle | Strokes.LowerRight | Strokes.Bottom,
        '6' => Strokes.Top | Strokes.UpperLeft | Strokes.Middle | Strokes.LowerLeft | Strokes.LowerRight | Strokes.Bottom,
        '7' => Strokes.Top | Strokes.UpperRight | Strokes.LowerRight,
        '8' => Strokes.Top | Strokes.Middle | Strokes.Bottom | Strokes.UpperLeft | Strokes.UpperRight | Strokes.LowerLeft | Strokes.LowerRight,
        '9' => Strokes.Top | Strokes.UpperLeft | Strokes.UpperRight | Strokes.Middle | Strokes.LowerRight | Strokes.Bottom,
        'A' => Strokes.Top | Strokes.UpperLeft | Strokes.UpperRight | Strokes.Middle | Strokes.LowerLeft | Strokes.LowerRight,
        _ => 0
    };

    private static double WidthOf(char character) => character == '1' ? OneFlag + Stroke : DigitWidth;

    // Adds one character at x and returns its width.
    private static double AddCharacter(GeometryGroup icon, char character, double x)
    {
        if (character == '1')
        {
            AddBlock(icon, x, 0, OneFlag + Stroke, Stroke);
            AddBlock(icon, x + OneFlag, 0, Stroke, DigitHeight);
            return WidthOf(character);
        }

        var strokes = StrokesOf(character);
        var middle = (DigitHeight - Stroke) / 2;
        var right = x + DigitWidth - Stroke;

        if (strokes.HasFlag(Strokes.Top))
            AddBlock(icon, x, 0, DigitWidth, Stroke);
        if (strokes.HasFlag(Strokes.Middle))
            AddBlock(icon, x, middle, DigitWidth, Stroke);
        if (strokes.HasFlag(Strokes.Bottom))
            AddBlock(icon, x, DigitHeight - Stroke, DigitWidth, Stroke);
        if (strokes.HasFlag(Strokes.UpperLeft))
            AddBlock(icon, x, 0, Stroke, middle + Stroke);
        if (strokes.HasFlag(Strokes.UpperRight))
            AddBlock(icon, right, 0, Stroke, middle + Stroke);
        if (strokes.HasFlag(Strokes.LowerLeft))
            AddBlock(icon, x, middle, Stroke, DigitHeight - middle);
        if (strokes.HasFlag(Strokes.LowerRight))
            AddBlock(icon, right, middle, Stroke, DigitHeight - middle);

        return DigitWidth;
    }

    private static void AddBars(GeometryGroup icon, double width)
    {
        var top = DigitHeight + BarsGap;
        var thin = width * ThinBarShare;
        var gap = width * BarGapShare;
        var x = 0.0;

        for (var bar = 0; bar < ThinBars; bar++)
        {
            AddBlock(icon, x, top, thin, BarsHeight);
            x += thin + gap;
        }

        AddBlock(icon, x, top, width - x, BarsHeight);
    }

    // Slightly rounded, so the outer corners are soft; where strokes meet they overlap and fill as one.
    private static void AddBlock(GeometryGroup icon, double x, double y, double width, double height) =>
        icon.Children.Add(new RectangleGeometry(new Rect(x, y, width, height), Corner, Corner));
}

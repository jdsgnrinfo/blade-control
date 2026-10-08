using System.Globalization;

namespace BladeControl.Core.Services;

/// <summary>How the Display page writes the panel's figures. Pure, so it can be tested without a screen.</summary>
internal static class DisplayPanelText
{
    // The makers of laptop panels, by the three letters their EDID gives.
    private static readonly Dictionary<string, string> Makers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AUO"] = "AU Optronics",
        ["BOE"] = "BOE",
        ["CMN"] = "Innolux",
        ["CSO"] = "CSOT",
        ["CSW"] = "CSOT",
        ["IVO"] = "InfoVision",
        ["LGD"] = "LG Display",
        ["SDC"] = "Samsung Display",
        ["SHP"] = "Sharp",
        ["TMA"] = "Tianma",
        ["APP"] = "Apple",
        ["SAM"] = "Samsung",
        ["LEN"] = "Lenovo",
        ["CHI"] = "Chi Mei",
        ["HSD"] = "HannStar"
    };

    /// <summary>The maker's name for its EDID code, or the code itself when it is not one of the known panel makers.</summary>
    public static string MakerName(string code) => Makers.TryGetValue(code, out var name) ? name : code;

    // The sizes laptops are sold as. A panel's own measure of its picture is a
    // little under the size on the box (15.53″ for a 15.6″), so a size this
    // close to one of them is shown as it.
    private static readonly double[] SoldSizes = [13.3, 13.4, 14.0, 14.5, 15.6, 16.0, 17.3, 18.0];

    /// <summary>The diagonal as written on the box, to a tenth of an inch: 15.6″.</summary>
    public static string Diagonal(double inches, CultureInfo culture)
    {
        var sold = SoldSizes.FirstOrDefault(size => Math.Abs(size - inches) <= 0.12, Math.Round(inches, 1));
        return $"{sold.ToString("0.0", culture)}″";
    }

    /// <summary>The visible area in centimeters, to a tenth: "34.4 × 19.3 cm".</summary>
    public static string Area(int widthMillimeters, int heightMillimeters, CultureInfo culture) =>
        $"{(widthMillimeters / 10.0).ToString("0.0", culture)} × {(heightMillimeters / 10.0).ToString("0.0", culture)} cm";

    /// <summary>A resolution, with a thin multiplication sign: "1920 × 1080".</summary>
    public static string Resolution(int width, int height) => $"{width} × {height}";
}

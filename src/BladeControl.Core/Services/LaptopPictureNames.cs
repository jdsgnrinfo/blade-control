using System.Text.RegularExpressions;

namespace BladeControl.Core.Services;

/// <summary>
/// Which picture in Assets\Laptops shows a laptop, from its name as the app
/// or Windows gives it ("Razer Blade 15 Base (2020)", "Blade 15 Base Model
/// (Early 2020) - RZ09-0328"). The pictures are named by line, size, edition
/// and year, "blade-15-base-2020.png", and the closest one there wins: the
/// same laptop that year, then any year of it, then its size, then any Blade.
/// </summary>
internal static partial class LaptopPictureNames
{
    /// <summary>The picture names to try, the closest first, without ".png".</summary>
    public static IReadOnlyList<string> CandidatesFor(string? model)
    {
        var names = new List<string>();

        if (string.IsNullOrWhiteSpace(model))
        {
            names.Add("blade");
            return names;
        }

        var text = model.ToLowerInvariant();
        var line = text.Contains("stealth") ? "blade-stealth" : text.Contains("advanced") ? "blade-advanced" : "blade";
        var size = Size().Match(text) is { Success: true } sizeMatch ? sizeMatch.Groups[1].Value : null;
        var year = Year().Match(text) is { Success: true } yearMatch ? yearMatch.Value : null;
        var edition = text.Contains("base") ? "base" : text.Contains("mercury") ? "mercury" : null;

        var sized = size is null ? line : $"{line}-{size}";

        void Add(params string?[] parts)
        {
            if (parts.Any(part => part is null))
                return;

            var name = string.Join("-", parts);

            if (!names.Contains(name))
                names.Add(name);
        }

        Add(sized, edition, year);
        Add(sized, year);
        Add(sized, edition);
        Add(sized);

        // An Advanced or a Stealth with no picture of its own still looks like its size of Blade.
        if (line != "blade" && size is not null)
        {
            Add($"blade-{size}", year);
            Add($"blade-{size}");
        }

        Add("blade");
        return names;
    }

    // "Blade 15", "Blade Stealth 13", "Blade 15 Advanced": the size in inches after the name.
    [GeneratedRegex(@"blade(?:\s+(?:stealth|pro|advanced))?\s+(1[3-8])\b")]
    private static partial Regex Size();

    [GeneratedRegex(@"\b20[1-3]\d\b")]
    private static partial Regex Year();
}

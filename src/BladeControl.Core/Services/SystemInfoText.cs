using System.Globalization;
using System.Text.RegularExpressions;
using BladeControl.Core.Localization;

namespace BladeControl.Core.Services;

/// <summary>How the System information window writes its figures.</summary>
internal static partial class SystemInfoText
{
    private const long Gigabyte = 1L << 30;
    private const long Megabyte = 1L << 20;

    /// <summary>
    /// The model without the product code the maker adds at the end:
    /// "Blade 15 Base Model (Early 2020) - RZ09-0328" becomes "Blade 15 Base Model (Early 2020)".
    /// </summary>
    public static string ModelName(string model) => ProductCode().Replace(model, string.Empty).Trim();

    /// <summary>A graphics card's line under its name: the maker, then its memory in brackets, "NVIDIA (6.0 GB)".</summary>
    public static string? GpuDetail(string? vendor, long? memoryBytes, CultureInfo culture) =>
        (vendor, memoryBytes is > 0 ? Size(memoryBytes.Value, culture) : null) switch
        {
            ({ } maker, { } memory) => $"{maker} ({memory})",
            ({ } maker, null) => maker,
            (null, { } memory) => memory,
            _ => null
        };

    /// <summary>
    /// The computer's name at the sidebar's foot: the laptop's model without
    /// the year in brackets, "Razer Blade 15 Base (2020)" as "Razer Blade 15 Base";
    /// with no model found, Windows' name for the computer.
    /// </summary>
    public static string ComputerName(string? model, string machineName) =>
        string.IsNullOrWhiteSpace(model) ? machineName : TrailingBrackets().Replace(model, string.Empty).Trim();

    [GeneratedRegex(@"\s*\([^()]*\)\s*$")]
    private static partial Regex TrailingBrackets();

    // " - RZ09-0328" and the like: a dash, then letters and digits in dash-joined groups, at the end.
    [GeneratedRegex(@"\s+-\s+[A-Z0-9]+(?:-[A-Z0-9]+)+$")]
    private static partial Regex ProductCode();

    /// <summary>
    /// The processor without its trademark marks and base clock:
    /// "Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz" becomes "Intel Core i7-10750H".
    /// </summary>
    public static string CpuName(string name)
    {
        var cleaned = TrademarkMarks().Replace(name, string.Empty);
        cleaned = BaseClock().Replace(cleaned, string.Empty);
        cleaned = CpuWords().Replace(cleaned, string.Empty);
        return Spaces().Replace(cleaned, " ").Trim();
    }

    /// <summary>"NVIDIA GeForce GTX 1660 Ti" as "GeForce GTX 1660 Ti": the maker is given beside it.</summary>
    public static string GpuName(string name)
    {
        var cleaned = Spaces().Replace(TrademarkMarks().Replace(name, string.Empty), " ").Trim();

        foreach (var maker in (string[])["NVIDIA ", "Intel ", "AMD "])
        {
            if (cleaned.StartsWith(maker, StringComparison.OrdinalIgnoreCase) && cleaned.Length > maker.Length)
                return cleaned[maker.Length..];
        }

        return cleaned;
    }

    /// <summary>The maker for a PCI vendor ID.</summary>
    public static string? Vendor(string? vendorId) => vendorId switch
    {
        "10DE" => "NVIDIA",
        "8086" => "Intel",
        "1002" => "AMD",
        _ => null
    };

    /// <summary>
    /// Windows 11 still calls itself "Windows 10" in the registry; its build
    /// number (22000 and up) is what tells them apart.
    /// </summary>
    public static string OsName(string name, int? build) =>
        build >= 22000 && name.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)
            ? "Windows 11" + name["Windows 10".Length..]
            : name;

    /// <summary>"25H2 (build 26200.9457)", with what is known.</summary>
    public static string? OsDetail(string? version, int? build, int? revision)
    {
        var buildText = build is { } number
            ? L.F("build {0}", revision is { } patch ? $"{number}.{patch}" : number.ToString(CultureInfo.InvariantCulture))
            : null;

        return (version, buildText) switch
        {
            (not null, not null) => $"{version} ({buildText})",
            (not null, null) => version,
            (null, not null) => buildText,
            _ => null
        };
    }

    /// <summary>"6 cores · 12 threads", or just the threads when the cores are unknown.</summary>
    public static string CpuDetail(int? cores, int threads) =>
        cores is { } count
            ? L.F("{0} cores · {1} threads", count, threads)
            : L.F("{0} threads", threads);

    /// <summary>
    /// A size as Windows writes it (powers of 1024, called GB): one decimal
    /// under 10 GB, whole above, and in TB from 1000 GB.
    /// </summary>
    public static string Size(long bytes, CultureInfo culture)
    {
        var gigabytes = bytes / (double)Gigabyte;

        // Below a gigabyte, such as built-in graphics memory: "128 MB".
        if (gigabytes < 1)
            return (bytes / (double)Megabyte).ToString("0", culture) + " MB";

        if (gigabytes >= 1000)
            return (gigabytes / 1024).ToString("0.0", culture) + " TB";

        return gigabytes.ToString(gigabytes < 10 ? "0.0" : "0", culture) + " GB";
    }

    /// <summary>The BIOS date as the registry keeps it (MM/DD/YYYY), in the reader's format.</summary>
    public static string? BiosDate(string? date, CultureInfo culture) =>
        DateTime.TryParseExact(date, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("d", culture)
            : date;

    [GeneratedRegex(@"\((R|TM|C)\)", RegexOptions.IgnoreCase)]
    private static partial Regex TrademarkMarks();

    [GeneratedRegex(@"\s*@\s*[\d.]+\s*GHz", RegexOptions.IgnoreCase)]
    private static partial Regex BaseClock();

    [GeneratedRegex(@"\b(CPU|Processor)\b|\bwith Radeon.*$", RegexOptions.IgnoreCase)]
    private static partial Regex CpuWords();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BladeControl.UI;

/// <summary>A program's own icon, as Explorer shows it, for the Apps page and its notices. Each is read once.</summary>
internal static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The icon of <paramref name="path"/> (an .exe), or null when it has none or is gone.</summary>
    public static ImageSource? For(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (Cache.TryGetValue(path, out var known))
            return known;

        ImageSource? icon = null;

        try
        {
            if (File.Exists(path))
            {
                using var extracted = System.Drawing.Icon.ExtractAssociatedIcon(path);

                if (extracted is not null)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(extracted.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    icon = source;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            icon = null;
        }

        Cache[path] = icon;
        return icon;
    }
}

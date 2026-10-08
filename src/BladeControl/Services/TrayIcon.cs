using System.Runtime.InteropServices;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace BladeControl.Services;

/// <summary>
/// Blade Control in the notification area, as Razer Helper is: the logo in
/// the color of the performance mode, its name and the mode on hover, a
/// double click to open the window and a right click for the menu (Open,
/// Exit). The app keeps running here after its window is closed.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly TrayIconArt _art;
    private PerformanceMode? _shownMode;

    public TrayIcon()
    {
        _art = new TrayIconArt(LoadIcon());
        _icon = new Forms.NotifyIcon
        {
            Icon = _art.For(null),
            Text = HoverText(null),
            Visible = true
        };

        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                OpenRequested?.Invoke(this, EventArgs.Empty);
        };

        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
                MenuRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? OpenRequested;

    /// <summary>A right click: the menu opens where the pointer is.</summary>
    public event EventHandler? MenuRequested;

    /// <summary>What the icon says on hover: "Blade Control (Balanced)", or just the name while the mode is unknown.</summary>
    internal static string HoverText(PerformanceMode? mode) =>
        mode is PerformanceMode known ? $"Blade Control ({L.T(known.ToString())})" : "Blade Control";

    /// <summary>The icon takes the mode's color and names it on hover; nothing is redrawn for the mode already shown.</summary>
    public void ShowMode(PerformanceMode? mode)
    {
        if (mode == _shownMode)
            return;

        _shownMode = mode;
        _icon.Icon = _art.For(mode);
        _icon.Text = HoverText(mode);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _art.Dispose();
    }

    // The icon file holds several sizes; the one the tray uses at this display scale.
    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/BladeControl.ico"));

            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or System.IO.IOException)
        {
            AppLog.Error("Could not load the tray icon.", exception);
        }

        return Drawing.SystemIcons.Application;
    }
}

/// <summary>
/// The tray icon in the color of the current performance mode: the logo with
/// its green disc repainted (Balanced keeps the original green) and the black
/// mark left as it is. Each color is made once and kept until the app exits.
/// </summary>
internal sealed class TrayIconArt(Drawing.Icon original) : IDisposable
{
    /// <summary>The disc's color for each mode. Modes left out keep the logo's own green.</summary>
    public static readonly IReadOnlyDictionary<PerformanceMode, Drawing.Color> ModeColors = new Dictionary<PerformanceMode, Drawing.Color>
    {
        [PerformanceMode.Silent] = Drawing.Color.FromArgb(0x00, 0x6A, 0xFF),
        [PerformanceMode.Custom] = Drawing.Color.FromArgb(0xA2, 0x59, 0xFF)
    };

    private readonly Dictionary<PerformanceMode, Drawing.Icon> _tinted = [];

    /// <summary>The icon for <paramref name="mode"/>; the original for Balanced or an unknown mode.</summary>
    public Drawing.Icon For(PerformanceMode? mode)
    {
        if (mode is not PerformanceMode known || !ModeColors.TryGetValue(known, out var color))
            return original;

        if (!_tinted.TryGetValue(known, out var icon))
            _tinted[known] = icon = Tint(original, color);

        return icon;
    }

    public void Dispose()
    {
        foreach (var icon in _tinted.Values)
            icon.Dispose();

        _tinted.Clear();
    }

    /// <summary>
    /// Repaints the logo's green in <paramref name="color"/>. A pixel's share of
    /// green sets how much of the new color it gets, so the soft edges between
    /// the disc and the black mark stay smooth; transparency is kept.
    /// </summary>
    internal static Drawing.Bitmap TintBitmap(Drawing.Bitmap source, Drawing.Color color)
    {
        var bitmap = new Drawing.Bitmap(source.Width, source.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var graphics = Drawing.Graphics.FromImage(bitmap))
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);

        var area = new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(area, Drawing.Imaging.ImageLockMode.ReadWrite, Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            var pixels = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            for (var at = 0; at < pixels.Length; at += 4)
            {
                // Byte order in memory: blue, green, red, alpha.
                var (blue, green, red) = (pixels[at], pixels[at + 1], pixels[at + 2]);

                // Only the green parts: greener than they are red or blue.
                if (green <= red || green <= blue)
                    continue;

                var share = (green - Math.Max(red, blue)) / 255.0;
                var rest = Math.Max(red, blue);

                pixels[at] = (byte)Math.Min(255, rest + color.B * share);
                pixels[at + 1] = (byte)Math.Min(255, rest + color.G * share);
                pixels[at + 2] = (byte)Math.Min(255, rest + color.R * share);
            }

            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static Drawing.Icon Tint(Drawing.Icon icon, Drawing.Color color)
    {
        using var source = icon.ToBitmap();
        using var tinted = TintBitmap(source, color);

        // FromHandle does not own the handle, so keep a copy and free it.
        var handle = tinted.GetHicon();

        try
        {
            using var borrowed = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}

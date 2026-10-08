using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace BladeControl.Services;

/// <summary>
/// Blade Control in the notification area: its logo, always the same, its
/// name and the performance mode on hover, a click to open the window and a right click for the menu (Open,
/// Exit). The app keeps running here after its window is closed.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _logo;
    private PerformanceMode? _shownMode;

    public TrayIcon()
    {
        _logo = LoadIcon();
        _icon = new Forms.NotifyIcon
        {
            Icon = _logo,
            Text = HoverText(null),
            Visible = true
        };

        // A click (or a double click) opens the window.
        _icon.MouseClick += (_, e) =>
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

    /// <summary>The hover text names the mode; the logo stays as it is.</summary>
    public void ShowMode(PerformanceMode? mode)
    {
        if (mode == _shownMode)
            return;

        _shownMode = mode;
        _icon.Text = HoverText(mode);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _logo.Dispose();
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

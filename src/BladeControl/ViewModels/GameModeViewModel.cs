using BladeControl.Core.Localization;
using BladeControl.Core.Services;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BladeControl.ViewModels;

/// <summary>
/// Game mode, on the Lighting page's Keyboard card: the Windows key kept from
/// Windows so it cannot throw you out of a game, and Alt+Tab and Alt+F4 too
/// when chosen. An app's profile can turn it on or off while the app runs,
/// without saving that; flipping the switch by hand takes over from it.
/// </summary>
internal sealed partial class GameModeViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly GameModeHook _hook = new();

    // What the app running now asks for, and its name; null when no app profile holds game mode.
    private (bool On, string App)? _appOverride;

    public GameModeViewModel(AppServices services)
    {
        _services = services;
        Apply();
    }

    /// <summary>Whether game mode is on now: the app's choice while one holds it, otherwise the switch's.</summary>
    public bool IsOn
    {
        get => _appOverride?.On ?? _services.Settings.GameMode;
        set
        {
            if (value == IsOn && _appOverride is null)
                return;

            _appOverride = null;
            _services.UpdateSettings(settings => settings with { GameMode = value });
            Apply();
        }
    }

    public bool BlockAltTab
    {
        get => _services.Settings.GameModeBlocksAltTab;
        set
        {
            _services.UpdateSettings(settings => settings with { GameModeBlocksAltTab = value });
            Apply();
        }
    }

    public bool BlockAltF4
    {
        get => _services.Settings.GameModeBlocksAltF4;
        set
        {
            _services.UpdateSettings(settings => settings with { GameModeBlocksAltF4 = value });
            Apply();
        }
    }

    /// <summary>Under the switches: which app holds game mode, if one does.</summary>
    public string Status => _appOverride is { } held
        ? L.F(held.On ? "On while {0} runs." : "Off while {0} runs.", held.App)
        : string.Empty;

    public bool HasStatus => _appOverride is not null;

    /// <summary>An app's profile turns game mode on or off while it runs (never saved); null gives it back to the switch.</summary>
    public void SetAppOverride(string? app, bool? on)
    {
        _appOverride = app is not null && on is bool wanted ? (wanted, app) : null;
        Apply();
    }

    public void Dispose() => _hook.Dispose();

    private void Apply()
    {
        _hook.Options = new GameModeOptions(BlockAltTab, BlockAltF4);
        _hook.Set(IsOn);
        OnPropertyChanged(string.Empty);
    }
}

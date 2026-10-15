using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using BladeControl.Services;
using BladeControl.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BladeControl.ViewModels;

/// <summary>
/// Settings' updates: a look at GitHub for a newer version ten seconds after
/// starting and then once a day, unless turned off, and a button to look now.
/// Updating downloads the portable zip, checks it against its published
/// checksum, puts it in the app's folder and restarts. When that cannot be done
/// (a build from source, a folder the app may not write to, a release without
/// the zip, or any failure) the release's page opens instead, to download it
/// by hand.
/// </summary>
internal sealed partial class UpdatesViewModel : ObservableObject
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Daily = TimeSpan.FromDays(1);

    private readonly AppServices _services;
    private readonly Version _current;
    private readonly AppUpdateClient _client;
    private readonly DispatcherTimer _timer = new() { Interval = FirstCheck };
    private AppRelease? _release;
    private string? _notified;

    // A look the user asked for shows a skipped version again.
    private bool _showSkipped;

    public UpdatesViewModel(AppServices services)
    {
        _services = services;
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        _current = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        _client = new AppUpdateClient(_current);
        _statusText = IsEnabled ? L.T("Not checked yet.") : L.T("Automatic checking is off.");

        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = Daily;

            if (IsEnabled)
                await CheckAsync(byUser: false);
        };
    }

    /// <summary>A newer version was found by a look of its own (not one the user asked for), once per version.</summary>
    public event EventHandler<string>? Found;

    /// <summary>Looks at GitHub ten seconds from now, then once a day while the app runs.</summary>
    public void Start() => _timer.Start();

    public bool IsEnabled
    {
        get => _services.Settings.CheckForUpdates;
        set
        {
            if (value == IsEnabled)
                return;

            _services.UpdateSettings(settings => settings with { CheckForUpdates = value });
            AppLog.Info($"Checking for updates turned {(value ? "on" : "off")}.");
            OnPropertyChanged();

            if (!value && !HasUpdate && !IsBusy)
                StatusText = L.T("Automatic checking is off.");
        }
    }

    /// <summary>Under the row's title: the version in use and how the last look went.</summary>
    [ObservableProperty]
    private string _statusText;

    /// <summary>A newer version to offer: the Update button, and the dot on Settings.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    private bool _hasUpdate;

    /// <summary>The row's title while a version is offered: "Blade Control 0.1.5 is available".</summary>
    [ObservableProperty]
    private string _title = L.T("Updates");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand), nameof(UpdateCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private LoaderStatus _status;

    private bool CanCheck => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckNowAsync() => CheckAsync(byUser: true);

    private async Task CheckAsync(bool byUser)
    {
        if (IsBusy)
            return;

        IsBusy = byUser;
        Status = LoaderStatus.Loading;

        if (byUser)
            StatusText = L.T("Checking...");

        try
        {
            _release = await _client.LatestAsync(CancellationToken.None);
            _showSkipped |= byUser;
            var newer = _release is { } release && ReleaseParser.IsNewer(release.Version, _current);
            var skipped = newer && !_showSkipped && _services.Settings.SkippedUpdate == _release!.Version.ToString(3);
            AppLog.Info($"Update check: latest {_release?.Version.ToString(3) ?? "none"}, this is {_current.ToString(3)}{(skipped ? ", skipped by the user" : string.Empty)}.");

            Offer(newer && !skipped);

            if (newer && skipped)
                StatusText = L.F("Version {0} was skipped. Check now to see it again.", _release!.Version.ToString(3));

            if (HasUpdate && !byUser && _notified != _release!.Version.ToString(3))
            {
                _notified = _release.Version.ToString(3);
                Found?.Invoke(this, _notified);
            }

            if (byUser)
                await ShowEnd(LoaderStatus.Success);
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or TaskCanceledException or IOException)
        {
            AppLog.Error("Could not check for updates.", exception);

            if (!HasUpdate)
                StatusText = L.T("Could not reach GitHub to check. Is the PC online?");

            if (byUser)
                await ShowEnd(LoaderStatus.Error);
        }
    }

    // The row as the last look left it: the version offered, or the one in use.
    private void Offer(bool newer)
    {
        HasUpdate = newer;

        if (newer)
        {
            Title = L.F("Blade Control {0} is available", _release!.Version.ToString(3));
            StatusText = L.F("You have {0}. Update downloads it, checks it and restarts Blade Control; your settings stay.", _current.ToString(3));
        }
        else
        {
            Title = L.T("Updates");
            StatusText = L.F("Blade Control {0} is the latest version. Checked at {1}.", _current.ToString(3), DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private bool CanUpdate => HasUpdate && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        if (_release is not { } release)
            return;

        var version = release.Version.ToString(3);
        var appFolder = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        if (!release.CanInstall || !UpdateInstaller.CanInstallInto(appFolder))
        {
            AppLog.Info($"Update {version} cannot be put in {appFolder} by the app; opening its page.");
            Dialogs.Tell(
                L.F("Blade Control cannot update this copy by itself. Its page on GitHub opens now: download the zip of version {0} and replace this folder with it. Your settings stay.", version),
                L.T("Update Blade Control"));
            OpenPage();
            return;
        }

        var size = release.ZipBytes > 0 ? SystemInfoText.Size(release.ZipBytes, System.Globalization.CultureInfo.CurrentCulture) : null;
        var answer = Dialogs.Ask(
            (size is null ? L.F("Update to Blade Control {0}?", version) : L.F("Update to Blade Control {0}? It downloads {1}.", version, size)) +
            "\r\n\r\n" + L.T("Blade Control restarts when it is done. Your settings and profiles stay."),
            L.T("Update Blade Control"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        Status = LoaderStatus.Loading;
        StatusText = L.F("Downloading... {0}%", 0);
        var progress = new Progress<double>(done => StatusText = L.F("Downloading... {0}%", (int)(done * 100)));
        var staging = Path.Combine(Path.GetTempPath(), "BladeControl-update");

        try
        {
            var files = await _client.DownloadAsync(release, staging, progress, CancellationToken.None);
            StatusText = L.T("Installing...");
            await Task.Run(() => UpdateInstaller.Install(files, appFolder));
            TryDelete(staging);
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not update to {version}.", exception);
            TryDelete(staging);
            await ShowEnd(LoaderStatus.Error);
            StatusText = L.T("The update could not be installed. Its page on GitHub is open to download it by hand.");
            OpenPage();
            return;
        }

        Status = LoaderStatus.Success;
        StatusText = L.T("Updated. Restarting...");
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        if (AppRestart.Relaunch())
        {
            App.QuitForRestart();
            return;
        }

        IsBusy = false;
        Dialogs.Tell(L.T("Blade Control was updated but could not restart itself. Please close it and open it again."), "Blade Control");
    }

    [RelayCommand]
    private void OpenPage() => ExternalLinks.OpenReleasePage(_release?.PageUrl);

    /// <summary>Not offered again until a later version comes out.</summary>
    [RelayCommand]
    private void Skip()
    {
        if (_release is not { } release)
            return;

        var version = release.Version.ToString(3);
        _services.UpdateSettings(settings => settings with { SkippedUpdate = version });
        _showSkipped = false;
        AppLog.Info($"Update {version} skipped.");
        HasUpdate = false;
        Title = L.T("Updates");
        StatusText = L.F("Version {0} was skipped. Check now to see it again.", version);
    }

    // The loader's check or cross for a moment, then the buttons again.
    private async Task ShowEnd(LoaderStatus end)
    {
        Status = end;
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        IsBusy = false;
        Status = LoaderStatus.Loading;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not delete the update's download.", exception);
        }
    }
}

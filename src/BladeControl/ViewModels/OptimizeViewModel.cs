using System.Globalization;
using System.ServiceProcess;
using System.Windows;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using BladeControl.Services;
using BladeControl.UI;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BladeControl.ViewModels;

/// <summary>
/// Everything that frees up the laptop, as in Razer Helper: memory, old
/// temporary files and the NVIDIA shader cache; the dedicated GPU, now or on
/// unplugging; the hibernation file; and Razer's own software when it is
/// installed. Nothing that is running is closed without asking, and what needs
/// administrator rights asks Windows for them one job at a time.
/// </summary>
internal sealed partial class OptimizeViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly Action<string, bool> _report;
    private readonly MemoryTrimmer _trimmer = new(new WindowsProcessMemory());
    private readonly TempCleaner _temp = TempCleaner.ForCurrentUser();
    private readonly TempCleaner _shaders = TempCleaner.ForNvidiaShaderCache();
    private readonly TempCleaner _windowsUpdate = WindowsCaches.WindowsUpdate();
    private readonly DgpuFreeUpCoordinator _gpu;
    private Dictionary<string, ServiceStartMode> _recordedModes;
    private Dictionary<string, string> _recordedLogins;
    private RazerSoftwareStatus? _razerStatus;
    private bool _settingHibernation;

    public OptimizeViewModel(AppServices services, Action<string, bool> report)
    {
        _services = services;
        _report = report;
        _recordedModes = new(services.Settings.RazerServiceStartModes ?? []);
        _recordedLogins = new(services.Settings.RazerLoginApprovals ?? []);

        Memory = new ActionRow(L.T("Free up memory"), L.T("Free up"), FreeUpMemoryAsync,
            L.T("Frees unused RAM and clears Windows' cache (asks for permission)."));
        TemporaryFiles = new ActionRow(L.T("Temporary files"), L.T("Clean"), CleanTemporaryFilesAsync, L.T("Scanning...")) { CanRun = false };
        ShaderCache = new ActionRow(L.T("NVIDIA shader cache"), L.T("Clear"), ClearShaderCacheAsync, L.T("Scanning...")) { CanRun = false };
        FreeUpGpu = new ActionRow(L.T("Free up GPU"), L.T("Free up"), FreeUpGpuAsync,
            L.T("Closes the apps keeping the dedicated GPU awake, if you confirm."));
        RazerSoftware = new ActionRow(L.T("Razer software"), L.T("Stop"), StopOrStartRazerAsync, L.T("Razer Software Running: --"));

        // Reads the settings at the moment it runs, so a change to the never-close list applies at once.
        var closer = new DgpuAppCloser(new WindowsProcessControl());
        _gpu = new DgpuFreeUpCoordinator(
            services.PowerSource,
            () => DgpuScanner.ScanAsync(_services.Settings.NeverCloseApps),
            ConfirmCloseGpuAppsAsync,
            apps => closer.Close(apps, _services.Settings.NeverCloseApps))
        {
            Enabled = services.Settings.CloseGpuAppsOnUnplug
        };
    }

    public ActionRow Memory { get; }

    public ActionRow TemporaryFiles { get; }

    public ActionRow ShaderCache { get; }

    public ActionRow FreeUpGpu { get; }

    public ActionRow RazerSoftware { get; }

    /// <summary>Razer's software row shows only once it is known to be installed.</summary>
    [ObservableProperty]
    private bool _hasRazerSoftware;

    public bool CloseGpuAppsOnUnplug
    {
        get => _services.Settings.CloseGpuAppsOnUnplug;
        set
        {
            if (value == CloseGpuAppsOnUnplug)
                return;

            _services.UpdateSettings(settings => settings with { CloseGpuAppsOnUnplug = value });
            _gpu.Enabled = value;
            OnPropertyChanged();
        }
    }

    // ===== Page =====

    /// <summary>The folders fill up between visits, and hibernation may change outside the app: all is read again.</summary>
    public async Task RefreshAsync()
    {
        ShowHibernation();
        await Task.WhenAll(ScanTemporaryFilesAsync(), ScanShaderCacheAsync(), ReadRazerSoftwareAsync());
    }

    public void Dispose() => _gpu.Dispose();

    // ===== Memory =====

    private async Task FreeUpMemoryAsync()
    {
        Memory.Hint = L.T("Freeing up memory...");

        try
        {
            var result = await Task.Run(_trimmer.Trim);
            AppLog.Info($"Free up memory: {result.Programs} programs trimmed, {result.FreedBytes} bytes freed.");
            var trimmed = L.F("{0} freed from {1} programs in the background.", Size(result.FreedBytes), result.Programs);
            Memory.Hint = trimmed;
            Memory.Hint = trimmed + " " + await ClearMemoryCacheAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not free up memory.", exception);
            Memory.Hint = L.T("Could not free up memory.");
        }
    }

    // Windows asks for administrator permission; declining only leaves the cache as it was.
    private static async Task<string> ClearMemoryCacheAsync()
    {
        var before = await Task.Run(WindowsMemoryCache.CachedBytes);
        var result = await ElevatedRunner.RunAsync(MemoryCacheCommand.Arguments());

        if (result.UserDeclined)
            return L.T("The memory cache was kept: administrator permission was not given.");

        if (result.ExitCode != MemoryCacheCommand.Success)
            return L.T("The memory cache could not be cleared.");

        var after = await Task.Run(WindowsMemoryCache.CachedBytes);
        var cleared = before is { } b && after is { } a ? Math.Max(0, b - a) : 0;
        AppLog.Info($"Free up memory: {cleared} bytes of memory cache cleared.");
        return L.F("{0} of memory cache cleared.", Size(cleared));
    }

    // ===== Temporary files: the temporary folder, the Recycle Bin and Windows Update =====

    private async Task ScanTemporaryFilesAsync()
    {
        if (TemporaryFiles.IsBusy)
            return;

        try
        {
            var (found, bin, update) = await Task.Run(() => (_temp.Scan(), WindowsCaches.RecycleBin(), _windowsUpdate.Scan()));
            var total = found.Bytes + bin.Bytes + update.Bytes;
            TemporaryFiles.Hint = total == 0
                ? L.T("Nothing to clean right now.")
                : L.F("{0} to clean: temporary files, Recycle Bin and Windows Update.", Size(total));
            TemporaryFiles.CanRun = total > 0;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not scan the temporary files.", exception);
            TemporaryFiles.Hint = L.T("Could not read the temporary folder.");
        }
    }

    private async Task CleanTemporaryFilesAsync()
    {
        // What is in the Recycle Bin cannot be recovered once emptied, so it is asked first.
        var bin = await Task.Run(WindowsCaches.RecycleBin);
        var emptyBin = false;

        if (bin.Items > 0)
        {
            var answer = Dialogs.Ask(
                L.F("Empty the Recycle Bin too? It holds {0} in {1} items, which cannot be recovered afterwards.", Size(bin.Bytes), bin.Items.ToString("N0", CultureInfo.CurrentCulture)),
                L.T("Temporary files"),
                MessageBoxButton.YesNoCancel);

            if (answer == MessageBoxResult.Cancel)
                return;

            emptyBin = answer == MessageBoxResult.Yes;
        }

        TemporaryFiles.Hint = L.T("Cleaning...");

        try
        {
            var result = await Task.Run(_temp.Clean);
            AppLog.Info($"Temporary files: {result.Files} removed ({result.Bytes} bytes), {result.Skipped} in use left.");
            var freed = result.Bytes;

            if (emptyBin && await Task.Run(WindowsCaches.EmptyRecycleBin))
            {
                freed += bin.Bytes;
                AppLog.Info($"Recycle Bin emptied: {bin.Items} items, {bin.Bytes} bytes.");
            }

            // Windows' caches need administrator permission; declining leaves them as they were.
            var before = await Task.Run(() => _windowsUpdate.Scan().Bytes);
            var elevated = await ElevatedRunner.RunAsync(WindowsCachesCommand.Arguments());
            var after = await Task.Run(() => _windowsUpdate.Scan().Bytes);
            freed += Math.Max(0, before - after);

            var line = L.F("{0} freed.", Size(freed));
            TemporaryFiles.Hint = elevated.UserDeclined
                ? $"{line} {L.T("Windows' caches were kept: administrator permission was not given.")}"
                : elevated.ExitCode != WindowsCachesCommand.Success
                    ? $"{line} {L.T("Windows' caches could not all be cleared.")}"
                    : line;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not clean the temporary files.", exception);
            TemporaryFiles.Hint = L.T("Could not clean the temporary files.");
        }

        // Nothing is left to clean until the next scan.
        TemporaryFiles.CanRun = false;
    }

    // ===== NVIDIA shader cache =====

    private async Task ScanShaderCacheAsync()
    {
        if (ShaderCache.IsBusy)
            return;

        try
        {
            var (exists, found) = await Task.Run(() => (_shaders.AnyFolderExists, _shaders.Scan()));
            ShaderCache.Hint = !exists ? L.T("No NVIDIA shader cache on this PC.")
                : found.Files == 0 ? L.T("Empty. Games rebuild it as they load.")
                : L.F("{0} in {1} files. Clearing it can fix stutter after a driver update.", Size(found.Bytes), found.Files.ToString("N0", CultureInfo.CurrentCulture));
            ShaderCache.CanRun = found.Files > 0;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not scan the NVIDIA shader cache.", exception);
            ShaderCache.Hint = L.T("Could not read the shader cache.");
        }
    }

    private async Task ClearShaderCacheAsync()
    {
        ShaderCache.Hint = L.T("Clearing...");

        try
        {
            var result = await Task.Run(_shaders.Clean);
            AppLog.Info($"NVIDIA shader cache: {result.Files} removed ({result.Bytes} bytes), {result.Skipped} in use left.");
            var cleaned = L.F("{0} freed ({1} files).", Size(result.Bytes), result.Files.ToString("N0", CultureInfo.CurrentCulture));
            ShaderCache.Hint = result.Skipped == 0
                ? cleaned
                : $"{cleaned} {L.F("{0} in use were left.", result.Skipped.ToString("N0", CultureInfo.CurrentCulture))}";
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not clear the NVIDIA shader cache.", exception);
            ShaderCache.Hint = L.T("Could not clear the shader cache.");
        }

        ShaderCache.CanRun = false;
    }

    // ===== Dedicated GPU =====

    private async Task FreeUpGpuAsync()
    {
        var outcome = await _gpu.FreeUpAsync();

        if (DgpuText.DescribeOutcome(outcome) is { } message)
            Dialogs.Tell(message, L.T("Free up GPU"));
    }

    /// <summary>
    /// For Display's integrated-graphics-only switch: the same list, question
    /// and closing as Free up GPU, with the outcome left to the caller to tell.
    /// </summary>
    public Task<DgpuFreeUpOutcome> CloseGpuAppsAsync() => _gpu.FreeUpAsync();

    // The coordinator works off the interface thread; the question is asked on it.
    private Task<bool> ConfirmCloseGpuAppsAsync(IReadOnlyList<DgpuApp> apps, bool dismissWhenPluggedIn) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
            Dialogs.Ask(DgpuText.BuildConfirmation(apps), L.T("Free up GPU"), MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes).Task;

    // ===== Hibernation =====

    [ObservableProperty]
    private string _hibernationHint = string.Empty;

    [ObservableProperty]
    private bool _hibernationChanging;

    private bool _hibernation;

    /// <summary>On while hibernation is. Changing it asks Windows for administrator permission.</summary>
    public bool Hibernation
    {
        get => _hibernation;
        set
        {
            if (_settingHibernation || value == _hibernation)
                return;

            _hibernation = value;
            OnPropertyChanged();
            _ = SetHibernationAsync(value);
        }
    }

    private void ShowHibernation()
    {
        var enabled = WindowsHibernation.IsEnabled() ?? true;

        _settingHibernation = true;
        _hibernation = enabled;
        OnPropertyChanged(nameof(Hibernation));
        _settingHibernation = false;

        HibernationHint = !enabled
            ? L.T("Off, with no file on disk. Hibernate and fast startup are off too.")
            : WindowsHibernation.FileBytes() is { } bytes
                ? L.F("Its file takes {0}. Off frees it, with no hibernate or fast startup.", Size(bytes))
                : L.T("Off frees its file's disk space, but turns off hibernate and fast startup.");
    }

    // Declining, or a refusal, leaves hibernation as it was and puts the switch back.
    private async Task SetHibernationAsync(bool enabled)
    {
        HibernationChanging = true;
        HibernationHint = L.T("Asking Windows (asks for permission)...");
        string? problem = null;

        try
        {
            var result = await ElevatedRunner.RunAsync(HibernationCommand.Arguments(enabled));

            if (result.UserDeclined)
                problem = L.T("Kept as it was: administrator permission was not given.");
            else if (result.ExitCode != HibernationCommand.Success)
                problem = L.T("Windows did not make the change.");
            else
                AppLog.Info($"Hibernation turned {(enabled ? "on" : "off")}.");
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not change hibernation.", exception);
            problem = L.T("Windows did not make the change.");
        }

        HibernationChanging = false;
        ShowHibernation();

        if (problem is not null)
            HibernationHint = problem;
    }

    // ===== Razer's software =====

    private async Task ReadRazerSoftwareAsync()
    {
        if (RazerSoftware.IsBusy)
            return;

        try
        {
            var status = await Task.Run(_services.RazerSoftware.GetStatus);
            _razerStatus = status;
            HasRazerSoftware = status.IsInstalled;
            RazerSoftware.Hint = status.Running > 0 ? L.F("Razer Software Running: {0}", status.Running)
                : status.LoginEnabled ? L.T("Razer Software Running: 0 (starts at login)")
                : L.T("Razer Software Running: 0");
            RazerSoftware.ButtonText = L.T(status.NeedsStop ? "Stop" : "Start");
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the Razer software.", exception);
        }
    }

    private async Task StopOrStartRazerAsync()
    {
        if (_razerStatus is not { } status)
            return;

        try
        {
            var outcome = status.NeedsStop ? await StopRazerAsync(status) : await StartRazerAsync(status);

            if (outcome is { } message)
                _report(message.Text, message.IsError);
        }
        catch (Exception exception)
        {
            AppLog.Error("Changing the Razer software failed.", exception);
            _report(L.T("Could not change the Razer software."), true);
        }

        await ReadRazerSoftwareAsync();
    }

    private async Task<(string Text, bool IsError)?> StopRazerAsync(RazerSoftwareStatus status)
    {
        var peripherals = await Task.Run(RazerPeripheralScanner.FindConnectedNames);

        if (Dialogs.Ask(BuildStopWarning(status, peripherals), L.T("Stop Razer software"), MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
        {
            return null;
        }

        var problems = new List<string>();

        if (status.ServicesToStop.Count > 0)
        {
            _recordedModes = new(RazerServiceManager.RecordStartModes(status.Services, _recordedModes));
            _services.UpdateSettings(settings => settings with { RazerServiceStartModes = _recordedModes });

            RazerSoftware.Hint = L.T("Stopping Razer software...");
            var result = await ElevatedRunner.RunAsync(RazerServiceCommand.StopArguments());

            if (result.UserDeclined)
                return (L.T("Administrator approval was declined. Nothing was changed."), true);

            if (result.ExitCode != RazerServiceCommand.Success)
                problems.Add(L.T("Some Razer services could not be stopped."));
        }

        var change = _services.RazerSoftware.DisableLoginEntries(_recordedLogins, record =>
        {
            _recordedLogins = new(record);
            _services.UpdateSettings(settings => settings with { RazerLoginApprovals = _recordedLogins });
        });

        if (!change.IsSuccess)
            problems.Add(L.T("Razer's startup at login could not be turned off."));

        await Task.Run(_services.RazerSoftware.AskAppsToClose);

        return problems.Count > 0
            ? (L.F("{0} See the log for details.", string.Join(" ", problems)), true)
            : (L.T("Razer software stopped and kept off."), false);
    }

    private async Task<(string Text, bool IsError)?> StartRazerAsync(RazerSoftwareStatus status)
    {
        RazerSoftware.Hint = L.T("Starting Razer software...");
        var problems = new List<string>();

        if (status.Services.Total > 0)
        {
            var result = await ElevatedRunner.RunAsync(RazerServiceCommand.RestoreArguments(_recordedModes));

            if (result.UserDeclined)
                return (L.T("Administrator approval was declined. Nothing was changed."), true);

            if (result.ExitCode != RazerServiceCommand.Success)
                problems.Add(L.T("Some Razer services could not be restored."));
        }

        if (_services.RazerSoftware.RestoreLoginEntries(_recordedLogins).Count == 0)
        {
            _recordedLogins = [];
            _services.UpdateSettings(settings => settings with { RazerLoginApprovals = _recordedLogins });
        }
        else
        {
            problems.Add(L.T("Razer's startup at login could not be restored."));
        }

        return problems.Count > 0
            ? (L.F("{0} See the log for details.", string.Join(" ", problems)), true)
            : (L.T("Razer software restored."), false);
    }

    private static string BuildStopWarning(RazerSoftwareStatus status, IReadOnlyList<string> peripherals)
    {
        var lines = new List<string> { L.T("Stop Razer's background software?"), string.Empty };
        var services = status.ServicesToStop;

        if (services.Count > 0)
        {
            lines.Add(L.F("These {0} services will be stopped and kept off, including after a restart:", services.Count));
            lines.AddRange(services.Select(service => $"  • {service.DisplayName}"));
            lines.Add(string.Empty);
        }

        if (status.RunningApps.Count > 0)
        {
            lines.Add(L.T("These Razer programs are running and will be asked to close (nothing is force-closed):"));
            lines.AddRange(status.RunningApps.Select(name => $"  • {name}"));
            lines.Add(string.Empty);
        }

        if (status.LoginEnabled)
        {
            lines.Add(L.T("Razer will be stopped from starting when you sign in, the same as switching it off in Task Manager's Startup tab:"));
            lines.AddRange(status.LoginEntries.Where(entry => entry.IsEnabled).Select(entry => $"  • {entry.Name}"));
            lines.Add(string.Empty);
        }

        lines.Add(peripherals.Count > 0
            ? L.F("Razer devices connected now: {0}.", string.Join(", ", peripherals))
            : L.T("No other Razer devices are connected right now."));
        lines.Add(L.T("While the services are off, Razer-only features can't be configured on those devices (button remapping, macros, lighting effects, DPI stages). The devices still work as normal."));
        lines.Add(string.Empty);
        lines.Add(L.T("Press Start to bring everything back exactly as it was."));

        if (services.Count > 0)
            lines.Add(L.T("Windows will ask for administrator approval."));

        return string.Join(Environment.NewLine, lines);
    }

    private static string Size(long bytes) => SystemInfoText.Size(bytes, CultureInfo.CurrentCulture);
}

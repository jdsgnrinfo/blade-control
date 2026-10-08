using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Services;

namespace BladeControl.Core.Platform;

/// <summary>An output device Windows can play to: its endpoint id and the name Windows shows for it.</summary>
internal sealed record AudioDevice(string Id, string Name);

/// <summary>
/// Windows' output devices: the ones plugged in and on, which one plays by
/// default, and switching that. The list and the default come from Windows'
/// audio API; the names from the registry beside them, where Windows keeps
/// the same "Speakers (Realtek(R) Audio)" it shows in its own sound settings.
/// </summary>
internal static class WindowsAudio
{
    private const string RenderKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";

    // The endpoint's name ("Speakers") and its device's ("Realtek(R) Audio").
    private const string EndpointNameValue = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    private const string DeviceNameValue = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";

    public static IReadOnlyList<AudioDevice> OutputDevices()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceStateActive, out var collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));

            var devices = new List<AudioDevice>();

            for (uint index = 0; index < count; index++)
            {
                if (collection.Item(index, out var device) != 0 || device.GetId(out var id) != 0)
                    continue;

                devices.Add(new AudioDevice(id, NameOf(id)));
            }

            return devices;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            AppLog.Error("Could not list the audio output devices.", exception);
            return [];
        }
    }

    /// <summary>The device Windows plays to by default, or null when there is none.</summary>
    public static string? DefaultOutputId()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device) == 0 && device.GetId(out var id) == 0 ? id : null;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            AppLog.Error("Could not read the default audio output.", exception);
            return null;
        }
    }

    /// <summary>
    /// Makes a device the default for everything (games and music, calls,
    /// system sounds), as picking it in Windows' sound settings does. Windows
    /// has no published call for this; every sound switcher uses this one,
    /// which Windows' own settings use too.
    /// </summary>
    public static void SetDefaultOutput(string id)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();

        foreach (var role in new[] { Role.Console, Role.Multimedia, Role.Communications })
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));

        AppLog.Info($"Default audio output set to {NameOf(id)}.");
    }

    private static string NameOf(string id)
    {
        using var properties = Registry.LocalMachine.OpenSubKey($@"{RenderKey}\{EqualizerApoConfig.DeviceGuid(id)}\Properties");
        var endpoint = properties?.GetValue(EndpointNameValue) as string;
        var device = properties?.GetValue(DeviceNameValue) as string;

        return (endpoint, device) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{endpoint} ({device})",
            ({ Length: > 0 }, _) => endpoint,
            (_, { Length: > 0 }) => device,
            _ => id
        };
    }

    /// <summary>The effects Windows runs on a device's sound: the class ids in its FxProperties.</summary>
    internal static IEnumerable<string> EffectClassIds(string id)
    {
        using var effects = Registry.LocalMachine.OpenSubKey($@"{RenderKey}\{EqualizerApoConfig.DeviceGuid(id)}\FxProperties");

        if (effects is null)
            yield break;

        foreach (var name in effects.GetValueNames())
        {
            var value = effects.GetValue(name);
            var texts = value as string[] ?? (value is string text ? [text] : []);

            foreach (var each in texts)
            {
                if (Guid.TryParse(each, out _))
                    yield return each;
            }
        }
    }

    private const int DeviceStateActive = 1;

    private enum DataFlow
    {
        Render = 0
    }

    private enum Role
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(DataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient;

    // Only SetDefaultEndpoint is called; the rest hold their places in the table.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
    }
}

/// <summary>
/// Equalizer APO, which does the equalizing: whether it is installed, where
/// its settings live, and whether it is hooked into a given device (its own
/// Device Selector does that, once per device, as administrator).
/// </summary>
internal static class EqualizerApo
{
    /// <summary>Its official page, where it is downloaded.</summary>
    public const string DownloadUrl = "https://sourceforge.net/projects/equalizerapo/";

    private const string Key = @"SOFTWARE\EqualizerAPO";

    /// <summary>Where it reads config.txt from, or null when it is not installed.</summary>
    public static string? ConfigFolder() => FolderValue("ConfigPath");

    private static string? InstallFolder() => FolderValue("InstallPath");

    public static bool IsInstalled => ConfigFolder() is not null;

    /// <summary>Whether one of the effects Windows runs on the device is Equalizer APO's.</summary>
    public static bool IsHookedOn(string deviceId)
    {
        try
        {
            return WindowsAudio.EffectClassIds(deviceId).Any(IsEqualizerApoClass);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Error("Could not read a device's audio effects.", exception);
            return false;
        }
    }

    // Its class ids are whichever ones load its own DLL.
    private static bool IsEqualizerApoClass(string classId)
    {
        using var server = Registry.ClassesRoot.OpenSubKey($@"CLSID\{classId}\InprocServer32");
        return server?.GetValue(null) is string dll &&
               string.Equals(Path.GetFileName(dll), "EqualizerAPO.dll", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Opens its Device Selector, where a device is ticked to hook it in;
    /// Windows asks for administrator approval. Done when the window closes;
    /// false when it could not open or the approval was refused.
    /// </summary>
    public static async Task<bool> OpenDeviceSelectorAsync()
    {
        if (InstallFolder() is not { } folder)
            return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(folder, "DeviceSelector.exe"))
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = folder
            });

            if (process is not null)
                await process.WaitForExitAsync();

            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            // 1223: the approval was refused, which is no error.
            if (exception.NativeErrorCode != 1223)
                AppLog.Error("Could not open Equalizer APO's Device Selector.", exception);

            return false;
        }
    }

    private static string? FolderValue(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Key);
            return key?.GetValue(name) is string folder && Directory.Exists(folder) ? folder : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}

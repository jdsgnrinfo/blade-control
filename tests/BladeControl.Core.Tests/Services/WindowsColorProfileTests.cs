using System.Buffers.Binary;
using System.Text;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class IccProfileTests
{
    private static byte[] Warm() => IccProfile.Build("Blade Control Warm", ColorProfiles.Ramp(ColorProfile.Warm), new DateTime(2026, 10, 15, 12, 0, 0));

    [Fact]
    public void TheHeaderIsADisplayProfile_OfItsOwnSize()
    {
        var profile = Warm();

        Assert.Equal(profile.Length, (int)BinaryPrimitives.ReadUInt32BigEndian(profile));
        Assert.Equal(0, profile.Length % 4);
        Assert.Equal("mntr", Encoding.ASCII.GetString(profile, 12, 4));
        Assert.Equal("RGB ", Encoding.ASCII.GetString(profile, 16, 4));
        Assert.Equal("acsp", Encoding.ASCII.GetString(profile, 36, 4));
    }

    [Fact]
    public void ItCarriesTheProfilesRamp_AsItsGammaTable() =>
        Assert.Equal(ColorProfiles.Ramp(ColorProfile.Warm), IccProfile.ReadVideoCardGamma(Warm()));

    [Fact]
    public void EveryTagSitsInsideTheProfile_OnAFourByteBoundary()
    {
        var profile = Warm();
        var count = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(128));

        for (var index = 0; index < count; index++)
        {
            var offset = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(132 + 12 * index + 4));
            var size = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(132 + 12 * index + 8));

            Assert.Equal(0u, offset % 4);
            Assert.True(offset + size <= profile.Length);
        }
    }

    [Fact]
    public void ARampOfTheWrongLength_IsRefused() =>
        Assert.Throws<ArgumentException>(() => IccProfile.Build("x", new ushort[10], DateTime.Now));
}

public sealed class WindowsColorProfileServiceTests : IDisposable
{
    private sealed class FakeWindows : IDisplayColorProfiles
    {
        public bool UsesMySettings { get; set; }
        public string? Default { get; set; }
        public HashSet<string> Installed { get; } = [];
        public List<string> Removed { get; } = [];

        public string? GetDefault() => Default;

        public string Install(string path)
        {
            Assert.True(File.Exists(path));
            Installed.Add(Path.GetFileName(path));
            return Path.GetFileName(path);
        }

        public void SetDefault(string name) => Default = name;

        public void Remove(string name)
        {
            Installed.Remove(name);
            Removed.Add(name);

            if (Default == name)
                Default = null;
        }
    }

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bc-icc-test-" + Guid.NewGuid().ToString("N"));
    private readonly FakeWindows _windows = new();
    private WindowsColorProfileState? _saved;

    private WindowsColorProfileService Service() => new(_windows, _folder, _saved, state => _saved = state);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void AProfile_BecomesTheScreensDefault_ForThisUserOnly()
    {
        Assert.True(Service().Apply(ColorProfile.Warm));

        Assert.Equal("Blade Control Warm.icm", _windows.Default);
        Assert.True(_windows.UsesMySettings);
        Assert.Equal(new WindowsColorProfileState("Blade Control Warm.icm", false, null), _saved);
    }

    [Fact]
    public void Standard_PutsTheScreenBackAsItWas()
    {
        var service = Service();
        service.Apply(ColorProfile.Warm);
        service.Apply(ColorProfile.Standard);

        Assert.Null(_windows.Default);
        Assert.False(_windows.UsesMySettings);
        Assert.Empty(_windows.Installed);
        Assert.Null(_saved);
    }

    [Fact]
    public void AProfileTheUserHadBefore_ComesBackWithStandard()
    {
        _windows.UsesMySettings = true;
        _windows.Default = "Calibrated.icc";
        var service = Service();

        service.Apply(ColorProfile.Cool);
        Assert.Equal("Blade Control Cool.icm", _windows.Default);

        service.Apply(ColorProfile.Standard);
        Assert.Equal("Calibrated.icc", _windows.Default);
        Assert.True(_windows.UsesMySettings);
    }

    [Fact]
    public void SwitchingProfiles_RemovesTheOldOne_AndKeepsWhatWasThereFirst()
    {
        _windows.UsesMySettings = true;
        _windows.Default = "Calibrated.icc";
        var service = Service();

        service.Apply(ColorProfile.Warm);
        service.Apply(ColorProfile.Contrast);

        Assert.Equal("Blade Control Contrast.icm", _windows.Default);
        Assert.Contains("Blade Control Warm.icm", _windows.Removed);
        Assert.Equal("Calibrated.icc", _saved?.DefaultBefore);
    }

    [Fact]
    public void AfterARestart_ItRemembersWhatToPutBack()
    {
        Service().Apply(ColorProfile.Warm);
        Service().Apply(ColorProfile.Standard);

        Assert.False(_windows.UsesMySettings);
        Assert.Null(_windows.Default);
    }

    [Fact]
    public void StandardWithNothingInstalled_ChangesNothing()
    {
        _windows.Default = "Other.icm";
        Service().Apply(ColorProfile.Standard);

        Assert.Equal("Other.icm", _windows.Default);
    }
}

public sealed class WindowsColorProfileUpdateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bc-icc-update-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private sealed class CountingWindows : IDisplayColorProfiles
    {
        public bool UsesMySettings { get; set; }
        public string? Default { get; set; }
        public int Installs { get; private set; }
        public int Removes { get; private set; }
        public string? GetDefault() => Default;
        public string Install(string path) { Installs++; return Path.GetFileName(path); }
        public void SetDefault(string name) => Default = name;
        public void Remove(string name) { Removes++; if (Default == name) Default = null; }
    }

    [Fact]
    public void TheSameValues_AreNotInstalledAgain()
    {
        var windows = new CountingWindows();
        WindowsColorProfileState? saved = null;
        new WindowsColorProfileService(windows, _folder, saved, state => saved = state).Apply(ColorProfile.Cool);
        new WindowsColorProfileService(windows, _folder, saved, state => saved = state).Apply(ColorProfile.Cool);

        Assert.Equal(1, windows.Installs);
        Assert.Equal(0, windows.Removes);
    }

    [Fact]
    public void ChangedValues_ReplaceTheCopyInWindows()
    {
        var windows = new CountingWindows();
        WindowsColorProfileState? saved = null;
        new WindowsColorProfileService(windows, _folder, saved, state => saved = state).Apply(ColorProfile.Cool);

        // A copy written by an earlier version, with other values.
        var path = Path.Combine(_folder, WindowsColorProfileService.FileName(ColorProfile.Cool));
        File.WriteAllBytes(path, IccProfile.Build("old", ColorProfiles.Ramp(ColorProfile.Warm), DateTime.Now));

        new WindowsColorProfileService(windows, _folder, saved, state => saved = state).Apply(ColorProfile.Cool);

        Assert.Equal(2, windows.Installs);
        Assert.Equal(1, windows.Removes);
        Assert.Equal(ColorProfiles.Ramp(ColorProfile.Cool), IccProfile.ReadVideoCardGamma(File.ReadAllBytes(path)));
        Assert.Equal(WindowsColorProfileService.FileName(ColorProfile.Cool), windows.Default);
    }
}

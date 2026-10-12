using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Models;
using Microsoft.Win32;

namespace BladeControl.Core.Services;

/// <summary>A game installed through a launcher: its name, the folder it lives in, and its main program when known.</summary>
internal sealed record InstalledGame(string Name, string Folder, string? Program, AppSource Source);

/// <summary>
/// Finds the games installed through Steam, the Epic Games Launcher, the Xbox
/// app and Battle.net, by reading the files and registry entries each launcher
/// keeps for itself; nothing is started or asked over the network. A launcher
/// that is not installed, or reads wrong, just adds nothing.
/// </summary>
internal static partial class GameLibrary
{
    // Never games: Steam's shared runtimes and tools.
    private static readonly HashSet<string> SteamToolIds = ["228980", "1070560", "1391110", "1628350", "1493710", "2180100", "1826330"];

    // Programs in a game's folder that are not the game: installers, crash reporters, anti-cheat and the like.
    [GeneratedRegex(@"unins|setup|install|redist|vc_?redist|dxsetup|directx|crash|report|easyanticheat|eac|battleye|beservice|update|prereq|dotnet|cef|helper|overlay|touchup|cleanup|activation|handler", RegexOptions.IgnoreCase)]
    private static partial Regex NotTheGame();

    [GeneratedRegex(@"""path""\s+""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPath();

    [GeneratedRegex(@"""(appid|name|installdir)""\s+""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase)]
    private static partial Regex AcfField();

    [GeneratedRegex(@"DefaultDisplayName\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex XboxDisplayName();

    [GeneratedRegex(@"<Executable\s[^>]*Name\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex XboxExecutable();

    /// <summary>Every installed game found, each folder once, by name.</summary>
    public static IReadOnlyList<InstalledGame> Scan()
    {
        var found = new List<InstalledGame>();

        Try("Steam", () => found.AddRange(ScanSteam()));
        Try("Epic Games", () => found.AddRange(ScanEpic()));
        Try("Xbox", () => found.AddRange(ScanXbox()));
        Try("Battle.net", () => found.AddRange(ScanBattleNet()));

        // A game can be listed by two launchers (Overwatch through Steam is Blizzard's too): the first one keeps it.
        return [.. found
            .Where(game => Directory.Exists(game.Folder))
            .DistinctBy(game => game.Folder.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    // ===== Steam: libraryfolders.vdf, then each library's appmanifest_*.acf =====

    private static IEnumerable<InstalledGame> ScanSteam()
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;

        if (steam is null)
            yield break;

        var libraries = new List<string> { steam };
        var folders = Path.Combine(steam, "steamapps", "libraryfolders.vdf");

        if (File.Exists(folders))
            libraries.AddRange(SteamLibraries(File.ReadAllText(folders)));

        foreach (var library in libraries.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(library, "steamapps");

            if (!Directory.Exists(apps))
                continue;

            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                if (SteamGame(File.ReadAllText(manifest), library) is { } game)
                    yield return game;
            }
        }
    }

    /// <summary>The library folders listed in Steam's libraryfolders.vdf.</summary>
    internal static IReadOnlyList<string> SteamLibraries(string vdf) =>
        [.. VdfPath().Matches(vdf).Select(match => Unescape(match.Groups[1].Value))];

    /// <summary>The game an appmanifest describes, or null for Steam's own tools and runtimes.</summary>
    internal static InstalledGame? SteamGame(string acf, string library)
    {
        var fields = AcfField().Matches(acf)
            .GroupBy(match => match.Groups[1].Value.ToLowerInvariant())
            .ToDictionary(group => group.Key, group => Unescape(group.First().Groups[2].Value));

        if (!fields.TryGetValue("name", out var name) || !fields.TryGetValue("installdir", out var folder))
            return null;

        if (fields.TryGetValue("appid", out var id) && SteamToolIds.Contains(id))
            return null;

        if (name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase))
            return null;

        return new InstalledGame(CleanName(name), Path.Combine(library, "steamapps", "common", folder), null, AppSource.Steam);
    }

    // ===== Epic Games: one .item (JSON) per game in the launcher's manifests =====

    private static IEnumerable<InstalledGame> ScanEpic()
    {
        var manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifests))
            yield break;

        foreach (var item in Directory.EnumerateFiles(manifests, "*.item"))
        {
            if (EpicGame(File.ReadAllText(item)) is { } game)
                yield return game;
        }
    }

    /// <summary>The game an Epic manifest describes, or null for anything that is not a game (Unreal Engine, plugins).</summary>
    internal static InstalledGame? EpicGame(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string? Text(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        var name = Text("DisplayName");
        var folder = Text("InstallLocation");

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(folder))
            return null;

        if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array &&
            !categories.EnumerateArray().Any(category => string.Equals(category.GetString(), "games", StringComparison.OrdinalIgnoreCase)))
            return null;

        var launch = Text("LaunchExecutable");
        var program = string.IsNullOrWhiteSpace(launch) ? null : Path.Combine(folder, launch.Replace('/', '\\'));

        return new InstalledGame(CleanName(name), folder.Replace('/', '\\'), program, AppSource.Epic);
    }

    // ===== Xbox app: the games folder on each drive (named in its .GamingRoot), one folder per game =====

    private static IEnumerable<InstalledGame> ScanXbox()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive is { DriveType: DriveType.Fixed, IsReady: true }))
        {
            var marker = Path.Combine(drive.RootDirectory.FullName, ".GamingRoot");
            var roots = File.Exists(marker) ? GamingRootFolders(File.ReadAllBytes(marker)) : ["XboxGames"];

            foreach (var root in roots)
            {
                var games = Path.Combine(drive.RootDirectory.FullName, root);

                if (!Directory.Exists(games))
                    continue;

                foreach (var folder in Directory.EnumerateDirectories(games))
                {
                    var content = Path.Combine(folder, "Content");

                    if (!Directory.Exists(content))
                        continue;

                    var config = Path.Combine(content, "MicrosoftGame.config");
                    var (name, program) = File.Exists(config) ? XboxConfig(File.ReadAllText(config)) : (null, null);

                    yield return new InstalledGame(
                        CleanName(name ?? Path.GetFileName(folder)),
                        content,
                        program is null ? null : Path.Combine(content, program),
                        AppSource.Xbox);
                }
            }
        }
    }

    /// <summary>
    /// The games folders a drive's .GamingRoot names: "RGBX", a count, then
    /// each folder in UTF-16, relative to the drive, ending in a zero.
    /// </summary>
    internal static IReadOnlyList<string> GamingRootFolders(byte[] marker)
    {
        if (marker.Length <= 8 || Encoding.ASCII.GetString(marker, 0, 4) != "RGBX")
            return ["XboxGames"];

        return [.. Encoding.Unicode.GetString(marker, 8, (marker.Length - 8) / 2 * 2)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(folder => folder.TrimStart('\\'))
            .Where(folder => folder.Length > 0)];
    }

    /// <summary>The game's name and main program from its MicrosoftGame.config; a name kept in a resource file is not readable here.</summary>
    internal static (string? Name, string? Program) XboxConfig(string config)
    {
        var name = XboxDisplayName().Match(config) is { Success: true } named ? named.Groups[1].Value : null;
        var program = XboxExecutable().Match(config) is { Success: true } run ? run.Groups[1].Value : null;

        return (name is null || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? null : name, program);
    }

    // ===== Battle.net: Blizzard's games each leave an entry in Windows' installed programs =====

    private static IEnumerable<InstalledGame> ScanBattleNet()
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

            if (uninstall is null)
                continue;

            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);

                if (BattleNetGame(
                        entry?.GetValue("DisplayName") as string,
                        entry?.GetValue("Publisher") as string,
                        entry?.GetValue("InstallLocation") as string) is { } game)
                    yield return game;
            }
        }
    }

    /// <summary>A Blizzard game from its installed-programs entry; Battle.net itself is not one.</summary>
    internal static InstalledGame? BattleNetGame(string? name, string? publisher, string? folder)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(folder) ||
            publisher?.Contains("Blizzard", StringComparison.OrdinalIgnoreCase) != true ||
            name.Contains("Battle.net", StringComparison.OrdinalIgnoreCase))
            return null;

        return new InstalledGame(CleanName(name), folder.TrimEnd('\\'), null, AppSource.BattleNet);
    }

    // ===== The game's program, for its icon =====

    /// <summary>
    /// The game's main program, for its icon: the one named by its launcher when
    /// it exists, otherwise the largest program in its folder that is not an
    /// installer, crash reporter or anti-cheat.
    /// </summary>
    public static string? FindProgram(InstalledGame game)
    {
        if (game.Program is { } named && File.Exists(named))
            return named;

        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true };

            return new DirectoryInfo(game.Folder)
                .EnumerateFiles("*.exe", options)
                .Take(400)
                .Where(file => !NotTheGame().IsMatch(file.Name))
                .MaxBy(file => file.Length)?.FullName;
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not look for {game.Name}'s program.", exception);
            return null;
        }
    }

    // ===== Helpers =====

    // Store names carry ™ and ®, and some a space at the end.
    internal static string CleanName(string name) =>
        name.Replace("™", string.Empty).Replace("®", string.Empty).Replace("©", string.Empty).Trim();

    private static string Unescape(string text) => text.Replace(@"\\", @"\").Replace("\\\"", "\"");

    private static void Try(string launcher, Action scan)
    {
        try
        {
            scan();
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not read the {launcher} library.", exception);
        }
    }
}

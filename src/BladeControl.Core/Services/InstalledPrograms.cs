using System.Text.RegularExpressions;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>An app on the PC, as listed on the Apps page: its name, what it is matched by (an .exe or a game's folder), its icon's program, and where it was found.</summary>
internal sealed record InstalledApp(string Name, string Path, string? IconPath, AppSource Source);

/// <summary>
/// The apps installed on the PC, for the Apps page: every program the Start
/// menu has a shortcut to (Windows' own left out), and the games found in
/// Steam, Epic Games, Xbox and Battle.net. A shortcut to a program inside a
/// game's folder is that game, listed once.
/// </summary>
internal static partial class InstalledPrograms
{
    // Shortcuts that are not the app itself: uninstallers, help, settings tools and the like.
    [GeneratedRegex(@"unins|uninstall|setup|install|readme|help|manual|license|update|repair|release notes|website|documentation|desinstalar|ayuda", RegexOptions.IgnoreCase)]
    private static partial Regex NotTheApp();

    /// <summary>Every app found, games first by folder, each once, by name.</summary>
    public static IReadOnlyList<InstalledApp> Scan()
    {
        var games = GameLibrary.Scan();
        var found = new List<InstalledApp>(games.Select(game => new InstalledApp(game.Name, game.Folder, GameLibrary.FindProgram(game), game.Source)));
        var gameFolders = games.Select(game => game.Folder.TrimEnd('\\') + "\\").ToList();

        try
        {
            foreach (var (name, program) in StartMenuPrograms())
            {
                if (gameFolders.Any(folder => program.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
                    continue;

                found.Add(new InstalledApp(name, program, program, AppSource.Manual));
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the Start menu's programs.", exception);
        }

        return [.. found
            .DistinctBy(app => app.Path.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase)
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    // The programs the Start menu's shortcuts open, for everyone and for this user.
    private static IEnumerable<(string Name, string Program)> StartMenuPrograms()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var shell = Type.GetTypeFromProgID("WScript.Shell") is { } type ? Activator.CreateInstance(type) : null;

        if (shell is null)
            yield break;

        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

        foreach (var shortcut in folders.Where(Directory.Exists).SelectMany(folder => Directory.EnumerateFiles(folder, "*.lnk", options)))
        {
            var name = Path.GetFileNameWithoutExtension(shortcut);

            if (IsNotTheApp(name) || TargetOf(shell, shortcut) is not { } program)
                continue;

            if (!program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                program.StartsWith(windows, StringComparison.OrdinalIgnoreCase) ||
                IsNotTheApp(Path.GetFileNameWithoutExtension(program)) ||
                !File.Exists(program))
                continue;

            yield return (GameLibrary.CleanName(name), program);
        }
    }

    /// <summary>A shortcut that is not the app itself: its uninstaller, help, readme and the like.</summary>
    internal static bool IsNotTheApp(string name) => NotTheApp().IsMatch(name);

    private static string? TargetOf(dynamic shell, string shortcut)
    {
        try
        {
            string target = shell.CreateShortcut(shortcut).TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : Environment.ExpandEnvironmentVariables(target);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

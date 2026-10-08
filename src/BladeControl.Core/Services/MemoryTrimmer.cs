namespace BladeControl.Core.Services;

/// <summary>A running program the trimmer may look at.</summary>
internal sealed record UserProgram(int Id, string Name, int Session);

/// <summary>Windows' side of freeing memory: the programs, which is in front, and trimming one.</summary>
internal interface IProcessMemory
{
    IReadOnlyList<UserProgram> Processes();

    /// <summary>The program whose window is in front (the game, while playing); 0 when none.</summary>
    int ForegroundProcessId();

    /// <summary>The session this app runs in: only programs in it are the user's.</summary>
    int CurrentSession();

    /// <summary>Asks Windows to page a program's unused memory out. False when it may not (another user's, protected).</summary>
    bool Trim(int processId);

    /// <summary>Physical memory free right now, in bytes.</summary>
    long AvailableBytes();
}

/// <summary>What a trim did: how many programs, and how much memory came free.</summary>
internal sealed record MemoryTrimResult(int Programs, long FreedBytes);

/// <summary>
/// "Free up memory": asks Windows to move the memory the user's background
/// programs are not using out of RAM. Nothing is closed and nothing is lost:
/// a program reads back what it needs when it next needs it. The program in
/// front (the game while playing) and this app are never touched, and
/// Windows itself refuses to trim system and other users' programs.
/// </summary>
internal sealed class MemoryTrimmer(IProcessMemory memory)
{
    public MemoryTrimResult Trim()
    {
        var before = memory.AvailableBytes();
        var foreground = memory.ForegroundProcessId();
        var session = memory.CurrentSession();
        var self = Environment.ProcessId;
        var trimmed = 0;

        foreach (var process in memory.Processes())
        {
            // Session 0 holds Windows' services; the program in front is left at full speed.
            if (process.Session != session || process.Id == foreground || process.Id == self || process.Id <= 4)
                continue;

            if (memory.Trim(process.Id))
                trimmed++;
        }

        return new MemoryTrimResult(trimmed, Math.Max(0, memory.AvailableBytes() - before));
    }
}

using System.Runtime.InteropServices;
using BladeControl.Core.Services;

namespace BladeControl.Core.Platform;

/// <summary>
/// The programs running now, with their paths and start times, for the Apps
/// page's profiles. Asked every couple of seconds, so each process is looked
/// up once and remembered until it ends; one Windows does not let us look at
/// (a protected service) is simply left out.
/// </summary>
internal sealed class RunningPrograms
{
    private const uint QueryLimitedInformation = 0x1000;

    private readonly Dictionary<int, RunningProgram?> _known = [];
    private readonly char[] _buffer = new char[1024];
    private readonly int _ownId = Environment.ProcessId;

    /// <summary>Every program running now that could be looked at, Blade Control left out.</summary>
    public IReadOnlyList<RunningProgram> Snapshot()
    {
        var ids = ProcessIds();
        var alive = new HashSet<int>(ids);

        foreach (var gone in _known.Keys.Where(id => !alive.Contains(id)).ToList())
            _known.Remove(gone);

        var programs = new List<RunningProgram>();

        foreach (var id in ids)
        {
            if (id is 0 or 4 || id == _ownId)
                continue;

            if (!_known.TryGetValue(id, out var program))
            {
                program = TryLookUp(id);
                _known[id] = program;
            }

            if (program is not null)
                programs.Add(program);
        }

        return programs;
    }

    private RunningProgram? TryLookUp(int id)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, id);

        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var size = _buffer.Length;

            if (!QueryFullProcessImageName(handle, 0, _buffer, ref size))
                return null;

            var started = GetProcessTimes(handle, out var creation, out _, out _, out _)
                ? DateTime.FromFileTimeUtc(creation)
                : DateTime.UtcNow;

            return new RunningProgram(id, new string(_buffer, 0, size), started);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static int[] ProcessIds()
    {
        var ids = new int[1024];

        while (true)
        {
            if (!EnumProcesses(ids, (uint)(ids.Length * sizeof(int)), out var bytesReturned))
                return [];

            if (bytesReturned < ids.Length * sizeof(int))
                return ids[..((int)bytesReturned / sizeof(int))];

            ids = new int[ids.Length * 2];
        }
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumProcesses([Out] int[] processIds, uint size, out uint bytesReturned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, [Out] char[] path, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

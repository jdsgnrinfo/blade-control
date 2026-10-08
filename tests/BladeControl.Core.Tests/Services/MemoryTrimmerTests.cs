using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class MemoryTrimmerTests
{
    [Fact]
    public void TrimsTheUsersBackgroundPrograms_AndReportsWhatCameFree()
    {
        var memory = new FakeMemory
        {
            Programs = [new(100, "browser", 1), new(200, "chat", 1)],
            Available = [1_000, 1_600]
        };

        var result = new MemoryTrimmer(memory).Trim();

        Assert.Equal([100, 200], memory.Trimmed);
        Assert.Equal(new MemoryTrimResult(2, 600), result);
    }

    [Fact]
    public void LeavesTheProgramInFront_ThisApp_AndWindowsServices()
    {
        var memory = new FakeMemory
        {
            Programs =
            [
                new(300, "game", 1),
                new(Environment.ProcessId, "RazerHelper", 1),
                new(400, "svchost", 0),
                new(4, "System", 1),
                new(500, "browser", 1)
            ],
            Foreground = 300
        };

        new MemoryTrimmer(memory).Trim();

        Assert.Equal([500], memory.Trimmed);
    }

    [Fact]
    public void CountsOnlyProgramsWindowsLetItTrim()
    {
        var memory = new FakeMemory
        {
            Programs = [new(100, "mine", 1), new(200, "protected", 1)],
            Refuse = [200]
        };

        Assert.Equal(1, new MemoryTrimmer(memory).Trim().Programs);
    }

    [Fact]
    public void NeverReportsLessThanNothing()
    {
        var memory = new FakeMemory { Programs = [new(100, "busy", 1)], Available = [2_000, 1_500] };

        Assert.Equal(0, new MemoryTrimmer(memory).Trim().FreedBytes);
    }

    private sealed class FakeMemory : IProcessMemory
    {
        private int _reads;

        public List<UserProgram> Programs { get; init; } = [];

        public int Foreground { get; init; }

        public long[] Available { get; init; } = [0, 0];

        public int[] Refuse { get; init; } = [];

        public List<int> Trimmed { get; } = [];

        public IReadOnlyList<UserProgram> Processes() => Programs;

        public int ForegroundProcessId() => Foreground;

        public int CurrentSession() => 1;

        public bool Trim(int processId)
        {
            if (Refuse.Contains(processId))
                return false;

            Trimmed.Add(processId);
            return true;
        }

        public long AvailableBytes() => Available[Math.Min(_reads++, Available.Length - 1)];
    }
}

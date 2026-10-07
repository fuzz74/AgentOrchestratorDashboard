using OrchDash.Core.Claude;
using OrchDash.Core.Copilot;
using OrchDash.Core.Model;
using OrchDash.Core.Replay;
using OrchDash.Core.RunFolder;
using OrchDash.Core.Store;
using OrchDash.Core.Tests.Fixtures;
using Xunit;

namespace OrchDash.Core.Tests.Replay;

// 30.2 and 30.4 on the copilot-run fixture: the resume, sync and resolver paths of the task count.
// The times are local wall time, as in progress.md.
public sealed class CopilotReplayTests
{
    private static readonly Lazy<RunSnapshot> Live = new(ReadFixture);

    private static RunSnapshot ReadFixture()
    {
        using var store = new RunStore(FixturePaths.CopilotRepo, new RunFolderReader(),
            (p, w) => p == Provider.Claude ? new ClaudeSessionParser(w) : new CopilotSessionParser(w));
        store.Poll();
        return store.Current;
    }

    private static DateTimeOffset Local(int hour, int minute, int second) =>
        new(new DateTime(2026, 10, 3, hour, minute, second, DateTimeKind.Local));

    private static TaskView Count(RunSnapshot replay) => replay.Tasks.Single(task => task.Id == "count");

    [Fact]
    public void At_the_resume_the_run_has_restarted_and_count_is_starting()
    {
        var replay = SnapshotReplay.At(Live.Value, Local(11, 37, 31));
        var count = Count(replay);

        Assert.Equal((Local(11, 37, 31), Provider.Copilot, RunPhase.Running),
            (replay.Run.StartedAt, replay.Run.Provider, replay.Run.Phase));
        Assert.Equal((TaskState.Running, "starting", "resume"), (count.Status, count.Detail, count.Mode));
    }

    [Fact]
    public void At_the_first_sync_count_runs_in_sync_mode()
    {
        var count = Count(SnapshotReplay.At(Live.Value, Local(11, 50, 46)));

        Assert.Equal((TaskState.Running, "sync", 1), (count.Status, count.Mode, count.SyncRuns));
    }

    [Fact]
    public void After_the_first_sync_the_resolver_runs()
    {
        var count = Count(SnapshotReplay.At(Live.Value, Local(11, 50, 47)));

        Assert.Equal((TaskState.Running, "resolver (attempt 1)"), (count.Status, count.Detail));
    }

    [Fact]
    public void After_the_second_sync_count_is_Done()
    {
        var count = Count(SnapshotReplay.At(Live.Value, Local(11, 52, 11)));

        Assert.Equal((TaskState.Done, 2, 1, "0.00 USD, 1 attempt(s)"), (count.Status, count.SyncRuns, count.Attempts, count.Detail));
    }
}

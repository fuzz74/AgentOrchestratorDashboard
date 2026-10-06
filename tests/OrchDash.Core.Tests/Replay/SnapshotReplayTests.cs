using System.Runtime.InteropServices;
using OrchDash.Core.Model;
using OrchDash.Core.Replay;
using Xunit;
using static OrchDash.Core.Tests.Replay.ReplayData;

namespace OrchDash.Core.Tests.Replay;

// 30.1, 30.2, 30.5 and 30.6 on the whole snapshot, and the edges of the time range.
public sealed class SnapshotReplayTests
{
    [Fact]
    public void The_replay_keeps_the_live_members_and_has_no_processes()
    {
        var live = Create();
        var at = At(12, 5, 0);

        var replay = SnapshotReplay.At(live, at);

        Assert.Equal(at, replay.ReadAt);
        Assert.Equal(live.Version, replay.Version);
        Assert.Equal(live.RepoPath, replay.RepoPath);
        Assert.Same(live.Plan, replay.Plan);
        Assert.Equal(live.Problems, replay.Problems);
        Assert.Same(live.Git, replay.Git);
        Assert.Same(ProcessInfo.Empty, replay.Processes);
    }

    [Fact]
    public void Progress_holds_the_entries_up_to_the_time_in_file_order()
    {
        var live = Create();
        var at = At(12, 0, 5);

        var replay = SnapshotReplay.At(live, at);

        Assert.Equal(live.Progress.Take(5), replay.Progress);
        Assert.True(live.Progress[5].Time > at);
    }

    [Fact]
    public void Run_is_derived_from_the_cut_progress_with_the_lock_held()
    {
        var replay = SnapshotReplay.At(Create(), At(12, 20, 59));

        Assert.Equal(new RunInfo(RunPhase.Running, At(12, 0, 0), null, 2, Provider.Claude, @"C:\bin\claude.exe", false), replay.Run);
    }

    [Fact]
    public void A_graceful_stop_entry_up_to_the_time_gives_Stopping()
    {
        var replay = SnapshotReplay.At(Create(), At(12, 21, 0));

        Assert.Equal((RunPhase.Stopping, true), (replay.Run.Phase, replay.Run.StopRequested));
    }

    [Fact]
    public void Without_a_graceful_stop_entry_the_run_stays_Running()
    {
        var replay = SnapshotReplay.At(Create(withStop: false), End);

        Assert.Equal((RunPhase.Running, false), (replay.Run.Phase, replay.Run.StopRequested));
    }

    [Fact]
    public void A_graceful_stop_entry_with_a_source_does_not_count()
    {
        var live = Snapshot([
            Entry(At(12, 0, 0), null, "Run started: 1 tasks, max 1 in parallel"),
            Entry(At(12, 1, 0), "alpha", "Graceful stop requested"),
        ]);

        var replay = SnapshotReplay.At(live, End);

        Assert.Equal((RunPhase.Running, false), (replay.Run.Phase, replay.Run.StopRequested));
    }

    [Fact]
    public void Before_the_run_started_the_phase_is_Planning()
    {
        var replay = SnapshotReplay.At(Create(), At(11, 59, 0));

        Assert.Equal(RunPhase.Planning, replay.Run.Phase);
    }

    [Fact]
    public void Before_every_entry_nothing_has_happened()
    {
        var replay = SnapshotReplay.At(Create(), At(11, 0, 0));

        Assert.Empty(replay.Progress);
        Assert.Equal(new RunInfo(RunPhase.NotStarted, null, null, null, Provider.Unknown, null, false), replay.Run);
        Assert.Empty(replay.Sessions);
        Assert.All(replay.Tasks, task => Assert.Equal(TaskState.Pending, task.Status));
        Assert.Equal(["ready", "ready", "waiting for alpha", "waiting for gamma", "waiting for delta"],
            replay.Tasks.Select(task => task.Detail));
        Assert.Empty(replay.Commands);
    }

    [Fact]
    public void After_everything_the_replay_equals_the_live_snapshot_but_for_ReadAt_and_Processes()
    {
        var live = Create();
        var at = End.AddHours(1);

        var replay = SnapshotReplay.At(live, at);

        Assert.Equal(live.Tasks, replay.Tasks);
        Assert.Equal(live.Run, replay.Run);
        Assert.Equal(live with { ReadAt = at, Processes = ProcessInfo.Empty }, replay);
    }

    [Fact]
    public void After_everything_the_live_arrays_are_returned()
    {
        var live = Create();

        var replay = SnapshotReplay.At(live, End);

        Assert.Same(ImmutableCollectionsMarshal.AsArray(live.Progress), ImmutableCollectionsMarshal.AsArray(replay.Progress));
        Assert.Same(ImmutableCollectionsMarshal.AsArray(live.Sessions), ImmutableCollectionsMarshal.AsArray(replay.Sessions));
        Assert.Same(ImmutableCollectionsMarshal.AsArray(live.Commands), ImmutableCollectionsMarshal.AsArray(replay.Commands));
        Assert.Same(ImmutableCollectionsMarshal.AsArray(live.Tasks), ImmutableCollectionsMarshal.AsArray(replay.Tasks));
    }

    [Fact]
    public void Commands_are_the_logs_written_up_to_the_time_resolved_at_that_time()
    {
        var live = Create();

        var replay = SnapshotReplay.At(live, At(12, 6, 10));

        Assert.Equal(
            [("alpha", CommandKind.Acceptance, CommandOutcome.Running), ("alpha", CommandKind.Setup, CommandOutcome.Passed)],
            replay.Commands.Select(log => (log.TaskId, log.Kind, log.Outcome)));
        Assert.Equal(CommandOutcome.Passed, live.Commands[1].Outcome);
        Assert.Equal("dotnet restore Sample.slnx", replay.Commands[1].Command);
    }

    [Fact]
    public void While_no_log_is_cut_the_live_command_list_is_returned_unresolved()
    {
        var live = Create();

        var replay = SnapshotReplay.At(live, At(12, 20, 0));

        Assert.NotEqual(live.Progress.Length, replay.Progress.Length);
        Assert.Same(ImmutableCollectionsMarshal.AsArray(live.Commands), ImmutableCollectionsMarshal.AsArray(replay.Commands));
    }

    [Fact]
    public void The_empty_snapshot_replays_to_itself_with_the_time()
    {
        var empty = RunSnapshot.Empty(@"C:\Work\Repo");
        var at = At(12, 0, 0);

        var replay = SnapshotReplay.At(empty, at);

        Assert.Equal(empty with { ReadAt = at }, replay);
    }

    [Fact]
    public void Default_arrays_count_as_empty()
    {
        var session = new Session(Files("alpha", AgentRole.Worker), Provider.Claude, SessionState.Running, "prompt", At(12, 0, 0),
            new SessionContent(null, null, null, default, default, null, null, At(12, 10, 0), 0))
        {
            Stores = new StoreData(null, default, default, default, default, null, null, null, 0),
            Unavailable = default,
        };
        var task = new TaskView("alpha", "Alpha", "Prompt", default, default, null, null, 1, 0, TaskState.Pending, "fresh",
            0, 0, 0, 0, null, null, null, null, null, null, null, null, "ready");
        var live = new RunSnapshot(1, End, @"C:\Work\Repo", RunSnapshot.Empty("x").Run, null, [task, task with { Id = "beta" }],
            [session], default, default) { Commands = default };

        var replay = SnapshotReplay.At(live, At(12, 5, 0));

        Assert.Empty(replay.Progress);
        Assert.Empty(replay.Commands);
        Assert.Equal(2, replay.Tasks.Length);
        Assert.Null(Assert.Single(replay.Sessions).Content.LastEventAt);
    }

    [Fact]
    public void A_snapshot_of_default_arrays_only_does_not_throw()
    {
        var live = new RunSnapshot(1, End, "", RunSnapshot.Empty("x").Run, null, default, default, default, default) { Commands = default };

        var replay = SnapshotReplay.At(live, End);

        Assert.True(replay.Tasks.IsEmpty && replay.Sessions.IsEmpty && replay.Progress.IsEmpty && replay.Commands.IsEmpty);
    }
}

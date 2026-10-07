using OrchDash.Core.Model;
using OrchDash.Core.Replay;
using Xunit;
using static OrchDash.Core.Tests.Replay.ReplayData;

namespace OrchDash.Core.Tests.Replay;

// 30.4: the task replay table and the step table, one test per row.
public sealed class TaskReplayTests
{
    private static TaskView TaskAt(string id, DateTimeOffset at) =>
        SnapshotReplay.At(Create(), at).Tasks.Single(task => task.Id == id);

    private static TaskView TaskAt(string id, int hour, int minute, int second) => TaskAt(id, At(hour, minute, second));

    private static RunSnapshot Small(IEnumerable<ProgressEntry> taskEntries, IEnumerable<TaskView> tasks, IEnumerable<Session>? sessions = null) =>
        Snapshot(
            [Entry(At(12, 0, 0), null, "Run started: 2 tasks, max 1 in parallel"), Entry(At(12, 0, 0), null, @"Claude: C:\bin\claude.exe"), .. taskEntries],
            tasks, sessions);

    [Fact]
    public void Tasks_keep_their_order_and_plan_fields()
    {
        var live = Create();

        var replay = SnapshotReplay.At(live, At(12, 5, 0));

        Assert.Equal(["alpha", "gamma", "beta", "delta", "epsilon"], replay.Tasks.Select(task => task.Id));
        Assert.All(replay.Tasks.Zip(live.Tasks), pair =>
        {
            var (task, plan) = pair;
            Assert.Equal((plan.Title, plan.Prompt, plan.Acceptance, plan.Model, plan.Wave, plan.Dependents),
                (task.Title, task.Prompt, task.Acceptance, task.Model, task.Wave, task.Dependents));
            Assert.Equal(plan.Deps, task.Deps);
            Assert.Equal(plan.Owns, task.Owns);
        });
    }

    [Theory]
    [InlineData("alpha", 11, 0, 0, TaskState.Pending)]
    [InlineData("alpha", 12, 0, 5, TaskState.Running)]
    [InlineData("alpha", 12, 9, 30, TaskState.Done)]
    [InlineData("gamma", 12, 19, 59, TaskState.Running)]
    [InlineData("gamma", 12, 20, 0, TaskState.Failed)]
    [InlineData("beta", 12, 11, 30, TaskState.Pending)]
    [InlineData("beta", 12, 12, 0, TaskState.Pending)]
    [InlineData("beta", 12, 12, 1, TaskState.Running)]
    [InlineData("delta", 12, 19, 59, TaskState.Pending)]
    [InlineData("delta", 12, 20, 0, TaskState.Blocked)]
    [InlineData("epsilon", 12, 20, 0, TaskState.Blocked)]
    public void Status_follows_the_latest_deciding_entry(string id, int hour, int minute, int second, TaskState expected)
    {
        Assert.Equal(expected, TaskAt(id, hour, minute, second).Status);
    }

    [Fact]
    public void A_dependency_cycle_does_not_hang()
    {
        var tasks = new[] { Task("a", 1, ["b"]), Task("b", 1, ["a"]), Task("c", 1, ["c"]) };

        var replay = SnapshotReplay.At(Small([], tasks), End);

        Assert.Equal(["waiting for b", "waiting for a", "waiting for c"], replay.Tasks.Select(task => task.Detail));
    }

    [Fact]
    public void Blocked_follows_failed_deps_through_a_cycle()
    {
        var tasks = new[] { Task("a"), Task("b", 2, ["a", "c"]), Task("c", 3, ["b"]) };

        var replay = SnapshotReplay.At(Small([Entry(At(12, 1, 0), "a", "FAILED: Setup command failed")], tasks), End);

        Assert.Equal([TaskState.Failed, TaskState.Blocked, TaskState.Blocked], replay.Tasks.Select(task => task.Status));
    }

    [Theory]
    [InlineData(12, 11, 59, "fresh")]
    [InlineData(12, 12, 1, "sync")]
    public void Mode_is_the_mode_of_the_latest_start(int hour, int minute, int second, string expected)
    {
        Assert.Equal(expected, TaskAt("beta", hour, minute, second).Mode);
    }

    [Fact]
    public void Mode_is_fresh_without_a_start()
    {
        Assert.Equal("fresh", TaskAt("delta", End).Mode);
    }

    [Theory]
    [InlineData(12, 0, 0, 0)]
    [InlineData(12, 10, 54, 1)]
    [InlineData(12, 10, 55, 2)]
    [InlineData(12, 20, 0, 3)]
    public void Attempts_is_the_n_of_the_latest_attempt_entry(int hour, int minute, int second, int expected)
    {
        Assert.Equal(expected, TaskAt("gamma", hour, minute, second).Attempts);
    }

    [Theory]
    [InlineData(12, 12, 0, 0)]
    [InlineData(12, 12, 1, 1)]
    public void SyncRuns_counts_the_sync_starts(int hour, int minute, int second, int expected)
    {
        Assert.Equal(expected, TaskAt("beta", hour, minute, second).SyncRuns);
    }

    [Fact]
    public void StartedAt_is_the_latest_start_and_FinishedAt_the_end_after_it()
    {
        Assert.Equal((At(12, 0, 5), (DateTimeOffset?)null), (TaskAt("alpha", 12, 9, 29).StartedAt, TaskAt("alpha", 12, 9, 29).FinishedAt));
        Assert.Equal((At(12, 0, 5), At(12, 9, 30)), (TaskAt("alpha", 12, 9, 30).StartedAt, TaskAt("alpha", 12, 9, 30).FinishedAt));
        Assert.Equal(At(12, 12, 1), TaskAt("beta", 12, 12, 1).StartedAt);
        Assert.Null(TaskAt("delta", End).StartedAt);
    }

    [Fact]
    public void A_restart_after_an_end_clears_FinishedAt()
    {
        var tasks = new[] { Task("a") };
        var live = Small([
            Entry(At(12, 1, 0), "a", "started (fresh) in X"),
            Entry(At(12, 2, 0), "a", "FAILED: merge kept conflicting"),
            Entry(At(12, 3, 0), "a", "started (resume) in X"),
        ], tasks);

        var task = Assert.Single(SnapshotReplay.At(live, End).Tasks);

        Assert.Equal((TaskState.Running, "resume", At(12, 3, 0), (DateTimeOffset?)null), (task.Status, task.Mode, task.StartedAt, task.FinishedAt));
    }

    [Fact]
    public void Error_is_the_whole_message_without_the_FAILED_colon_prefix()
    {
        var gamma = TaskAt("gamma", End);

        Assert.Equal((GammaError, GammaError), (gamma.Error, gamma.Detail));
    }

    [Fact]
    public void Error_is_the_message_after_FAILED_colon()
    {
        var live = Small([Entry(At(12, 1, 0), "a", "started (fresh) in X"), Entry(At(12, 2, 0), "a", "FAILED: Setup command failed (exit 1)\nmore")],
            [Task("a")]);

        var task = Assert.Single(SnapshotReplay.At(live, End).Tasks);

        Assert.Equal(("Setup command failed (exit 1)\nmore", "Setup command failed (exit 1)"), (task.Error, task.Detail));
    }

    [Fact]
    public void Error_is_null_unless_Failed()
    {
        Assert.Null(TaskAt("gamma", 12, 19, 59).Error);
        Assert.Null(TaskAt("alpha", End).Error);
    }

    [Theory]
    [InlineData("alpha", 12, 5, 49, 0.0)]
    [InlineData("alpha", 12, 5, 50, 0.25)]
    [InlineData("alpha", 12, 7, 30, 0.375)]
    [InlineData("alpha", 12, 8, 55, 0.5)]
    [InlineData("gamma", 12, 5, 0, 0.25)]
    [InlineData("gamma", 12, 15, 30, 0.75)]
    [InlineData("beta", 12, 30, 0, 0.0)]
    public void CostUsd_sums_the_shown_results(string id, int hour, int minute, int second, double expected)
    {
        Assert.Equal(expected, TaskAt(id, hour, minute, second).CostUsd);
    }

    [Theory]
    [InlineData("alpha", 12, 0, 9, null)]
    [InlineData("alpha", 12, 9, 0, "s-alpha-1")]
    [InlineData("gamma", 12, 10, 59, "s-gamma-1")]
    [InlineData("gamma", 12, 11, 0, "s-gamma-2")]
    [InlineData("beta", 12, 12, 5, "s-beta-2")]
    [InlineData("delta", 12, 30, 0, null)]
    public void SessionId_is_the_latest_kept_worker_session(string id, int hour, int minute, int second, string? expected)
    {
        Assert.Equal(expected, TaskAt(id, hour, minute, second).SessionId);
    }

    [Fact]
    public void Summary_and_Notes_come_from_the_latest_shown_worker_report()
    {
        Assert.Equal((null, null), (TaskAt("alpha", 12, 5, 49).Summary, TaskAt("alpha", 12, 5, 49).Notes));
        Assert.Equal((AlphaSummary, AlphaNotes), (TaskAt("alpha", 12, 5, 50).Summary, TaskAt("alpha", 12, 5, 50).Notes));
        Assert.Null(TaskAt("gamma", 12, 15, 29).Summary);
        Assert.Equal((GammaSummary, (string?)null), (TaskAt("gamma", 12, 15, 30).Summary, TaskAt("gamma", 12, 15, 30).Notes));
    }

    [Theory]
    [InlineData(12, 7, 29, null)]
    [InlineData(12, 7, 30, SpecFeedback)]
    [InlineData(12, 8, 55, QualityFeedback)]
    public void Feedback_is_the_latest_shown_review_that_did_not_pass(int hour, int minute, int second, string? expected)
    {
        Assert.Equal(expected, TaskAt("alpha", hour, minute, second).Feedback);
    }

    [Theory]
    [InlineData(12, 7, 29, 0)]
    [InlineData(12, 7, 30, 1)]
    [InlineData(12, 8, 55, 1)]
    public void SpecRejections_counts_the_shown_reviews_without_spec_pass(int hour, int minute, int second, int expected)
    {
        Assert.Equal(expected, TaskAt("alpha", hour, minute, second).SpecRejections);
    }

    [Fact]
    public void A_review_that_passed_both_gives_no_feedback_and_no_rejection()
    {
        var review = WithResult(
            Session(Files("a", AgentRole.Reviewer, 1, 1), At(12, 1, 0), SessionState.Succeeded, "r", lastEventAt: At(12, 2, 0)),
            Result(null, review: new ReviewVerdict("pass", "pass", "All good", [])));
        var live = Small([Entry(At(12, 1, 0), "a", "review started (sonnet)")], [Task("a")], [review]);

        var task = Assert.Single(SnapshotReplay.At(live, End).Tasks);

        Assert.Equal((null, 0), (task.Feedback, task.SpecRejections));
    }

    [Fact]
    public void MergedSha_is_the_live_value_once_Done()
    {
        Assert.Null(TaskAt("alpha", 12, 9, 29).MergedSha);
        Assert.Equal("3f9c2e1", TaskAt("alpha", 12, 9, 30).MergedSha);
    }

    [Fact]
    public void Detail_of_a_Done_task_has_the_cost_and_attempts()
    {
        Assert.Equal("0.50 USD, 1 attempt(s)", TaskAt("alpha", 12, 9, 30).Detail);
    }

    [Fact]
    public void Detail_of_a_Blocked_task_says_a_dependency_failed()
    {
        Assert.Equal("a dependency failed", TaskAt("epsilon", 12, 20, 0).Detail);
    }

    [Theory]
    [InlineData("beta", 12, 9, 29, "waiting for alpha")]
    [InlineData("epsilon", 12, 19, 59, "waiting for delta")]
    [InlineData("beta", 12, 9, 30, "waiting for gamma (owns overlap)")]
    [InlineData("beta", 12, 11, 30, "waiting for gamma (owns overlap)")]
    public void Detail_of_a_Pending_task_waits_for_deps_then_owns(string id, int hour, int minute, int second, string expected)
    {
        Assert.Equal(expected, TaskAt(id, hour, minute, second).Detail);
    }

    [Fact]
    public void Detail_of_a_Pending_task_waits_for_the_first_overlapping_running_task()
    {
        var tasks = new[] { Task("a", owns: ["src/**"]), Task("b", owns: ["src/B/**"]), Task("c", owns: ["src/B/C/**"]) };
        var live = Small([Entry(At(12, 1, 0), "c", "started (fresh) in X"), Entry(At(12, 1, 0), "b", "started (fresh) in X")], tasks);

        Assert.Equal("waiting for b (owns overlap)", SnapshotReplay.At(live, End).Tasks[0].Detail);
    }

    [Fact]
    public void Detail_of_a_Pending_task_waits_for_a_free_slot_when_max_tasks_run()
    {
        var live = Small([Entry(At(12, 1, 0), "a", "started (fresh) in X")], [Task("a"), Task("b")]);

        Assert.Equal("waiting for a free slot", SnapshotReplay.At(live, End).Tasks[1].Detail);
    }

    [Fact]
    public void Detail_of_a_Pending_task_is_ready_otherwise()
    {
        var live = Small([Entry(At(12, 1, 0), "a", "started (fresh) in X"), Entry(At(12, 2, 0), "a", "DONE and merged")], [Task("a"), Task("b")]);

        Assert.Equal("ready", SnapshotReplay.At(live, End).Tasks[1].Detail);
    }

    [Theory]
    [InlineData("alpha", 12, 0, 5, "starting")]
    [InlineData("alpha", 12, 0, 8, "setup")]
    [InlineData("alpha", 12, 0, 9, "worker (attempt 1)")]
    [InlineData("alpha", 12, 1, 0, "working")]
    [InlineData("alpha", 12, 5, 20, "resolver (attempt 1)")]
    [InlineData("alpha", 12, 6, 0, "acceptance (attempt 1)")]
    [InlineData("alpha", 12, 6, 25, "review (attempt 1)")]
    [InlineData("alpha", 12, 9, 0, "integration check")]
    [InlineData("gamma", 12, 4, 31, "working")]
    [InlineData("gamma", 12, 15, 35, "acceptance (attempt 2)")]
    [InlineData("beta", 12, 12, 1, "starting")]
    [InlineData("beta", 12, 12, 2, "worker (attempt 2)")]
    public void Detail_of_a_Running_task_follows_the_step_table(string id, int hour, int minute, int second, string expected)
    {
        var task = TaskAt(id, hour, minute, second);

        Assert.Equal((TaskState.Running, expected), (task.Status, task.Detail));
    }

    [Fact]
    public void A_step_before_any_attempt_counts_as_attempt_1()
    {
        var live = Small([Entry(At(12, 1, 0), "a", "started (fresh) in X"), Entry(At(12, 2, 0), "a", "acceptance: dotnet test")], [Task("a")]);

        Assert.Equal("acceptance (attempt 1)", SnapshotReplay.At(live, End).Tasks[0].Detail);
    }

    [Fact]
    public void Entries_of_the_same_second_count_in_file_order()
    {
        var live = Small([
            Entry(At(12, 1, 0), "a", "review passed"),
            Entry(At(12, 1, 0), "a", "merge conflict with newer integration work; re-queued to sync"),
            Entry(At(12, 1, 0), "a", "started (sync) in X"),
        ], [Task("a")]);

        var task = SnapshotReplay.At(live, At(12, 1, 0)).Tasks[0];

        Assert.Equal((TaskState.Running, "sync", "starting"), (task.Status, task.Mode, task.Detail));
    }

    [Fact]
    public void A_task_whose_fields_all_equal_is_the_live_instance()
    {
        var live = Create();

        var replay = SnapshotReplay.At(live, At(12, 20, 0));

        Assert.Same(live.Tasks[Delta], replay.Tasks[Delta]);
        Assert.NotSame(live.Tasks[Beta], replay.Tasks[Beta]);
    }
}

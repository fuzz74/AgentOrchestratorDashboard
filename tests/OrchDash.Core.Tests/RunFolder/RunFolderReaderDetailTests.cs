using System.Globalization;
using System.Text.Json.Nodes;
using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.RunFolder.TempRunFolder;

namespace OrchDash.Core.Tests.RunFolder;

public sealed class RunFolderReaderDetailTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);

    private readonly TempRunFolder _run = new();

    public void Dispose() => _run.Dispose();

    [Fact]
    public void Done_shows_cost_and_attempts_in_the_invariant_culture()
    {
        _run.WriteTasks(Task("a"));
        _run.WriteState(new JsonObject { ["a"] = new JsonObject { ["status"] = "done", ["costUsd"] = 1.1573, ["attempts"] = 2 } });

        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nb-NO");
        try
        {
            Assert.Equal("1.16 USD, 2 attempt(s)", Assert.Single(_run.Read().Tasks).Detail);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("acceptance failed: 2 tests failed", "acceptance failed: 2 tests failed")]
    [InlineData("\r\n   \r\nworker error: crashed\r\nstack", "worker error: crashed")]
    [InlineData(null, "")]
    public void Failed_shows_the_first_non_empty_line_of_the_error(string? error, string expected)
    {
        _run.WriteTasks(Task("a"));
        _run.WriteState(new JsonObject { ["a"] = new JsonObject { ["status"] = "failed", ["error"] = error } });

        Assert.Equal(expected, Assert.Single(_run.Read().Tasks).Detail);
    }

    [Fact]
    public void Blocked_shows_that_a_dependency_failed()
    {
        _run.WriteTasks(Task("a"), Task("b", ["a"]));
        _run.WriteState(("a", "failed"));

        Assert.Equal("a dependency failed", _run.Read().Tasks[1].Detail);
    }

    [Fact]
    public void Pending_waits_for_the_deps_that_are_not_done_in_deps_order()
    {
        _run.WriteTasks(Task("a"), Task("b"), Task("c"), Task("d", ["c", "missing", "a", "b"]));
        _run.WriteState(("a", "running"), ("b", "done"));

        Assert.Equal("waiting for c, a", _run.Read().Tasks[3].Detail);
    }

    [Theory]
    [InlineData(new[] { "src/Core/**" }, new[] { "SRC\\core\\Parser.cs" }, true)]
    [InlineData(new[] { "./src/Core/*.cs" }, new[] { "src/core/sub/x.cs" }, true)]
    [InlineData(new[] { "src/A[bc]/x.cs" }, new[] { "src/Ad/x.cs" }, true)]
    [InlineData(new[] { "src/Core?/x.cs" }, new[] { "src/Cor" }, true)]
    [InlineData(new[] { "**" }, new[] { "tests/x.cs" }, true)]
    [InlineData(new[] { "src/Other/**", "tests/Core/**" }, new[] { "src/Core/**", "tests/Core/A.cs" }, true)]
    [InlineData(new string[0], new[] { "src/Core/**" }, true)]
    [InlineData(null, new[] { "src/Core/**" }, true)]
    [InlineData(new[] { "src/Core/**" }, null, true)]
    [InlineData(new[] { "src/Core/**" }, new[] { "src/Other/**" }, false)]
    [InlineData(new[] { "src/Core.Tests/**" }, new[] { "src/Core/**" }, false)]
    public void Pending_waits_for_a_running_task_whose_owns_overlap(string[]? runningOwns, string[]? pendingOwns, bool overlap)
    {
        _run.WriteTasks(Task("done"), Task("run", owns: runningOwns), Task("other", owns: ["docs/**"]), Task("p", ["done"], pendingOwns));
        _run.WriteState(("done", "done"), ("other", "running"), ("run", "running"));

        var detail = _run.Read().Tasks[3].Detail;

        Assert.Equal(overlap ? "waiting for run (owns overlap)" : "ready", detail);
    }

    [Fact]
    public void Owns_overlap_names_the_first_overlapping_running_task()
    {
        _run.WriteTasks(Task("p", owns: ["src/**"]), Task("r1", owns: ["src/A/**"]), Task("r2", owns: ["src/B/**"]));
        _run.WriteState(("r1", "running"), ("r2", "running"));

        Assert.Equal("waiting for r1 (owns overlap)", _run.Read().Tasks[0].Detail);
    }

    [Theory]
    [InlineData(2, "waiting for a free slot")]
    [InlineData(3, "ready")]
    public void Pending_waits_for_a_free_slot_when_max_parallel_tasks_run(int maxParallel, string expected)
    {
        _run.Write("progress.md", $"2026-10-03 12:00:00  Run started: 3 tasks, max {maxParallel} in parallel\n");
        _run.WriteTasks(Task("r1", owns: ["src/A/**"]), Task("r2", owns: ["src/B/**"]), Task("p", owns: ["src/C/**"]));
        _run.WriteState(("r1", "running"), ("r2", "running"));

        var data = _run.Read();

        Assert.Equal(maxParallel, data.Run.MaxParallel);
        Assert.Equal(expected, data.Tasks[2].Detail);
    }

    [Fact]
    public void Pending_is_ready_without_max_parallel()
    {
        _run.WriteTasks(Task("r1", owns: ["src/A/**"]), Task("p", owns: ["src/C/**"]));
        _run.WriteState(("r1", "running"));

        var data = _run.Read();

        Assert.Null(data.Run.MaxParallel);
        Assert.Equal("ready", data.Tasks[1].Detail);
    }

    [Theory]
    [InlineData("setup.log", "setup")]
    [InlineData("setup.log.stderr", "working")]
    [InlineData("attempt-2-worker.json.prompt.md", "worker (attempt 2)")]
    [InlineData("attempt-2-worker.json.events.jsonl", "worker (attempt 2)")]
    [InlineData("attempt-2-worker.json.stderr", "worker (attempt 2)")]
    [InlineData("attempt-2-worker.json", "worker (attempt 2) finished")]
    [InlineData("attempt-2-worker.json.nudge.json.events.jsonl", "worker (attempt 2)")]
    [InlineData("attempt-2-worker.json.nudge.json", "worker (attempt 2) finished")]
    [InlineData("attempt-1-resolver.json.events.jsonl", "resolver (attempt 1)")]
    [InlineData("attempt-1-resolver.json", "resolver (attempt 1) finished")]
    [InlineData("attempt-3-review-2.json.prompt.md", "review (attempt 3)")]
    [InlineData("attempt-3-review-2.json", "review (attempt 3) finished")]
    [InlineData("attempt-3-review-2.json.nudge.json", "review (attempt 3) finished")]
    [InlineData("attempt-1-acceptance.log", "acceptance (attempt 1)")]
    [InlineData("attempt-1-acceptance.log.stderr", "acceptance (attempt 1)")]
    [InlineData("attempt-1-worker", "working")]
    [InlineData("notes.txt", "working")]
    public void Running_shows_the_step_of_the_newest_file_in_the_latest_start_folder(string newestFile, string expected)
    {
        WriteRunningTask();
        _run.Touch("logs/t/20261003-120000/older.txt", T0);
        _run.Touch("logs/t/20261003-120000/" + newestFile, T0.AddMinutes(1));
        // An earlier start folder with a newer file does not count.
        _run.Touch("logs/t/20261003-110000/setup.log", T0.AddMinutes(2));

        Assert.Equal(expected, _run.Read().Tasks[0].Detail);
    }

    [Fact]
    public void Running_shows_integration_check_when_an_integration_log_is_newer_than_the_start_folder()
    {
        WriteRunningTask();
        _run.Touch("logs/t/20261003-120000/attempt-1-worker.json", T0);
        _run.Touch("logs/t-integration-setup.log", T0.AddMinutes(1));

        Assert.Equal("integration check", _run.Read().Tasks[0].Detail);
    }

    [Theory]
    [InlineData("t-integration-check.log", -1)]
    [InlineData("t-integration-check.log.stderr", 1)]
    [InlineData("u-integration-check.log", 1)]
    [InlineData("t-other.log", 1)]
    public void Running_ignores_older_and_other_root_logs(string rootFile, int minutes)
    {
        WriteRunningTask();
        _run.Touch("logs/t/20261003-120000/attempt-1-worker.json", T0);
        _run.Touch("logs/" + rootFile, T0.AddMinutes(minutes));

        Assert.Equal("worker (attempt 1) finished", _run.Read().Tasks[0].Detail);
    }

    [Fact]
    public void Running_without_a_start_folder_is_starting()
    {
        WriteRunningTask();
        _run.Touch("logs/other/20261003-120000/setup.log", T0);
        _run.Touch("logs/t-integration-setup.log", T0);

        Assert.Equal("starting", _run.Read().Tasks[0].Detail);
    }

    private void WriteRunningTask()
    {
        _run.WriteTasks(Task("t"), Task("other"));
        _run.WriteState(("t", "running"));
    }
}

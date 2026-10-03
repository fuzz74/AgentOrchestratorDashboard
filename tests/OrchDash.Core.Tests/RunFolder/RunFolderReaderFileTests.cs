using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using Xunit;
using static OrchDash.Core.Tests.RunFolder.TempRunFolder;

namespace OrchDash.Core.Tests.RunFolder;

public sealed class RunFolderReaderFileTests : IDisposable
{
    private readonly TempRunFolder _run = new();

    public void Dispose() => _run.Dispose();

    [Fact]
    public void A_missing_run_folder_gives_an_empty_run_without_problems()
    {
        var data = new RunFolderReader().Read(Path.Combine(_run.RunDir, "missing", ".orchestrator"), Now);

        AssertEmpty(data);
    }

    [Fact]
    public void An_empty_run_folder_gives_an_empty_run_without_problems()
    {
        AssertEmpty(_run.Read());
    }

    [Fact]
    public void Planning_while_there_are_entries_but_no_start()
    {
        _run.Write("progress.md", "2026-10-03 11:00:00  [planner] Read .orchestrator/spec.md\n");

        Assert.Equal(RunPhase.Planning, _run.Read().Run.Phase);
    }

    [Fact]
    public void Running_and_stopping_while_the_lock_is_held_then_interrupted()
    {
        _run.Write("progress.md", Started + ClaudeLine);

        using (_run.Hold("run.lock"))
        {
            var running = _run.Read();
            Assert.Equal(
                new RunInfo(RunPhase.Running, running.Run.StartedAt, null, 2, Provider.Claude, "C:\\Users\\me\\.local\\bin\\claude.exe", false),
                running.Run);
            Assert.Equal(new DateTime(2026, 10, 3, 12, 0, 0), running.Run.StartedAt!.Value.DateTime);
            Assert.Empty(running.Problems);

            _run.Write("stop-requested", "");
            var stopping = _run.Read().Run;
            Assert.Equal((RunPhase.Stopping, true), (stopping.Phase, stopping.StopRequested));
        }

        var interrupted = _run.Read();
        Assert.Equal((RunPhase.Interrupted, true), (interrupted.Run.Phase, interrupted.Run.StopRequested));
        Assert.Empty(interrupted.Problems);
    }

    [Fact]
    public void Interrupted_when_run_lock_is_missing()
    {
        _run.Write("progress.md", Started + ClaudeLine);

        var data = _run.Read();

        Assert.Equal(RunPhase.Interrupted, data.Run.Phase);
        Assert.False(data.Run.StopRequested);
        Assert.Empty(data.Problems);
    }

    [Fact]
    public void Finished_does_not_test_the_lock()
    {
        _run.Write("progress.md", Started + ClaudeLine + Finished);
        _run.Write("stop-requested", "");

        using (_run.Hold("run.lock"))
        {
            var data = _run.Read();

            Assert.Equal((RunPhase.Finished, true), (data.Run.Phase, data.Run.StopRequested));
            Assert.Equal(new DateTime(2026, 10, 3, 12, 30, 0), data.Run.FinishedAt!.Value.DateTime);
            Assert.Empty(data.Problems);
        }
    }

    [Fact]
    public void A_cut_off_tasks_json_gives_the_rest_and_one_problem()
    {
        WriteValidRun();
        _run.Write("tasks.json", """{ "spec": "s", "tasks": [ { "id": "a", "ti""");

        var data = _run.Read();

        Assert.Null(data.Plan);
        Assert.Empty(data.Tasks);
        AssertRestIsRead(data);
        Assert.StartsWith("tasks.json: ", Assert.Single(data.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tasks_json_that_is_not_an_object_gives_one_problem()
    {
        WriteValidRun();
        _run.Write("tasks.json", "[]");

        var data = _run.Read();

        Assert.Null(data.Plan);
        Assert.Empty(data.Tasks);
        Assert.Equal("tasks.json: the content is not a JSON object", Assert.Single(data.Problems));
    }

    [Fact]
    public void A_state_json_that_is_not_json_gives_the_rest_and_one_problem()
    {
        WriteValidRun();
        _run.Write("state.json", "this is not JSON");

        var data = _run.Read();

        AssertTasksWithoutState(data);
        AssertRestIsRead(data);
        Assert.StartsWith("state.json: ", Assert.Single(data.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_state_json_in_use_gives_the_rest_and_one_problem()
    {
        WriteValidRun();

        using (_run.Hold("state.json"))
        {
            var data = _run.Read();

            AssertTasksWithoutState(data);
            AssertRestIsRead(data);
            var problem = Assert.Single(data.Problems);
            Assert.StartsWith("state.json: ", problem, StringComparison.Ordinal);
            Assert.Contains("being used by another process", problem, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_progress_md_in_use_gives_the_rest_and_one_problem()
    {
        WriteValidRun();

        using (_run.Hold("progress.md"))
        {
            var data = _run.Read();

            Assert.Empty(data.Progress);
            Assert.Equal(RunPhase.NotStarted, data.Run.Phase);
            Assert.Equal(TaskState.Done, Assert.Single(data.Tasks).Status);
            Assert.Single(data.Sessions);
            Assert.StartsWith("progress.md: ", Assert.Single(data.Problems), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Missing_files_give_no_problems()
    {
        _run.WriteTasks(Task("a"));

        var data = _run.Read();

        Assert.NotNull(data.Plan);
        AssertTasksWithoutState(data);
        Assert.Empty(data.Progress);
        Assert.Empty(data.Sessions);
        Assert.Empty(data.Problems);
    }

    [Fact]
    public void Sessions_come_from_the_logs_folder()
    {
        _run.Write("logs/planner-20261003-110111-1.json.events.jsonl", "");
        _run.Write("logs/a/20261003-120000/attempt-1-worker.json.prompt.md", "prompt");

        var data = _run.Read();

        Assert.Equal(
            ["a/20261003-120000/attempt-1-worker.json", "planner-20261003-110111-1.json"],
            data.Sessions.Select(s => s.Key));
        Assert.Empty(data.Problems);
    }

    private void WriteValidRun()
    {
        _run.WriteTasks(Task("a"));
        _run.WriteState(("a", "done"));
        _run.Write("progress.md", Started + ClaudeLine + Finished);
        _run.Write("logs/a/20261003-120000/attempt-1-worker.json", "{}");
    }

    private static void AssertRestIsRead(RunFolderData data)
    {
        Assert.Equal(3, data.Progress.Length);
        Assert.Equal(RunPhase.Finished, data.Run.Phase);
        Assert.Equal("a/20261003-120000/attempt-1-worker.json", Assert.Single(data.Sessions).Key);
    }

    private static void AssertTasksWithoutState(RunFolderData data)
    {
        var task = Assert.Single(data.Tasks);
        Assert.Equal((TaskState.Pending, "fresh", 0, "ready"), (task.Status, task.Mode, task.Attempts, task.Detail));
    }

    private static void AssertEmpty(RunFolderData data)
    {
        Assert.Equal(new RunInfo(RunPhase.NotStarted, null, null, null, Provider.Unknown, null, false), data.Run);
        Assert.Null(data.Plan);
        Assert.Empty(data.Tasks);
        Assert.Empty(data.Progress);
        Assert.Empty(data.Sessions);
        Assert.Empty(data.Problems);
    }
}

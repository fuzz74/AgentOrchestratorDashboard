using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using OrchDash.Core.Tests.Fixtures;
using Xunit;

namespace OrchDash.Core.Tests.RunFolder;

public sealed class RunInfoRulesTests
{
    private const string Started = "2026-10-03 11:00:00  Run started: 5 tasks, max 3 in parallel, integration branch orch/integration\n";
    private const string ClaudeLine = "2026-10-03 11:00:00  Claude: C:\\Users\\me\\.local\\bin\\claude.exe\n";
    private const string Finished = "2026-10-03 11:30:00  Run finished: 5 done, 0 failed, 0 blocked, 1,25 USD\n";

    [Fact]
    public void No_entries_is_not_started()
    {
        var run = RunInfoRules.Derive([], stopRequested: false, lockHeld: false);

        Assert.Equal(new RunInfo(RunPhase.NotStarted, null, null, null, Provider.Unknown, null, false), run);
        Assert.False(RunInfoRules.NeedsLockTest([]));
    }

    [Fact]
    public void Default_array_counts_as_no_entries()
    {
        Assert.Equal(RunPhase.NotStarted, RunInfoRules.Derive(default, stopRequested: false, lockHeld: false).Phase);
        Assert.False(RunInfoRules.NeedsLockTest(default));
    }

    [Theory]
    [InlineData("2026-10-03 11:00:00  [planner] tool: view\n")]
    [InlineData("2026-10-03 11:00:00  Plan written: 5 tasks, 0,00 USD\n")]
    [InlineData("2026-10-03 11:00:00  [core] Run started: 5 tasks, max 3 in parallel\n2026-10-03 11:30:00  [core] Run finished\n")]
    public void Entries_without_a_start_are_planning(string text)
    {
        var progress = ProgressParser.Parse(text);

        var run = RunInfoRules.Derive(progress, stopRequested: true, lockHeld: true);

        Assert.Equal(RunPhase.Planning, run.Phase);
        Assert.Null(run.StartedAt);
        Assert.Null(run.FinishedAt);
        Assert.Null(run.MaxParallel);
        Assert.Equal(Provider.Unknown, run.Provider);
        Assert.True(run.StopRequested);
        Assert.False(RunInfoRules.NeedsLockTest(progress));
    }

    [Theory]
    [InlineData(true, false, RunPhase.Running)]
    [InlineData(true, true, RunPhase.Stopping)]
    [InlineData(false, false, RunPhase.Interrupted)]
    [InlineData(false, true, RunPhase.Interrupted)]
    public void A_start_without_finish_depends_on_the_lock_and_stop_request(bool lockHeld, bool stopRequested, RunPhase phase)
    {
        var progress = ProgressParser.Parse(Started + ClaudeLine + "2026-10-03 11:00:01  [core] started (fresh)\n");

        var run = RunInfoRules.Derive(progress, stopRequested, lockHeld);

        Assert.True(RunInfoRules.NeedsLockTest(progress));
        Assert.Equal(phase, run.Phase);
        Assert.Equal(stopRequested, run.StopRequested);
        Assert.Equal(new DateTime(2026, 10, 3, 11, 0, 0), run.StartedAt?.DateTime);
        Assert.Null(run.FinishedAt);
        Assert.Equal(3, run.MaxParallel);
        Assert.Equal(Provider.Claude, run.Provider);
        Assert.Equal(@"C:\Users\me\.local\bin\claude.exe", run.AgentPath);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void A_finish_after_the_start_is_finished(bool lockHeld, bool stopRequested)
    {
        var progress = ProgressParser.Parse(Started + ClaudeLine + "2026-10-03 11:10:00  [core] DONE\n" + Finished);

        var run = RunInfoRules.Derive(progress, stopRequested, lockHeld);

        Assert.False(RunInfoRules.NeedsLockTest(progress));
        Assert.Equal(RunPhase.Finished, run.Phase);
        Assert.Equal(new DateTime(2026, 10, 3, 11, 30, 0), run.FinishedAt?.DateTime);
    }

    [Fact]
    public void The_first_finish_after_the_start_wins()
    {
        var progress = ProgressParser.Parse(Started + Finished + "2026-10-03 11:40:00  Run finished: again\n");

        Assert.Equal(new DateTime(2026, 10, 3, 11, 30, 0), RunInfoRules.Derive(progress, false, false).FinishedAt?.DateTime);
    }

    [Fact]
    public void The_latest_of_three_starts_wins()
    {
        const string text =
            "2026-10-03 09:00:00  Run started: 5 tasks, max 1 in parallel\n" +
            "2026-10-03 09:00:00  Claude: C:\\old\\claude.exe\n" +
            "2026-10-03 10:00:00  Run started: 5 tasks, max 2 in parallel\n" +
            "2026-10-03 10:00:00  Claude: C:\\older\\claude.exe\n" +
            "2026-10-03 12:00:00  Run started: 5 tasks, max 4 in parallel\n" +
            "2026-10-03 12:00:00  Copilot: C:\\new\\copilot.exe\n" +
            "2026-10-03 12:00:01  [core] started (resume)\n";
        var progress = ProgressParser.Parse(text);

        var run = RunInfoRules.Derive(progress, stopRequested: false, lockHeld: true);

        Assert.Equal(RunPhase.Running, run.Phase);
        Assert.Equal(new DateTime(2026, 10, 3, 12, 0, 0), run.StartedAt?.DateTime);
        Assert.Equal(4, run.MaxParallel);
        Assert.Equal(Provider.Copilot, run.Provider);
        Assert.Equal(@"C:\new\copilot.exe", run.AgentPath);
    }

    [Fact]
    public void A_finish_of_an_earlier_start_does_not_count()
    {
        const string text =
            "2026-10-03 09:00:00  Run started: 5 tasks, max 3 in parallel\n" +
            "2026-10-03 09:30:00  Run finished: 2 done, 0 failed, 0 blocked, 0,10 USD\n" +
            "2026-10-03 10:00:00  Run started: 5 tasks, max 3 in parallel\n";
        var progress = ProgressParser.Parse(text);

        Assert.True(RunInfoRules.NeedsLockTest(progress));
        var run = RunInfoRules.Derive(progress, stopRequested: false, lockHeld: false);
        Assert.Equal(RunPhase.Interrupted, run.Phase);
        Assert.Equal(new DateTime(2026, 10, 3, 10, 0, 0), run.StartedAt?.DateTime);
        Assert.Null(run.FinishedAt);
    }

    [Fact]
    public void Entries_with_a_source_are_ignored()
    {
        const string text =
            Started +
            "2026-10-03 11:00:00  [core] Claude: C:\\fake.exe\n" +
            "2026-10-03 11:00:01  Copilot: C:\\real\\copilot.exe\n" +
            "2026-10-03 11:20:00  [core] Run finished\n";
        var progress = ProgressParser.Parse(text);

        var run = RunInfoRules.Derive(progress, stopRequested: false, lockHeld: true);

        Assert.Equal(RunPhase.Running, run.Phase);
        Assert.Equal(Provider.Copilot, run.Provider);
        Assert.Equal(@"C:\real\copilot.exe", run.AgentPath);
        Assert.True(RunInfoRules.NeedsLockTest(progress));
    }

    [Theory]
    [InlineData("2026-10-03 11:00:01  [core] started (fresh)\n")]
    [InlineData("2026-10-03 11:00:01  Plan written: 5 tasks\n")]
    [InlineData("2026-10-03 11:00:01  Gemini: C:\\gemini.exe\n")]
    [InlineData("")]
    public void Without_a_provider_line_the_provider_is_unknown(string next)
    {
        var run = RunInfoRules.Derive(ProgressParser.Parse(Started + next), stopRequested: false, lockHeld: true);

        Assert.Equal(Provider.Unknown, run.Provider);
        Assert.Null(run.AgentPath);
    }

    [Theory]
    [InlineData("Run started: 5 tasks, max 12 in parallel, integration branch x", 12)]
    [InlineData("Run started: 5 tasks", null)]
    [InlineData("Run started: 5 tasks, max many in parallel", null)]
    [InlineData("Run started: 5 tasks, max 99999999999 in parallel", null)]
    public void Reads_max_parallel_from_the_start_entry(string message, int? maxParallel)
    {
        var run = RunInfoRules.Derive(ProgressParser.Parse("2026-10-03 11:00:00  " + message), stopRequested: false, lockHeld: true);

        Assert.Equal(maxParallel, run.MaxParallel);
    }

    [Fact]
    public void Run_started_needs_the_colon_and_space()
    {
        var progress = ProgressParser.Parse("2026-10-03 11:00:00  Run started\n");

        Assert.Equal(RunPhase.Planning, RunInfoRules.Derive(progress, stopRequested: false, lockHeld: true).Phase);
    }

    [Fact]
    public void Derives_the_claude_fixture()
    {
        var progress = ProgressParser.Parse(File.ReadAllText(Path.Combine(FixturePaths.ClaudeRunDir, "progress.md")));

        var run = RunInfoRules.Derive(progress, stopRequested: false, lockHeld: false);

        Assert.False(RunInfoRules.NeedsLockTest(progress));
        Assert.Equal(RunPhase.Finished, run.Phase);
        Assert.Equal(new DateTime(2026, 10, 1, 10, 42, 1), run.StartedAt?.DateTime);
        Assert.Equal(new DateTime(2026, 10, 1, 11, 19, 15), run.FinishedAt?.DateTime);
        Assert.Equal(10, run.MaxParallel);
        Assert.Equal(Provider.Claude, run.Provider);
        Assert.Equal(@"C:\Users\user1\.local\bin\claude.exe", run.AgentPath);
        Assert.False(run.StopRequested);
    }

    [Fact]
    public void Derives_the_copilot_fixture()
    {
        var progress = ProgressParser.Parse(File.ReadAllText(Path.Combine(FixturePaths.CopilotRunDir, "progress.md")));

        var run = RunInfoRules.Derive(progress, stopRequested: false, lockHeld: false);

        Assert.Equal(3, progress.Count(entry => entry.Source is null && entry.Message.StartsWith("Run started: ", StringComparison.Ordinal)));
        Assert.False(RunInfoRules.NeedsLockTest(progress));
        Assert.Equal(RunPhase.Finished, run.Phase);
        Assert.Equal(new DateTime(2026, 10, 3, 11, 49, 55), run.StartedAt?.DateTime);
        Assert.Equal(new DateTime(2026, 10, 3, 11, 53, 38), run.FinishedAt?.DateTime);
        Assert.Equal(3, run.MaxParallel);
        Assert.Equal(Provider.Copilot, run.Provider);
        Assert.Equal(
            @"C:\Users\user1\AppData\Local\Microsoft\WinGet\Packages\GitHub.Copilot_Microsoft.Winget.Source_8wekyb3d8bbwe\copilot.exe",
            run.AgentPath);
    }
}

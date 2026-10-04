using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Overview.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.Overview.Format;

public sealed class OverviewTextTests
{
    private readonly RunSnapshot _run = SampleRun.Create();
    private readonly DateTimeOffset _now = SampleRun.At(12, 30, 0);

    private TaskView Task(string id) => _run.Tasks.Single(t => t.Id == id);

    private Session Session(string key) => _run.Sessions.Single(s => s.Files.Key == key);

    [Fact]
    public void Run_panel_shows_every_run_value()
    {
        Assert.Equal(
        [
            "Phase: Running",
            "Started: 12:00:00",
            "Elapsed: 30m00s",
            "Provider: Claude",
            "Max parallel: 2",
            "Tasks: [muted]· 1 pending[/]  [primary]▶ 1 running[/]  [success]✔ 1 done[/]  [error]✖ 1 failed[/]  [warning]⊘ 1 blocked[/]",
            "Cost: 0.87 USD",
        ], OverviewText.RunPanel(_run, _now));
    }

    [Fact]
    public void Run_panel_counts_the_tasks_per_status()
    {
        var run = _run with { Tasks = [Task("alpha"), Task("alpha") with { Id = "alpha2" }, Task("gamma")] };

        Assert.Contains(
            "Tasks: [muted]· 0 pending[/]  [primary]▶ 0 running[/]  [success]✔ 2 done[/]  [error]✖ 1 failed[/]  [warning]⊘ 0 blocked[/]",
            OverviewText.RunPanel(run, _now));
        Assert.Equal(2.0 / 3, OverviewText.Progress(run), 9);
    }

    [Fact]
    public void Progress_is_done_over_total()
    {
        Assert.Equal(0.2, OverviewText.Progress(_run), 9);
    }

    [Fact]
    public void Run_panel_of_a_finished_run_stops_the_elapsed_time()
    {
        var run = _run with { Run = _run.Run with { Phase = RunPhase.Finished, FinishedAt = SampleRun.At(12, 25, 7) } };

        Assert.Contains("Elapsed: 25m07s", OverviewText.RunPanel(run, _now));
    }

    [Fact]
    public void Run_panel_shows_stop_requested_in_the_warning_colour_only_while_requested()
    {
        var stopping = _run with { Run = _run.Run with { Phase = RunPhase.Stopping, StopRequested = true } };

        Assert.Equal("[warning]stop requested[/]", OverviewText.RunPanel(stopping, _now)[^1]);
        Assert.DoesNotContain(OverviewText.RunPanel(_run, _now), line => line.Contains("stop requested", StringComparison.Ordinal));
    }

    [Fact]
    public void Snapshot_without_plan_and_tasks_shows_missing_values_as_dashes()
    {
        var empty = RunSnapshot.Empty(SampleRun.RepoPath);

        Assert.Equal(
        [
            "Phase: NotStarted",
            "Started: -",
            "Elapsed: -",
            "Provider: -",
            "Max parallel: -",
            "Tasks: [muted]· 0 pending[/]  [primary]▶ 0 running[/]  [success]✔ 0 done[/]  [error]✖ 0 failed[/]  [warning]⊘ 0 blocked[/]",
            "Cost: 0.00 USD",
        ], OverviewText.RunPanel(empty, _now));
        Assert.Equal(0, OverviewText.Progress(empty));

        var section = Assert.Single(OverviewText.RunPopup(empty));
        Assert.Equal("Run", section.Heading);
        Assert.Equal(
            "phase: NotStarted\nstarted: -\nfinished: -\nmax parallel: -\nprovider: -\nagent path: -\nstop requested: false",
            section.Text);
    }

    [Fact]
    public void Run_popup_lists_every_run_and_plan_value()
    {
        var sections = OverviewText.RunPopup(_run);

        Assert.Equal(["Run", "Plan", "Settings"], sections.Select(s => s.Heading));
        Assert.All(sections, s => Assert.Equal(TextKind.Plain, s.Kind));
        Assert.Equal(
            "phase: Running\nstarted: 12:00:00\nfinished: -\nmax parallel: 2\nprovider: Claude\n" +
            @"agent path: C:\Users\sample\.local\bin\claude.exe" + "\nstop requested: false",
            sections[0].Text);
        Assert.Equal("spec: .orchestrator/spec.md\nbase branch: main\nintegration branch: orch/integration", sections[1].Text);
        Assert.Equal("maxAttempts: 3\nmodel: \"sonnet\"\nreview: true", sections[2].Text);
    }

    [Fact]
    public void Task_cells_are_plain_values()
    {
        Assert.Equal(
            ["✔", "alpha", "W1", "Alpha parser", "0.25 USD, 1 attempt(s)", "1", "0.25 USD", "9m25s"],
            OverviewText.TaskCells(Task("alpha"), _now));
        Assert.Equal("20m00s", OverviewText.TaskCells(Task("beta"), _now)[7]);
        Assert.Equal("", OverviewText.TaskCells(Task("epsilon"), _now)[7]);
    }

    [Fact]
    public void Task_row_aligns_the_columns_in_the_status_colour()
    {
        Assert.Equal(
            "[success]✔  alpha    W1    Alpha parser      0.25 USD, 1 attempt(s)      1    0.25 USD    9m25s[/]",
            OverviewText.TaskRow(Task("alpha"), _now, 7, 16, 24));
        Assert.Equal(
            "[primary]▶  beta     W2    Beta checker      worker (attempt 1)          1    0.00 USD   20m00s[/]",
            OverviewText.TaskRow(Task("beta"), _now, 7, 16, 24));
    }

    [Fact]
    public void Task_row_cuts_long_cells_with_dots_and_leaves_elapsed_empty_before_the_start()
    {
        Assert.Equal(
            "[muted]·  epsilon  W3    Epsilon comma...  waiting for beta            0    0.00 USD[/]",
            OverviewText.TaskRow(Task("epsilon"), _now, 7, 16, 24));
        Assert.Contains("acceptance failed...  ", OverviewText.TaskRow(Task("gamma"), _now, 7, 16, 20), StringComparison.Ordinal);
    }

    [Fact]
    public void Task_header_lines_up_with_the_rows()
    {
        Assert.Equal(
            "[muted]   Id       Wave  Title             Detail                    Att        Cost  Elapsed[/]",
            OverviewText.TaskHeader(7, 16, 24));
    }

    [Fact]
    public void Task_widths_fit_every_cell_when_the_row_is_wide_enough()
    {
        // Longest id 7 ("epsilon"), title 20 ("Epsilon command line"), detail 33 (the gamma error).
        var widths = OverviewText.TaskWidths(_run.Tasks, OverviewText.FixedRowWidth + 7 + 20 + 33 + 10);

        Assert.Equal((7, 20, 33), widths);
    }

    [Fact]
    public void Task_widths_give_the_detail_room_first_and_the_title_the_rest()
    {
        var width = OverviewText.FixedRowWidth + 7 + 45;

        Assert.Equal((7, 12, 33), OverviewText.TaskWidths(_run.Tasks, width));
        Assert.Equal((7, 12, 28), OverviewText.TaskWidths(_run.Tasks, width - 5));
        // The header has every column at its full width: "[muted]" + header + "[/]".
        Assert.Equal(width, OverviewText.TaskHeader(7, 12, 33).Length - "[muted][/]".Length);
    }

    [Fact]
    public void Task_widths_without_tasks_fit_the_header()
    {
        Assert.Equal((2, 5, 6), OverviewText.TaskWidths([], 200));
        Assert.Equal((2, 0, 0), OverviewText.TaskWidths([], 10));
    }

    [Fact]
    public void Task_popup_has_the_sections_in_order()
    {
        var full = Task("alpha") with { Error = "boom", Feedback = "fix it" };

        Assert.Equal(
            ["Status", "Plan", "Prompt", "Summary", "Notes", "Error", "Feedback", "Sessions"],
            OverviewText.TaskPopup(full, _run).Select(s => s.Heading));
    }

    [Fact]
    public void Task_popup_shows_every_state_and_plan_field_and_the_sessions()
    {
        var sections = OverviewText.TaskPopup(Task("alpha"), _run);

        Assert.Equal(["Status", "Plan", "Prompt", "Summary", "Notes", "Sessions"], sections.Select(s => s.Heading));
        Assert.Equal(
            "status: Done\nmode: fresh\nattempts: 1\nsync runs: 0\nspec rejections: 0\ncost: 0.25 USD\n" +
            "session id: 0b7f3c2a-5d41-4e8a-9c11-2f6a8d3e4b70\nstarted: 12:00:05\nfinished: 12:09:30\n" +
            "merged sha: 3f9c2e1\ndetail: 0.25 USD, 1 attempt(s)",
            sections[0].Text);
        Assert.Equal(
            "deps: -\nowns: src/Alpha/**, tests/Alpha/**\nacceptance: dotnet test tests/Alpha\nmodel: -\nwave: 1\ndependents: 2",
            sections[1].Text);
        Assert.Equal(Task("alpha").Prompt, sections[2].Text);
        Assert.Equal("Added the alpha parser and its tests.", sections[3].Text);
        Assert.Equal("Worker · #1 · Succeeded · claude-sonnet-4-5\nReviewer · #1.1 · Succeeded · gpt-5.1", sections[5].Text);
    }

    [Fact]
    public void Task_popup_leaves_out_empty_sections()
    {
        Assert.Equal(
            ["Status", "Plan", "Prompt", "Error", "Feedback"],
            OverviewText.TaskPopup(Task("gamma"), _run).Select(s => s.Heading));
        Assert.Equal(
            ["Status", "Plan"],
            OverviewText.TaskPopup(Task("delta") with { Prompt = "", Summary = "  " }, _run).Select(s => s.Heading));
        Assert.Contains("acceptance: -\n", OverviewText.TaskPopup(Task("delta"), _run)[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Running_sessions_are_the_sessions_in_state_running()
    {
        Assert.Equal([SampleRun.BetaWorkerKey], OverviewText.RunningSessions(_run).Select(s => s.Files.Key));
    }

    [Fact]
    public void Running_block_shows_a_header_and_the_last_five_items()
    {
        Assert.Equal(
        [
            "beta · [cyan]Worker[/] · #1 · claude-sonnet-4-5 · 3 tool calls · 30s ago",
            "  context 34.5k of 200.0k (17 %)",
            "  Let me read the alpha parser first.",
            "  Read src/Alpha/Parser.cs",
            "  Grep Parse\\( in src",
            "  Now I will write the checker and build it.",
            "  Bash dotnet build src/Beta",
        ], OverviewText.RunningBlock(Session(SampleRun.BetaWorkerKey), _run, _now));
    }

    [Fact]
    public void Running_block_shows_thinking_without_text_as_its_token_estimate()
    {
        Assert.Equal(
        [
            "alpha · [cyan]Worker[/] · #1 · claude-sonnet-4-5 · 2 tool calls · 10s ago",
            "  context 43.3k of 200.0k (22 %)",
            "  I will replace the Parser stub in src/Alpha/Parser.cs.",
            "  Edit src/Alpha/Parser.cs",
            "  [muted]thinking (~1.2k tokens, no text)[/]",
            "  Bash dotnet test tests/Alpha",
            "  The parser is in place and all alpha tests pass.",
        ], OverviewText.RunningBlock(Session(SampleRun.AlphaWorkerKey), _run, SampleRun.At(12, 6, 0)));
    }

    [Fact]
    public void Running_block_header_turns_warning_after_more_than_five_minutes()
    {
        var beta = Session(SampleRun.BetaWorkerKey);

        Assert.Equal(
            "beta · [cyan]Worker[/] · #1 · claude-sonnet-4-5 · 3 tool calls · 5m00s ago",
            OverviewText.RunningBlock(beta, _run, SampleRun.At(12, 34, 30))[0]);
        Assert.Equal(
            "[warning]beta · Worker · #1 · claude-sonnet-4-5 · 3 tool calls · 5m01s ago[/]",
            OverviewText.RunningBlock(beta, _run, SampleRun.At(12, 34, 31))[0]);
    }

    [Fact]
    public void Running_block_without_task_id_shows_the_role_and_short_item_lists_whole()
    {
        var beta = Session(SampleRun.BetaWorkerKey);
        var planner = beta with
        {
            Files = beta.Files with { TaskId = null, Role = AgentRole.Planner },
            Content = beta.Content with { Items = [.. beta.Content.Items.Take(2)], LastEventAt = null },
        };

        Assert.Equal(
        [
            "Planner · [blue]Planner[/] · #1 · claude-sonnet-4-5 · 0 tool calls · -",
            "  context 34.5k of 200.0k (17 %)",
            "  [muted]Beta builds on the alpha parser.[/]",
            "  Let me read the alpha parser first.",
        ], OverviewText.RunningBlock(planner, _run, _now));
    }

    [Fact]
    public void Running_block_takes_the_context_of_the_latest_call_with_usage()
    {
        var beta = Session(SampleRun.BetaWorkerKey);
        var calls = beta.Content.Calls;
        var session = beta with
        {
            Content = beta.Content with { Calls = calls.Add(new ModelCall("msg_03beta", "claude-sonnet-4-5", SampleRun.At(12, 25, 0), null)) },
        };

        Assert.Equal("  context 34.5k of 200.0k (17 %)", OverviewText.RunningBlock(session, _run, _now)[1]);
    }

    [Fact]
    public void Running_block_has_no_context_line_without_a_call_with_usage()
    {
        var beta = Session(SampleRun.BetaWorkerKey);
        var withoutUsage = beta with
        {
            Content = beta.Content with { Calls = [.. beta.Content.Calls.Select(c => c with { Usage = null })] },
        };
        var withoutCalls = beta with { Content = beta.Content with { Calls = [] } };

        Assert.All([withoutUsage, withoutCalls], session =>
        {
            var lines = OverviewText.RunningBlock(session, _run, _now);
            Assert.Equal(6, lines.Count);
            Assert.Equal("  Let me read the alpha parser first.", lines[1]);
        });
    }

    [Fact]
    public void Running_block_without_a_context_limit_shows_the_context_alone()
    {
        var beta = Session(SampleRun.BetaWorkerKey);

        Assert.Equal("  context 34.5k", OverviewText.RunningBlock(beta, _run with { Sessions = [beta] }, _now)[1]);
    }

    [Fact]
    public void Running_block_uses_the_sessions_own_context_window()
    {
        var beta = Session(SampleRun.BetaWorkerKey);
        var result = Session(SampleRun.AlphaWorkerKey).Content.Result! with { ContextWindow = 1_000_000 };
        var session = beta with { Content = beta.Content with { Result = result } };

        Assert.Equal("  context 34.5k of 1.0M (3 %)", OverviewText.RunningBlock(session, _run, _now)[1]);
    }

    [Theory]
    [InlineData(0, "12:00:00 Run started: 5 tasks, max 2 in parallel")]
    [InlineData(1, "[success]12:09:30 [[alpha]] DONE in 9m25s, 0.25 USD[/]")]
    [InlineData(2, "[error]12:20:00 [[gamma]] FAILED after 3 attempts: acceptance failed[/]")]
    public void Log_line_of_the_sample_entries(int index, string expected)
    {
        Assert.Equal(expected, OverviewText.LogLine(_run.Progress[index]));
    }

    [Fact]
    public void Log_line_of_activity_and_warning()
    {
        Assert.Equal(
            "[muted]12:01:00 [[planner]] Read .orchestrator/spec.md[/]",
            OverviewText.LogLine(new ProgressEntry(SampleRun.At(12, 1, 0), "planner", "Read .orchestrator/spec.md", ProgressKind.Activity)));
        Assert.Equal(
            "[warning]12:02:00 [[beta]] merge conflict in src/Beta/Checker.cs[/]",
            OverviewText.LogLine(new ProgressEntry(SampleRun.At(12, 2, 0), "beta", "merge conflict in src/Beta/Checker.cs\r\nsecond", ProgressKind.Warning)));
    }

    [Fact]
    public void Log_popup_has_one_section_with_the_whole_message()
    {
        var section = Assert.Single(OverviewText.LogPopup(_run.Progress[1]));

        Assert.Equal("12:09:30 · alpha · Success", section.Heading);
        Assert.Equal("DONE in 9m25s, 0.25 USD\nmerged as 3f9c2e1", section.Text);
        Assert.Equal("12:00:00 · Info", Assert.Single(OverviewText.LogPopup(_run.Progress[0])).Heading);
    }

    [Fact]
    public void Model_text_with_brackets_comes_out_escaped()
    {
        var task = Task("alpha") with { Id = "a[1]", Title = "[bold]Alpha[/]", Detail = "see [log]" };
        var beta = Session(SampleRun.BetaWorkerKey);
        var session = beta with
        {
            Files = beta.Files with { TaskId = "t[x]" },
            Content = beta.Content with { Model = "m[1]", Items = [new AssistantText(null, null, "array[0] is [red]")] },
        };
        var run = _run with { Run = _run.Run with { AgentPath = "[x]" }, Tasks = [task] };

        Assert.Equal(
            "[success]✔  a[[1]]  W1    [[bold]]Alp...  see [[log]]    1    0.25 USD    9m25s[/]",
            OverviewText.TaskRow(task, _now, 4, 12, 9));
        Assert.Equal(
        [
            "t[[x]] · [cyan]Worker[/] · #1 · m[[1]] · 0 tool calls · 30s ago",
            "  context 34.5k",
            "  array[[0]] is [[red]]",
        ], OverviewText.RunningBlock(session, run, _now));
        Assert.Equal("12:00:00 [[x]] y", OverviewText.LogLine(new ProgressEntry(SampleRun.At(12, 0, 0), "x", "y", ProgressKind.Info)));
        Assert.Equal("[warning]12:00:00 [[a]] [[b]][/]",
            OverviewText.LogLine(new ProgressEntry(SampleRun.At(12, 0, 0), "a", "[b]", ProgressKind.Warning)));
        Assert.Equal("Phase: Running", OverviewText.RunPanel(run, _now)[0]);
        Assert.Contains("agent path: [x]", OverviewText.RunPopup(run)[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Popup_text_is_plain()
    {
        var task = Task("alpha") with { Summary = "keeps [brackets]" };

        Assert.Equal("keeps [brackets]", OverviewText.TaskPopup(task, _run).Single(s => s.Heading == "Summary").Text);
    }
}

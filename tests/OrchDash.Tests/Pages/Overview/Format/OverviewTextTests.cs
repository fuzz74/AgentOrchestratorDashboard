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
    private readonly RunSnapshot _subs = SampleRun.CreateSubAgents();

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

    private static AgentProcess Process(int pid, string? taskId, AgentRole? role, DateTimeOffset? startedAt, double? cpuShare = 0.5) =>
        new(pid, "claude.exe", $"claude -p --name orch:{taskId}", startedAt, 2_500_000, cpuShare, taskId, role, null);

    private RunSnapshot WithProcesses(params AgentProcess[] processes) =>
        _run with { Processes = new ProcessInfo(_now, [.. processes], null) };

    [Fact]
    public void Running_block_shows_the_process_line_after_the_context_line()
    {
        var run = SampleRun.CreateInsight();
        var beta = run.Sessions.Single(s => s.Files.Key == SampleRun.BetaWorkerKey);

        var lines = OverviewText.RunningBlock(beta, run, _now);

        Assert.Equal(
        [
            "beta · [cyan]Worker[/] · #1 · claude-sonnet-4-5 · 3 tool calls · 30s ago",
            "  context 34.5k of 200.0k (17 %)",
            "  pid 4242 · up 20m00s · cpu 12 % · mem 367.0 MB",
            "  Let me read the alpha parser first.",
            "  Read src/Alpha/Parser.cs",
            "  Grep Parse\\( in src",
            "  Now I will write the checker and build it.",
            "  Bash dotnet build src/Beta",
        ], lines);
    }

    [Fact]
    public void Running_block_has_no_process_line_without_a_sample()
    {
        var run = SampleRun.CreateInsight() with { Processes = ProcessInfo.Empty };
        var beta = run.Sessions.Single(s => s.Files.Key == SampleRun.BetaWorkerKey);

        var lines = OverviewText.RunningBlock(beta, run, _now);

        Assert.Equal(7, lines.Count);
        Assert.Equal("  Let me read the alpha parser first.", lines[2]);
    }

    [Fact]
    public void Running_block_says_no_process_when_the_sample_has_no_match()
    {
        var run = WithProcesses(
            Process(1, "alpha", AgentRole.Worker, SampleRun.At(12, 0, 0)),
            Process(2, "beta", AgentRole.Reviewer, SampleRun.At(12, 0, 0)));

        Assert.Equal("  [warning]no process[/]", OverviewText.RunningBlock(Session(SampleRun.BetaWorkerKey), run, _now)[2]);
        Assert.Equal("  [warning]no process[/]",
            OverviewText.RunningBlock(Session(SampleRun.BetaWorkerKey), WithProcesses(), _now)[2]);
    }

    [Fact]
    public void Running_block_takes_the_latest_started_of_several_matching_processes()
    {
        var run = WithProcesses(
            Process(1, "beta", AgentRole.Worker, null),
            Process(2, "beta", AgentRole.Worker, SampleRun.At(12, 20, 0)),
            Process(3, "beta", AgentRole.Worker, SampleRun.At(12, 25, 0)),
            Process(4, "beta", AgentRole.Worker, SampleRun.At(12, 10, 0)));

        Assert.Equal(
            "  pid 3 · up 5m00s · cpu 50 % · mem 2.5 MB",
            OverviewText.RunningBlock(Session(SampleRun.BetaWorkerKey), run, _now)[2]);
    }

    [Fact]
    public void Running_block_shows_dashes_for_an_unknown_start_and_cpu_share()
    {
        var run = WithProcesses(Process(7, "beta", AgentRole.Worker, null, cpuShare: null));

        Assert.Equal(
            "  pid 7 · up - · cpu - · mem 2.5 MB",
            OverviewText.RunningBlock(Session(SampleRun.BetaWorkerKey), run, _now)[2]);
    }

    [Fact]
    public void Running_block_of_a_planner_matches_the_process_without_task_id()
    {
        var beta = Session(SampleRun.BetaWorkerKey);
        var planner = beta with { Files = beta.Files with { TaskId = null, Role = AgentRole.Planner } };
        var run = WithProcesses(
            Process(1, "beta", AgentRole.Planner, SampleRun.At(12, 0, 0)),
            Process(2, null, AgentRole.Bootstrap, SampleRun.At(12, 0, 0)),
            Process(3, null, AgentRole.Planner, SampleRun.At(11, 59, 0)));

        Assert.Equal("  pid 3 · up 31m00s · cpu 50 % · mem 2.5 MB", OverviewText.RunningBlock(planner, run, _now)[2]);
    }

    [Fact]
    public void Task_popup_lists_the_tasks_processes_after_the_sessions()
    {
        var run = SampleRun.CreateInsight();
        var beta = run.Tasks.Single(t => t.Id == "beta");
        var alpha = run.Tasks.Single(t => t.Id == "alpha");

        var sections = OverviewText.TaskPopup(beta, run);

        Assert.Equal(["Status", "Plan", "Prompt", "Sessions", "Processes"], sections.Select(s => s.Heading));
        Assert.Equal(
            "pid 4242 · worker · started 12:10:00 · cpu 12 % · mem 367.0 MB\n" +
            "claude -p --output-format stream-json --verbose --name orch:beta",
            sections[^1].Text);
        Assert.DoesNotContain(OverviewText.TaskPopup(alpha, run), s => s.Heading == "Processes");
    }

    [Fact]
    public void Task_popup_shows_dashes_for_unknown_process_values_and_keeps_brackets_plain()
    {
        var run = WithProcesses(
            Process(1, "beta", null, null, cpuShare: null) with { CommandLine = "" },
            Process(2, "alpha", AgentRole.Worker, null),
            Process(3, "beta", AgentRole.Reviewer, SampleRun.At(12, 20, 0)) with { CommandLine = "claude [x] --name orch:beta:review" });

        var section = OverviewText.TaskPopup(Task("beta"), run).Single(s => s.Heading == "Processes");

        Assert.Equal(
            "pid 1 · - · started - · cpu - · mem 2.5 MB\n-\n" +
            "pid 3 · reviewer · started 12:20:00 · cpu 50 % · mem 2.5 MB\nclaude [x] --name orch:beta:review",
            section.Text);
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

    private Session SubSession(string key) => _subs.Sessions.Single(s => s.Files.Key == key);

    private AgentRow Child(string sessionKey, string agentId) =>
        AgentTree.Rows([SubSession(sessionKey)]).Single(row => row.SubAgent.Id == agentId);

    private RunSnapshot WithSession(Session session) =>
        _subs with { Sessions = [.. _subs.Sessions.Select(s => s.Files.Key == session.Files.Key ? session : s)] };

    [Fact]
    public void Task_rows_follow_each_task_with_the_child_rows_of_its_sessions()
    {
        var rows = OverviewText.TaskRows(_subs);

        Assert.Equal(
        [
            "alpha", $"{SampleRun.AlphaWorkerKey}|{SampleRun.AlphaSub1Id}", $"{SampleRun.AlphaWorkerKey}|{SampleRun.AlphaSub2Id}",
            "gamma",
            "beta", $"{SampleRun.BetaWorkerKey}|{SampleRun.BetaSub1Id}",
            "delta",
            "epsilon",
        ], rows.Select(r => r.Key));
        Assert.Equal(["├", "└", "└"], rows.Select(r => r.Child?.Prefix).OfType<string>());
        Assert.All(rows, r => Assert.Equal(r.Task.Id, r.Child?.Session.Files.TaskId ?? r.Task.Id));
    }

    [Fact]
    public void Task_rows_without_sub_agents_are_the_tasks_alone()
    {
        var rows = OverviewText.TaskRows(_run);

        Assert.Equal(["alpha", "gamma", "beta", "delta", "epsilon"], rows.Select(r => r.Key));
        Assert.All(rows, r => Assert.Null(r.Child));
    }

    [Fact]
    public void Task_rows_make_the_sub_agents_of_all_the_tasks_sessions_siblings()
    {
        var review = SubSession(SampleRun.AlphaReviewKey);
        var sub = new SubAgent("toolu_review_sub", null, "toolu_review_sub", "Check the tests", null, "Explore", null,
            Background: false, Prompt: "", StartedAt: null, FinishedAt: null, SessionState.Succeeded, Report: null);
        var run = WithSession(review with { Content = review.Content with { SubAgents = [sub] } });

        var children = OverviewText.TaskRows(run).Where(r => r.Task.Id == "alpha").Select(r => r.Child).OfType<AgentRow>().ToArray();

        Assert.Equal(["├", "├", "└"], children.Select(c => c.Prefix));
        Assert.Equal("[success]    └✔ Check the tests · reviewer #1.1 · 0 tool calls · -[/]", OverviewText.ChildRow(children[2], _now, 100));
    }

    [Fact]
    public void Child_row_starts_at_the_id_column_in_the_colour_of_its_state()
    {
        var alpha = OverviewText.TaskRow(_subs.Tasks.Single(t => t.Id == "alpha"), _now, 7, 16, 24);
        var child = OverviewText.ChildRow(Child(SampleRun.AlphaWorkerKey, SampleRun.AlphaSub1Id), _now, 100);

        Assert.Equal("[success]    ├✔ Survey the parser module · worker #1 · 2 tool calls · 41s[/]", child);
        Assert.Equal(alpha.IndexOf("alpha", StringComparison.Ordinal), child.IndexOf(" ├", StringComparison.Ordinal));
        Assert.Equal(
            "[error]    └✖ Check the public API surface of… · worker #1 · 1 tool call · 44s[/]",
            OverviewText.ChildRow(Child(SampleRun.AlphaWorkerKey, SampleRun.AlphaSub2Id), _now, 100));
        Assert.Equal(
            "[primary]    └▶ Survey CLI flags · worker #1 · 1 tool call · 50s[/]",
            OverviewText.ChildRow(Child(SampleRun.BetaWorkerKey, SampleRun.BetaSub1Id), _now, 100));
    }

    [Fact]
    public void Child_row_of_a_running_sub_agent_in_an_ended_session_is_aborted()
    {
        var beta = SubSession(SampleRun.BetaWorkerKey) with { State = SessionState.Failed };

        Assert.Equal(
            "[muted]    └◌ Survey CLI flags · worker #1 · 1 tool call · 50s[/]",
            OverviewText.ChildRow(Assert.Single(AgentTree.Rows([beta])), _now, 100));
    }

    [Fact]
    public void Child_row_counts_the_sub_agents_own_tool_calls_and_keeps_the_nesting()
    {
        // "Map the repo" has the task call that starts "Read the spec" and a glob; the view inside "Read the spec" is not its.
        Assert.Equal(
            "[success]    ├✔ Map the repo · planner #1 · 2 tool calls · 28s[/]",
            OverviewText.ChildRow(Child(SampleRun.PlannerKey, SampleRun.PlannerSub1Id), _now, 100));
        Assert.Equal(
            "[success]    │ └✔ Read the spec · planner #1 · 1 tool call · 14s[/]",
            OverviewText.ChildRow(Child(SampleRun.PlannerKey, SampleRun.PlannerSub2Id), _now, 100));
    }

    [Fact]
    public void Child_row_is_cut_to_the_row_width()
    {
        var row = Child(SampleRun.AlphaWorkerKey, SampleRun.AlphaSub1Id);
        const string whole = "    ├✔ Survey the parser module · worker #1 · 2 tool calls · 41s";

        Assert.Equal($"[success]{whole}[/]", OverviewText.ChildRow(row, _now, whole.Length));
        Assert.Equal("[success]    ├✔ Survey the parser mo...[/]", OverviewText.ChildRow(row, _now, 30));
    }

    [Fact]
    public void Running_block_counts_the_agents_own_events_and_lists_its_sub_agents()
    {
        // The header, the context and the items leave out the sub-agents' calls and items; finished sub-agents show no
        // item, and the haiku model has no known context window.
        Assert.Equal(
        [
            "alpha · [cyan]Worker[/] · #1 · claude-sonnet-4-5 · 4 tool calls · 10s ago",
            "  context 43.3k of 200.0k (22 %)",
            "  Agent Survey the parser module",
            "  [muted]thinking (~1.2k tokens, no text)[/]",
            "  Bash dotnet test tests/Alpha",
            "  Agent Check the public API surface of the alpha parser",
            "  The parser is in place and all alpha tests pass.",
            "[success]├✔ Survey the parser module[/] · 2 tool calls · context 5.4k",
            "[error]└✖ Check the public API surface of…[/] · 1 tool call · context 9.8k of 200.0k (5 %)",
        ], OverviewText.RunningBlock(SubSession(SampleRun.AlphaWorkerKey), _subs, SampleRun.At(12, 6, 0)));
    }

    [Fact]
    public void Running_block_shows_the_latest_item_of_a_running_sub_agent()
    {
        Assert.Equal(
        [
            "beta · [cyan]Worker[/] · #1 · claude-sonnet-4-5 · 4 tool calls · 30s ago",
            "  context 34.5k of 200.0k (17 %)",
            "  Read src/Alpha/Parser.cs",
            "  Grep Parse\\( in src",
            "  Now I will write the checker and build it.",
            "  Agent Survey CLI flags",
            "  Bash dotnet build src/Beta",
            "[primary]└▶ Survey CLI flags[/] · 1 tool call · context 3.6k · Read src/Beta/Program.cs",
        ], OverviewText.RunningBlock(SubSession(SampleRun.BetaWorkerKey), _subs, _now));
    }

    [Fact]
    public void Running_block_takes_the_sub_agents_context_window_from_its_model()
    {
        var alpha = SubSession(SampleRun.AlphaWorkerKey);
        var haiku = alpha with { Content = alpha.Content with { Model = "claude-haiku-4-5" } };
        var run = WithSession(haiku);

        Assert.Equal(
            "[primary]└▶ Survey CLI flags[/] · 1 tool call · context 3.6k of 200.0k (2 %) · Read src/Beta/Program.cs",
            OverviewText.RunningBlock(SubSession(SampleRun.BetaWorkerKey), run, _now)[^1]);
    }

    [Fact]
    public void Running_block_leaves_out_what_a_sub_agent_does_not_have_yet_and_the_item_once_it_ended()
    {
        var beta = SubSession(SampleRun.BetaWorkerKey);
        var started = beta with
        {
            Content = beta.Content with
            {
                Calls = SubAgents.Calls(beta.Content, null),
                Items = SubAgents.Items(beta.Content, null),
            },
        };
        var ended = beta with { State = SessionState.Failed };

        Assert.Equal("[primary]└▶ Survey CLI flags[/] · 0 tool calls", OverviewText.RunningBlock(started, _subs, _now)[^1]);
        Assert.Equal("[muted]└◌ Survey CLI flags[/] · 1 tool call · context 3.6k", OverviewText.RunningBlock(ended, _subs, _now)[^1]);
    }

    [Fact]
    public void Running_block_lists_nested_sub_agents_with_their_prefixes()
    {
        Assert.Equal(
        [
            "Planner · [blue]Planner[/] · #1 · gpt-5.6-luna · 2 tool calls · 2s ago",
            "  context 15.8k",
            "  I will map the repo first, then survey the tests.",
            "  task Map the repo",
            "  task Survey the tests",
            "  The plan has five tasks in three waves: alpha and gamma, then beta and delta, then epsilon.",
            "[success]├✔ Map the repo[/] · 2 tool calls · context 6.8k",
            "[success]│ └✔ Read the spec[/] · 1 tool call · context 5.9k",
            "[success]└✔ Survey the tests[/] · 1 tool call · context 6.1k",
        ], OverviewText.RunningBlock(SubSession(SampleRun.PlannerKey), _subs, SampleRun.At(12, 0, 0)));
    }

    [Fact]
    public void Log_line_and_popup_show_the_sub_agent_after_the_source()
    {
        var entry = _subs.Progress[0];
        var alone = entry with { Source = null };

        Assert.Equal("[muted]11:59:30 [[planner › Map the repo]] Glob **/*[/]", OverviewText.LogLine(entry));
        Assert.Equal("11:59:30 · planner › Map the repo · Activity", Assert.Single(OverviewText.LogPopup(entry)).Heading);
        Assert.Equal("[muted]11:59:30 [[Map the repo]] Glob **/*[/]", OverviewText.LogLine(alone));
        Assert.Equal("11:59:30 · Map the repo · Activity", Assert.Single(OverviewText.LogPopup(alone)).Heading);
    }

    [Fact]
    public void Task_popup_lists_each_sessions_sub_agents_after_its_line()
    {
        Assert.Equal(
            "Worker · #1 · Succeeded · claude-sonnet-4-5\n" +
            "  ├ Survey the parser module · Explore · Succeeded · claude-haiku-4-5\n" +
            "  └ Check the public API surface of… · general-purpose · Failed · claude-sonnet-4-5\n" +
            "Reviewer · #1.1 · Succeeded · gpt-5.1",
            OverviewText.TaskPopup(_subs.Tasks.Single(t => t.Id == "alpha"), _subs).Single(s => s.Heading == "Sessions").Text);
    }

    [Fact]
    public void Task_popup_keeps_the_nesting_and_shows_unknown_values_as_dashes_and_the_state_of_an_ended_session()
    {
        var planner = SubSession(SampleRun.PlannerKey);
        var subs = planner.Content.SubAgents;
        var session = planner with
        {
            Files = planner.Files with { TaskId = "delta" },
            State = SessionState.Failed,
            Content = planner.Content with
            {
                SubAgents = [subs[0], subs[1] with { State = SessionState.Running }, subs[2] with { AgentType = null, Model = null }],
            },
        };

        Assert.Equal(
            "Planner · #1 · Failed · gpt-5.6-luna\n" +
            "  ├ Map the repo · explore · Succeeded · gpt-5.6-luna\n" +
            "  │ └ Read the spec · explore · Aborted · gpt-5.6-luna\n" +
            "  └ Survey the tests · - · Succeeded · -",
            OverviewText.TaskPopup(_subs.Tasks.Single(t => t.Id == "delta"), WithSession(session)).Single(s => s.Heading == "Sessions").Text);
    }

    [Fact]
    public void Sub_agent_text_with_brackets_comes_out_escaped()
    {
        var beta = SubSession(SampleRun.BetaWorkerKey);
        var session = beta with { Content = beta.Content with { SubAgents = [beta.Content.SubAgents[0] with { Name = "flags [x]" }] } };

        Assert.Equal(
            "[primary]    └▶ flags [[x]] · worker #1 · 1 tool call · 50s[/]",
            OverviewText.ChildRow(Assert.Single(AgentTree.Rows([session])), _now, 100));
        Assert.Equal(
            "[primary]└▶ flags [[x]][/] · 1 tool call · context 3.6k · Read src/Beta/Program.cs",
            OverviewText.RunningBlock(session, _subs, _now)[^1]);
        Assert.Equal(
            "12:00:00 [[a › [[x]]]] y",
            OverviewText.LogLine(new ProgressEntry(SampleRun.At(12, 0, 0), "a", "y", ProgressKind.Info) { SubAgent = "[x]" }));
    }
}

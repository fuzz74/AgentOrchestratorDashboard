using System.Text.RegularExpressions;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Overview;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.Pages.Overview;

public sealed class OverviewPageTests
{
    private static readonly string[] TaskHeadings = ["Status", "Plan", "Prompt", "Summary", "Notes", "Error", "Feedback", "Sessions"];

    private static IPage[] Pages() => [new OverviewPage(), new ConversationStubPage()];

    private static string[] Lines(UiTestHost host) => host.Frame().Split('\n');

    private static int RowOf(UiTestHost host, string text)
    {
        var row = Array.FindIndex(Lines(host), line => line.Contains(text, StringComparison.Ordinal));
        Assert.True(row >= 0, $"'{text}' is not on screen:\n{host.Frame()}");
        return row;
    }

    /// <summary>The text lines inside the pop-up, without its borders and scroll bar; empty when no pop-up is open.</summary>
    private static List<string> PopupLines(UiTestHost host)
    {
        var lines = Lines(host);
        var top = Array.FindIndex(lines, line => line.Contains("┌ ", StringComparison.Ordinal) && line.Contains("[X] ┐", StringComparison.Ordinal));
        if (top < 0)
        {
            return [];
        }
        var left = lines[top].IndexOf('┌', StringComparison.Ordinal);
        var right = lines[top].LastIndexOf('┐');
        var text = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > right && lines[row][left] == '│'; row++)
        {
            text.Add(lines[row][(left + 1)..(right - 1)].TrimEnd());
        }
        return text;
    }

    private static string PopupTitleRow(UiTestHost host) =>
        Lines(host).FirstOrDefault(line => line.Contains("[X] ┐", StringComparison.Ordinal)) ?? "";

    private static void AssertTaskPopup(UiTestHost host, string title, params string[] headings)
    {
        Assert.Contains($"┌ {title} ", PopupTitleRow(host), StringComparison.Ordinal);
        var shown = PopupLines(host).Where(line => TaskHeadings.Contains(line)).ToArray();
        Assert.Equal(headings, shown);
    }

    private static RunSnapshot WithLog(int count, long version = 1)
    {
        var entries = Enumerable.Range(1, count)
            .Select(i => new ProgressEntry(SampleRun.At(12, i / 60, i % 60), "alpha", $"entry {i:D2}", ProgressKind.Activity));
        return SampleRun.Create() with { Version = version, Progress = [.. entries] };
    }

    /// <summary>The log lines on screen: the entry numbers, top to bottom.</summary>
    private static string[] VisibleEntries(UiTestHost host) =>
        [.. Regex.Matches(host.Frame(), @"\[alpha\] entry (\d+)").Select(m => m.Groups[1].Value)];

    [Fact]
    public void Frame_shows_the_run_panel_the_tasks_the_running_block_and_the_log()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());
        var frame = host.Frame();

        Assert.Contains("Phase: Running", frame, StringComparison.Ordinal);
        Assert.Contains("Started: 12:00:00", frame, StringComparison.Ordinal);
        Assert.Contains("Elapsed: 30m00s", frame, StringComparison.Ordinal);
        Assert.Contains("Provider: Claude", frame, StringComparison.Ordinal);
        Assert.Contains("Max parallel: 2", frame, StringComparison.Ordinal);
        Assert.Contains("Tasks: · 1 pending  ▶ 1 running  ✔ 1 done  ✖ 1 failed  ⊘ 1 blocked", frame, StringComparison.Ordinal);
        Assert.Contains("Cost: 0.87 USD", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("stop requested", frame, StringComparison.Ordinal);

        string[] rows =
        [
            @"✔  alpha    W1 .*  0\.25 USD, 1 attempt\(s\) ",
            @"✖  gamma    W1 .*  acceptance failed: 2 tests failed ",
            @"▶  beta     W2 .*  worker \(attempt 1\) ",
            @"⊘  delta    W2 .*  a dependency failed ",
            @"·  epsilon  W3 .*  waiting for beta ",
        ];
        var lines = Lines(host);
        var rowIndexes = rows.Select(row => Array.FindIndex(lines, line => Regex.IsMatch(line, row))).ToArray();
        Assert.All(rowIndexes, index => Assert.True(index >= 0, frame));
        Assert.Equal([.. Enumerable.Range(rowIndexes[0], rows.Length)], rowIndexes);

        Assert.Contains("beta · Worker · #1 · claude-sonnet-4-5 · 3 tool calls", frame, StringComparison.Ordinal);
        var block = RowOf(host, "beta · Worker · #1");
        Assert.Contains("Let me read the alpha parser first.", lines[block + 2], StringComparison.Ordinal);
        Assert.Contains("Bash dotnet build src/Beta", lines[block + 6], StringComparison.Ordinal);
        Assert.DoesNotContain("Beta builds on the alpha parser", frame, StringComparison.Ordinal);

        var log = RowOf(host, "12:00:00 Run started: 5 tasks, max 2 in parallel");
        Assert.Contains("12:09:30 [alpha] DONE in 9m25s, 0.25 USD", lines[log + 1], StringComparison.Ordinal);
        Assert.Contains("12:20:00 [gamma] FAILED after 3 attempts: acceptance failed", lines[log + 2], StringComparison.Ordinal);
        Assert.DoesNotContain("merged as", frame, StringComparison.Ordinal);
        host.SaveSvg("overview");
    }

    [Fact]
    public void The_running_block_shows_the_context_size_below_its_header()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.CreateEnriched());
        var lines = Lines(host);

        var block = RowOf(host, "beta · Worker · #1 · claude-sonnet-4-5 · 3 tool calls");
        Assert.Contains("  context 34.5k of 200.0k (17 %)", lines[block + 1], StringComparison.Ordinal);
        Assert.Contains("Let me read the alpha parser first.", lines[block + 2], StringComparison.Ordinal);
        Assert.Contains("Bash dotnet build src/Beta", lines[block + 6], StringComparison.Ordinal);
        host.SaveSvg("overview-context");
    }

    private const string BetaProcessLine = "pid 4242 · up 20m00s · cpu 12 % · mem 367.0 MB";

    [Fact]
    public void The_running_block_shows_the_process_line_below_the_context_size()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.CreateInsight());
        var lines = Lines(host);

        var block = RowOf(host, "beta · Worker · #1 · claude-sonnet-4-5 · 3 tool calls");
        Assert.Contains("  context 34.5k of 200.0k (17 %)", lines[block + 1], StringComparison.Ordinal);
        Assert.Contains("  " + BetaProcessLine, lines[block + 2], StringComparison.Ordinal);
        Assert.Contains("Let me read the alpha parser first.", lines[block + 3], StringComparison.Ordinal);
        host.SaveSvg("overview-process");
    }

    [Fact]
    public void Without_a_process_sample_the_running_block_has_no_process_line()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.CreateInsight() with { Processes = ProcessInfo.Empty });
        var frame = host.Frame();

        Assert.Contains("beta · Worker · #1", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("pid 4242", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("no process", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sample_without_the_sessions_process_shows_no_process()
    {
        var snapshot = SampleRun.CreateInsight();
        snapshot = snapshot with { Processes = snapshot.Processes with { Processes = [] } };
        using var host = UiTestHost.Start(Pages(), snapshot);

        var block = RowOf(host, "beta · Worker · #1");
        Assert.Contains("  no process", Lines(host)[block + 2], StringComparison.Ordinal);
        Assert.DoesNotContain("pid 4242", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_task_popup_lists_the_tasks_processes()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.CreateInsight());

        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Enter);

        Assert.Contains("┌ beta - Beta checker ", PopupTitleRow(host), StringComparison.Ordinal);
        var popup = PopupLines(host);
        var sessions = popup.IndexOf("Sessions");
        var heading = popup.IndexOf("Processes");
        Assert.True(sessions >= 0 && heading > sessions, string.Join('\n', popup));
        Assert.Equal("pid 4242 · worker · started 12:10:00 · cpu 12 % · mem 367.0 MB", popup[heading + 1]);
        Assert.Equal("claude -p --output-format stream-json --verbose --name orch:beta", popup[heading + 2]);
    }

    [Fact]
    public void Without_a_plan_the_task_table_says_No_plan_yet()
    {
        var snapshot = SampleRun.Create() with { Plan = null, Tasks = [] };
        using var host = UiTestHost.Start(Pages(), snapshot);

        Assert.Contains("No plan yet", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("Elapsed  ", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_run_panel_shows_stop_requested()
    {
        var snapshot = SampleRun.Create();
        snapshot = snapshot with { Run = snapshot.Run with { StopRequested = true } };
        using var host = UiTestHost.Start(Pages(), snapshot);

        Assert.Contains("stop requested", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_moves_between_the_panels_and_Enter_on_the_run_panel_opens_the_run_popup()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());
        Assert.Contains("[Enter] Details", Lines(host)[^1], StringComparison.Ordinal);

        host.Press(TerminalKey.Tab);
        Assert.Contains("[Enter] Conversation", Lines(host)[^1], StringComparison.Ordinal);
        host.Press(TerminalKey.Tab);
        Assert.Contains("[End] Follow", Lines(host)[^1], StringComparison.Ordinal);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Enter);

        Assert.Contains("┌ Run ", PopupTitleRow(host), StringComparison.Ordinal);
        var popup = PopupLines(host);
        Assert.Contains("phase: Running", popup);
        Assert.Contains(@"agent path: C:\Users\sample\.local\bin\claude.exe", popup);
        Assert.Contains("integration branch: orch/integration", popup);
        Assert.Contains("model: \"sonnet\"", popup);
        host.SaveSvg("overview-run-popup");

        host.Press(TerminalKey.Escape);
        Assert.Empty(PopupLines(host));
        host.Press(TerminalKey.Tab);
        Assert.Contains("[Enter] Details", Lines(host)[^1], StringComparison.Ordinal);
        Assert.DoesNotContain("[End] Follow", Lines(host)[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_the_run_panel_opens_the_run_popup()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.ClickText("Phase: Running");

        Assert.Contains("┌ Run ", PopupTitleRow(host), StringComparison.Ordinal);
        Assert.Contains("phase: Running", PopupLines(host));
    }

    [Fact]
    public void Enter_on_a_task_row_opens_its_popup_with_the_sections_in_order()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Enter);

        AssertTaskPopup(host, "gamma - Gamma formatter", "Status", "Plan", "Prompt", "Error", "Feedback");
        Assert.Contains("GammaTests.Formats_empty_input: expected \"\" but was null", PopupLines(host));
        host.SaveSvg("overview-task-popup");

        host.Press(TerminalKey.Escape);
        Assert.Empty(PopupLines(host));
        host.Press(TerminalKey.Up);
        host.Press(TerminalKey.Enter);
        AssertTaskPopup(host, "alpha - Alpha parser", "Status", "Plan", "Prompt", "Summary", "Notes", "Sessions");
        Assert.Contains("Reviewer · #1.1 · Succeeded · gpt-5.1", PopupLines(host));
    }

    [Fact]
    public void A_click_on_a_task_row_opens_its_popup()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.ClickText("Delta report");

        AssertTaskPopup(host, "delta - Delta report", "Status", "Plan", "Prompt");
    }

    [Fact]
    public void Enter_on_a_log_entry_opens_the_whole_message()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Up);
        host.Press(TerminalKey.Enter);

        Assert.Contains("┌ Log entry ", PopupTitleRow(host), StringComparison.Ordinal);
        Assert.Equal(["12:09:30 · alpha · Success", "DONE in 9m25s, 0.25 USD", "merged as 3f9c2e1"], PopupLines(host).Take(3));
        host.SaveSvg("overview-log-popup");
    }

    [Fact]
    public void A_click_on_a_log_entry_opens_the_whole_message()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.ClickText("[alpha] DONE");

        Assert.Contains("┌ Log entry ", PopupTitleRow(host), StringComparison.Ordinal);
        Assert.Contains("merged as 3f9c2e1", PopupLines(host));
    }

    [Fact]
    public void Enter_on_a_running_block_shows_its_session_on_the_conversation_page()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Enter);

        Assert.Contains(ConversationStubPage.Prefix + SampleRun.BetaWorkerKey, host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("Phase: Running", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_a_running_block_shows_its_session_on_the_conversation_page()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.ClickText("Bash dotnet build src/Beta");

        Assert.Contains(ConversationStubPage.Prefix + SampleRun.BetaWorkerKey, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_log_follows_the_newest_entry_until_the_user_scrolls_up_and_again_after_End()
    {
        using var host = UiTestHost.Start(Pages(), WithLog(40));
        Assert.Equal("40", VisibleEntries(host)[^1]);
        Assert.DoesNotContain("01", VisibleEntries(host));
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Up);
        host.Press(TerminalKey.PageUp);
        var view = VisibleEntries(host);
        Assert.DoesNotContain("40", view);
        host.SetSnapshot(WithLog(41, version: 2));
        Assert.Equal(view, VisibleEntries(host));

        host.Press(TerminalKey.End);
        Assert.Equal("41", VisibleEntries(host)[^1]);
        host.SetSnapshot(WithLog(42, version: 3));
        Assert.Equal("42", VisibleEntries(host)[^1]);

        host.Press(TerminalKey.Home);
        Assert.Equal("01", VisibleEntries(host)[0]);
        host.SetSnapshot(WithLog(43, version: 4));
        Assert.Equal("01", VisibleEntries(host)[0]);
    }

    [Fact]
    public void The_log_stops_following_on_the_wheel_up_and_follows_again_when_the_wheel_reaches_the_last_entry()
    {
        using var host = UiTestHost.Start(Pages(), WithLog(40));
        var row = RowOf(host, "[alpha] entry 40");
        var column = Lines(host)[row].IndexOf("entry", StringComparison.Ordinal);

        host.Wheel(column, row, 12);
        var view = VisibleEntries(host);
        Assert.DoesNotContain("40", view);
        host.SetSnapshot(WithLog(41, version: 2));
        Assert.Equal(view, VisibleEntries(host));

        host.Wheel(column, row, -13);
        Assert.Equal("41", VisibleEntries(host)[^1]);
        host.SetSnapshot(WithLog(42, version: 3));
        Assert.Equal("42", VisibleEntries(host)[^1]);
    }

    [Fact]
    public void The_log_follows_again_after_Down_reaches_the_last_entry()
    {
        using var host = UiTestHost.Start(Pages(), WithLog(40));
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Up);
        host.SetSnapshot(WithLog(41, version: 2));
        Assert.Equal("40", VisibleEntries(host)[^1]);

        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);
        host.SetSnapshot(WithLog(42, version: 3));
        Assert.Equal("42", VisibleEntries(host)[^1]);
    }

    [Fact]
    public void The_selected_task_survives_a_new_snapshot()
    {
        var first = SampleRun.Create();
        using var host = UiTestHost.Start(Pages(), first);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);

        var zeta = first.Tasks[0] with { Id = "zeta", Title = "Zeta first" };
        host.SetSnapshot(first with { Version = 2, Tasks = [zeta, .. first.Tasks.Select(t => t with { })] });
        Assert.Matches("→ ▶  beta ", host.Frame());

        host.Press(TerminalKey.Enter);
        AssertTaskPopup(host, "beta - Beta checker", "Status", "Plan", "Prompt", "Sessions");
    }

    private static string SubAgentKey(RunSnapshot snapshot, string sessionKey, string agentId) =>
        AgentKey.Of(snapshot.Sessions.Single(s => s.Files.Key == sessionKey), agentId);

    [Fact]
    public void The_task_table_shows_the_child_rows_the_running_block_its_sub_agents_and_the_log_their_paths()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.CreateSubAgents());
        var frame = host.Frame();
        var lines = Lines(host);

        // 39.1: under its task, starting at the id column.
        var alpha = RowOf(host, "✔  alpha ");
        Assert.Contains("├✔ Survey the parser module · worker #1 · 2 tool calls · 41s", lines[alpha + 1], StringComparison.Ordinal);
        Assert.Contains("└✖ Check the public API surface of… · worker #1 · 1 tool call · 44s", lines[alpha + 2], StringComparison.Ordinal);
        Assert.Contains("✖  gamma ", lines[alpha + 3], StringComparison.Ordinal);
        var idColumn = lines[alpha].IndexOf("alpha", StringComparison.Ordinal);
        Assert.Equal(idColumn + 1, lines[alpha + 1].IndexOf('├', StringComparison.Ordinal));
        Assert.Equal(idColumn + 1, lines[alpha + 2].IndexOf('└', StringComparison.Ordinal));
        var beta = RowOf(host, "▶  beta ");
        Assert.Contains("└▶ Survey CLI flags · worker #1 · 1 tool call · 50s", lines[beta + 1], StringComparison.Ordinal);
        Assert.Contains("⊘  delta ", lines[beta + 2], StringComparison.Ordinal);
        Assert.DoesNotContain("Map the repo · planner", frame, StringComparison.Ordinal);
        Assert.Contains("1/8", frame, StringComparison.Ordinal);

        // 39.2: the agent's own tool calls and items, then the sub-agent's line in the same block.
        var block = RowOf(host, "beta · Worker · #1 · claude-sonnet-4-5 · 4 tool calls");
        Assert.Contains("  context 34.5k of 200.0k (17 %)", lines[block + 1], StringComparison.Ordinal);
        Assert.Contains("  Agent Survey CLI flags", lines[block + 5], StringComparison.Ordinal);
        Assert.Contains("  Bash dotnet build src/Beta", lines[block + 6], StringComparison.Ordinal);
        Assert.Contains("└▶ Survey CLI flags · 1 tool call · context 3.6k", lines[block + 7], StringComparison.Ordinal);
        Assert.Contains("1/1", frame, StringComparison.Ordinal);

        // 39.3
        Assert.Contains("11:59:30 [planner › Map the repo] Glob **/*", frame, StringComparison.Ordinal);
        host.SaveSvg("overview-subagents");
    }

    [Fact]
    public void Enter_on_a_child_row_shows_its_sub_agent_on_the_conversation_page()
    {
        var snapshot = SampleRun.CreateSubAgents();
        using var host = UiTestHost.Start(Pages(), snapshot);

        host.Press(TerminalKey.Down);
        Assert.Contains("2/8", host.Frame(), StringComparison.Ordinal);
        host.Press(TerminalKey.Enter);

        Assert.Contains(ConversationStubPage.Prefix + SubAgentKey(snapshot, SampleRun.AlphaWorkerKey, SampleRun.AlphaSub1Id),
            host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("Phase: Running", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_a_child_row_shows_its_sub_agent_on_the_conversation_page()
    {
        var snapshot = SampleRun.CreateSubAgents();
        using var host = UiTestHost.Start(Pages(), snapshot);

        host.ClickText("Survey CLI flags · worker #1");

        Assert.Contains(ConversationStubPage.Prefix + SubAgentKey(snapshot, SampleRun.BetaWorkerKey, SampleRun.BetaSub1Id),
            host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_on_a_task_row_with_child_rows_opens_its_popup_with_the_sub_agents()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.CreateSubAgents());

        host.Press(TerminalKey.Enter);

        AssertTaskPopup(host, "alpha - Alpha parser", "Status", "Plan", "Prompt", "Summary", "Notes", "Sessions");
        var popup = PopupLines(host);
        var sessions = popup.IndexOf("Sessions");
        Assert.Equal(
        [
            "Worker · #1 · Succeeded · claude-sonnet-4-5",
            "  ├ Survey the parser module · Explore · Succeeded · claude-haiku-4-5",
            "  └ Check the public API surface of… · general-purpose · Failed · claude-sonnet-4-5",
            "Reviewer · #1.1 · Succeeded · gpt-5.1",
        ], popup.Skip(sessions + 1).Take(4));
    }

    [Fact]
    public void The_selected_child_row_survives_a_new_snapshot()
    {
        var first = SampleRun.CreateSubAgents();
        using var host = UiTestHost.Start(Pages(), first);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);
        Assert.Matches("→ +└✖ Check the public API", host.Frame());

        var zeta = first.Tasks[0] with { Id = "zeta", Title = "Zeta first" };
        host.SetSnapshot(first with { Version = 2, Tasks = [zeta, .. first.Tasks.Select(t => t with { })] });
        Assert.Matches("→ +└✖ Check the public API", host.Frame());
        Assert.Contains("4/9", host.Frame(), StringComparison.Ordinal);

        host.Press(TerminalKey.Enter);
        Assert.Contains(ConversationStubPage.Prefix + SubAgentKey(first, SampleRun.AlphaWorkerKey, SampleRun.AlphaSub2Id),
            host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_selected_running_block_survives_a_new_snapshot()
    {
        var first = SampleRun.Create();
        var other = first.Sessions[2] with { Files = first.Sessions[2].Files with { Key = "gamma/20261003-122000/attempt-4-worker.json", TaskId = "gamma" } };
        using var host = UiTestHost.Start(Pages(), first with { Sessions = first.Sessions.Add(other) });
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        Assert.Matches("→ gamma · Worker", host.Frame());

        host.SetSnapshot(first with { Version = 2, Sessions = [other, .. first.Sessions] });
        Assert.Matches("→ gamma · Worker", host.Frame());

        host.Press(TerminalKey.Enter);
        Assert.Contains(ConversationStubPage.Prefix + other.Files.Key, host.Frame(), StringComparison.Ordinal);
    }
}

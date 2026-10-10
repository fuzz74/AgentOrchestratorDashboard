using System.Collections.Immutable;
using System.Text.RegularExpressions;
using OrchDash.Core.Model;
using OrchDash.Pages.Usage;
using OrchDash.Shell;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.Pages.Usage;

public sealed partial class UsagePageTests
{
    private const string AlphaRow = "alpha 2 4 2.0k 70.2k 21.4k 4.3k 0.25 USD 1 3.99 AIU +12 -3";
    private const string GammaRow = "gamma 2 4 10 89.2k 24.5k 5.1k 0.62 USD - - -";
    private const string BetaRow = "beta 1 2 1.7k 47.8k 3.8k - - - - -";
    private const string AlphaWorkerRow = "✔ worker #1 claude-sonnet-4-5 2 43.3k 2.0k 56.0k 4.5k 3.1k 0.25 USD - +12 -3";
    private const string AlphaReviewRow = "✔ reviewer #1.1 gpt-5.1 2 17.0k 30 14.2k 16.9k 1.2k - 3.99 AIU +0 -0";
    private const string GammaWorker1Row = "✖ worker #1 claude-opus-4-5 2 26.5k 6 32.0k 17.5k 2.3k 0.29 USD - -";
    private const string GammaWorker2Row = "✖ worker #2 claude-opus-4-5 2 33.5k 4 57.2k 7.0k 2.9k 0.33 USD - -";
    private const string BetaWorkerRow = "▶ worker #1 claude-sonnet-4-5 2 34.5k 1.7k 47.8k 3.8k - - - -";

    // CreateSubAgents()
    private const string SubAlphaRow = "alpha 2 7 2.0k 74.2k 36.6k 4.3k 0.25 USD 1 3.99 AIU +12 -3 2 · 17 %";
    private const string SubAlphaWorkerRow = "✔ worker #1 claude-sonnet-4-5 5 43.3k 2.0k 60.0k 19.7k 3.1k 0.25 USD - +12 -3";
    private const string AlphaSub1Row = "├✔ Survey the parser module claude-haiku-4-5 2 5.4k 5 4.0k 5.4k 380 - - -";
    private const string AlphaSub2Row = "└✖ Check the public API surface of… claude-sonnet-4-5 1 9.8k 3 0 9.8k - - - -";

    private static UiTestHost Start(RunSnapshot? snapshot = null) =>
        UiTestHost.Start([new UsagePage(), new SessionKeyPage()], snapshot ?? SampleRun.CreateEnriched());

    // Wide enough for the group table with its Sub column (43.4).
    private static UiTestHost StartWithSubAgents() =>
        UiTestHost.Start([new UsagePage(), new SessionKeyPage()], SampleRun.CreateSubAgents(), width: 180);

    [Fact]
    public void The_page_shows_the_run_the_rate_limits_the_versions_the_groups_the_chart_and_the_first_groups_sessions()
    {
        using var host = Start();

        var run = PaneText(host.Frame(), "Run");
        foreach (var figure in new[]
        {
            "Sessions: 5", "Calls: 10", "Input: 3.7k", "Cache read: 207.2k", "Cache write: 49.7k", "Output: 9.4k",
            "Thinking: 1.9k", "Cost: 0.87 USD", "Premium requests: 1", "AIU: 3.99 AIU", "Lines: +12 -3",
        })
        {
            Assert.Contains(figure, run, StringComparison.Ordinal);
        }
        var lines = PaneLines(host.Frame(), "Run");
        Assert.Contains("Rate limits: 5-hour 20 %, resets 17:00 · 7-day 27 %, resets 2026-10-07 09:00 · allowed", lines);
        Assert.Contains("Versions: Claude Code 2.1.3 (made for 2.1.285) · Copilot CLI 1.0.91 (made for 1.0.91)", lines);
        Assert.Equal(["Group Sess Calls Input C.read C.write Output Cost Prem AIU Lines", AlphaRow, GammaRow, BetaRow], Rows(host.Frame(), "Groups"));
        Assert.Equal(AlphaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Contains("Tokens per group", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(["Role Attempt Model Calls Peak Input C.read C.write Output Cost AIU Lines", AlphaWorkerRow, AlphaReviewRow], Rows(host.Frame(), "Sessions"));
        Assert.Equal(AlphaWorkerRow, SelectedRow(host.Frame(), "Sessions"));
        Assert.Contains("[Enter] Sessions", host.Frame(), StringComparison.Ordinal);
        Assert.Contains("[Tab] Switch table", host.Frame(), StringComparison.Ordinal);
        host.SaveSvg("usage");
    }

    [Fact]
    public void The_chart_has_one_bar_per_group_with_its_tokens()
    {
        using var host = Start();

        var frame = host.Frame();

        Assert.Contains(frame.Split('\n'), line => Spaced(line).Contains("alpha █", StringComparison.Ordinal) && line.Contains("98.0k", StringComparison.Ordinal));
        Assert.Contains(frame.Split('\n'), line => Spaced(line).Contains("gamma █", StringComparison.Ordinal) && line.Contains("118.8k", StringComparison.Ordinal));
        Assert.Contains(frame.Split('\n'), line => Spaced(line).Contains("beta █", StringComparison.Ordinal) && line.Contains("53.3k", StringComparison.Ordinal));
    }

    [Fact]
    public void Down_on_the_group_table_shows_the_next_groups_sessions_without_setting_the_key()
    {
        using var host = Start();

        host.Press(TerminalKey.Down);

        Assert.Equal(GammaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal([GammaWorker1Row, GammaWorker2Row], SessionRows(host.Frame()));
        Assert.Equal(GammaWorker1Row, SelectedRow(host.Frame(), "Sessions"));
        host.Type('2');
        Assert.Contains("selected key: none", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_a_group_shows_its_sessions()
    {
        using var host = Start();

        host.ClickText("beta ");

        Assert.Equal(BetaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal([BetaWorkerRow], SessionRows(host.Frame()));
        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Moving_to_a_session_row_sets_the_selected_key()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Down);

        Assert.Equal(AlphaReviewRow, SelectedRow(host.Frame(), "Sessions"));
        Assert.Equal(AlphaRow, SelectedRow(host.Frame(), "Groups"));
        host.Type('2');
        Assert.Contains($"selected key: {SampleRun.AlphaReviewKey}", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_a_session_row_selects_it_sets_the_key_and_opens_its_popup()
    {
        using var host = Start();
        host.Press(TerminalKey.Down);

        host.ClickText("worker     #2");

        AssertPopup(host.Frame(), "Usage: gamma worker #2", "Totals", "Calls");
        Assert.Contains("cost: 0.33 USD", PopupLines(host.Frame()));
        host.Press(TerminalKey.Escape);
        Assert.Equal(GammaWorker2Row, SelectedRow(host.Frame(), "Sessions"));
        host.Type('2');
        Assert.Contains($"selected key: {SampleRun.GammaWorker2Key}", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_set_by_another_page_selects_that_sessions_group_and_row()
    {
        using var host = Start();
        host.Type('2');

        host.Type('g');

        Assert.Equal(GammaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal([GammaWorker1Row, GammaWorker2Row], SessionRows(host.Frame()));
        Assert.Equal(GammaWorker2Row, SelectedRow(host.Frame(), "Sessions"));
    }

    [Fact]
    public void A_key_set_by_another_page_wins_over_the_group_picked_before()
    {
        using var host = Start();
        host.ClickText("beta ");
        host.Type('2');

        host.Type('g');

        Assert.Equal(GammaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal(GammaWorker2Row, SelectedRow(host.Frame(), "Sessions"));
        host.Press(TerminalKey.Up);
        Assert.Equal(AlphaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal(AlphaWorkerRow, SelectedRow(host.Frame(), "Sessions"));
    }

    [Fact]
    public void Enter_on_a_session_row_opens_its_usage_popup_and_Escape_closes_it()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Enter);

        AssertPopup(host.Frame(), "Usage: alpha worker #1", "Totals", "Calls");
        var popup = PopupLines(host.Frame());
        Assert.Contains("calls: 2", popup);
        Assert.Contains("peak context: 43.3k of 200.0k (22 %)", popup);
        Assert.Contains("lines: +12 -3", popup);
        Assert.Contains(popup, line => line.StartsWith("call 1 · 12:00:10 · claude-sonnet-4-5 · input 1.2k", StringComparison.Ordinal));
        Assert.DoesNotContain("Unavailable", popup);
        host.SaveSvg("usage-session-popup");

        host.Press(TerminalKey.Escape);

        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(AlphaWorkerRow, SelectedRow(host.Frame(), "Sessions"));
    }

    [Fact]
    public void The_popup_of_the_beta_worker_has_the_unavailable_section()
    {
        using var host = Start();
        host.ClickText("beta ");
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Enter);

        AssertPopup(host.Frame(), "Usage: beta worker #1", "Totals", "Calls", "Unavailable");
        Assert.Contains("no transcript", PopupLines(host.Frame()));
    }

    [Fact]
    public void Enter_on_a_group_row_moves_the_keys_to_the_session_table()
    {
        using var host = Start();
        host.Press(TerminalKey.Down);

        host.Press(TerminalKey.Enter);
        host.Press(TerminalKey.Down);

        Assert.Equal(GammaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal(GammaWorker2Row, SelectedRow(host.Frame(), "Sessions"));
        Assert.Contains("[Enter] Details", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_moves_the_keys_between_the_group_table_and_the_session_table()
    {
        using var host = Start();

        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);

        Assert.Equal(AlphaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal(AlphaReviewRow, SelectedRow(host.Frame(), "Sessions"));

        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);

        Assert.Equal(GammaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal(GammaWorker1Row, SelectedRow(host.Frame(), "Sessions"));
    }

    [Fact]
    public void Shift_Tab_moves_the_keys_between_the_group_table_and_the_session_table()
    {
        var snapshot = SampleRun.CreateEnriched();
        var shell = new AppShell([new UsagePage()], () => snapshot, new FixedTimeProvider(snapshot.ReadAt));
        using var harness = TerminalHarness.Start(shell.Root, shell.OnUpdate);

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        harness.Press(TerminalKey.Down);

        Assert.Equal(AlphaRow, SelectedRow(harness.Frame(), "Groups"));
        Assert.Equal(AlphaReviewRow, SelectedRow(harness.Frame(), "Sessions"));

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        harness.Press(TerminalKey.Down);

        Assert.Equal(GammaRow, SelectedRow(harness.Frame(), "Groups"));
    }

    [Fact]
    public void A_new_snapshot_keeps_the_selected_group_and_session()
    {
        using var host = Start();
        host.ClickText("gamma ");
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        Assert.Equal(GammaWorker2Row, SelectedRow(host.Frame(), "Sessions"));

        var next = SampleRun.CreateEnriched();
        host.SetSnapshot(next with { Version = 2, Sessions = next.Sessions.RemoveAt(1) });

        Assert.Equal("gamma 1 2 4 57.2k 7.0k 2.9k 0.33 USD - - -", SelectedRow(host.Frame(), "Groups"));
        Assert.Equal([GammaWorker2Row], SessionRows(host.Frame()));
        Assert.Equal(GammaWorker2Row, SelectedRow(host.Frame(), "Sessions"));
    }

    [Fact]
    public void A_new_snapshot_without_the_selected_group_selects_the_first_group()
    {
        using var host = Start();
        host.ClickText("beta ");

        var next = SampleRun.CreateEnriched();
        host.SetSnapshot(next with { Version = 2, Sessions = next.Sessions.RemoveAt(3) });

        Assert.Equal(AlphaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal([AlphaWorkerRow, AlphaReviewRow], SessionRows(host.Frame()));
    }

    [Fact]
    public void The_wheel_scrolls_the_session_table()
    {
        using var host = Start(WithAlphaWorkers(30));
        Assert.StartsWith("✔ worker #1 ", SessionRows(host.Frame())[0], StringComparison.Ordinal);

        var frame = host.Frame().Split('\n');
        var top = Array.FindIndex(frame, line => line.Contains("┌ Sessions", StringComparison.Ordinal));
        host.Wheel(40, top + 5, -5);

        var rows = SessionRows(host.Frame());
        Assert.False(rows[0].StartsWith("✔ worker #1 ", StringComparison.Ordinal), host.Frame());
        Assert.Null(SelectedRow(host.Frame(), "Sessions", visible: false));
    }

    [Fact]
    public void A_run_without_sessions_shows_dashes_and_empty_tables()
    {
        using var host = Start(SampleRun.CreateEnriched() with { Sessions = [] });

        var run = PaneText(host.Frame(), "Run");
        foreach (var figure in new[] { "Sessions: 0", "Calls: 0", "Input: -", "Output: -", "Cost: -", "AIU: -", "Lines: -" })
        {
            Assert.Contains(figure, run, StringComparison.Ordinal);
        }
        Assert.Contains("Rate limits: -", PaneLines(host.Frame(), "Run"));
        Assert.Contains("Versions: -", PaneLines(host.Frame(), "Run"));
        Assert.Equal("No sessions yet", PaneLines(host.Frame(), "Groups")[1]);
        Assert.Equal("No sessions yet", PaneLines(host.Frame(), "Sessions")[1]);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Enter);
        host.Press(TerminalKey.Enter);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Enter);
        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_sub_agents_the_page_shows_their_number_the_sub_column_the_sub_agent_bars_and_child_rows()
    {
        using var host = StartWithSubAgents();

        Assert.Contains("sub-agents 6", PaneText(host.Frame(), "Run"), StringComparison.Ordinal);
        Assert.Equal(
        [
            "Group Sess Calls Input C.read C.write Output Cost Prem AIU Lines Sub",
            "planner 1 5 88 12.4k 34.6k 1.4k - 1 4.20 AIU +0 -0 3 · 40 %",
            SubAlphaRow,
            "gamma 2 4 10 89.2k 24.5k 5.1k 0.62 USD - - - -",
            "beta 1 3 1.7k 47.8k 7.4k - - - - - 1 · 6 %",
        ], Rows(host.Frame(), "Groups"));
        AssertSubAgentBar(host.Frame(), "planner", "19.4k");
        AssertSubAgentBar(host.Frame(), "alpha", "19.5k");
        AssertSubAgentBar(host.Frame(), "beta", "3.6k");
        Assert.Null(SubAgentBarUnder(host.Frame(), "gamma"));
        Assert.Equal(
        [
            "✔ planner #1 gpt-5.6-luna 5 15.8k 88 12.4k 34.6k 1.4k - 4.20 AIU +0 -0",
            "├✔ Map the repo gpt-5.6-luna 1 6.8k 12 0 6.8k 210 - 0.31 AIU -",
            "│ └✔ Read the spec gpt-5.6-luna 1 5.9k 10 0 5.9k 180 - 0.27 AIU -",
            "└✔ Survey the tests gpt-5.6-luna 1 6.1k 11 0 6.1k 190 - 0.29 AIU -",
        ], SessionRows(host.Frame()));

        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);

        Assert.Equal(SubAlphaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal([SubAlphaWorkerRow, AlphaSub1Row, AlphaSub2Row, AlphaReviewRow], SessionRows(host.Frame()));
        Assert.Equal(AlphaSub1Row, SelectedRow(host.Frame(), "Sessions"));
        host.SaveSvg("usage-subagents");
    }

    [Fact]
    public void Moving_to_a_child_row_sets_the_key_of_its_sub_agent()
    {
        using var host = StartWithSubAgents();
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);

        Assert.Equal(AlphaSub2Row, SelectedRow(host.Frame(), "Sessions"));
        host.Type('2');
        Assert.Contains($"selected key: {SampleRun.AlphaWorkerKey}|{SampleRun.AlphaSub2Id}", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_on_a_child_row_opens_the_usage_popup_of_its_sub_agent()
    {
        using var host = StartWithSubAgents();
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);

        host.Press(TerminalKey.Enter);

        AssertPopup(host.Frame(), "Usage: alpha worker #1 › Survey the parser module", "Totals", "Calls");
        var popup = PopupLines(host.Frame());
        Assert.Contains("calls: 2", popup);
        Assert.Contains("peak context: 5.4k", popup);
        Assert.Contains(popup, line => line.StartsWith("call 2 · 12:00:50 · claude-haiku-4-5 · input 2 ·", StringComparison.Ordinal));
        Assert.DoesNotContain(popup, line => line.StartsWith("Sub-agent ", StringComparison.Ordinal));

        host.Press(TerminalKey.Escape);

        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(AlphaSub1Row, SelectedRow(host.Frame(), "Sessions"));
    }

    [Fact]
    public void A_click_on_a_child_row_selects_it_and_opens_its_popup()
    {
        using var host = StartWithSubAgents();

        host.ClickText("Read the spec");

        AssertPopup(host.Frame(), "Usage: planner #1 › Map the repo › Read the spec", "Totals", "Calls");
        Assert.Contains("AIU: 0.27 AIU", PopupLines(host.Frame()));
        host.Press(TerminalKey.Escape);
        host.Type('2');
        Assert.Contains($"selected key: {SampleRun.PlannerKey}|{SampleRun.PlannerSub2Id}", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_popup_of_a_session_with_sub_agents_has_a_section_per_sub_agent()
    {
        using var host = StartWithSubAgents();
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Enter);

        AssertPopup(host.Frame(), "Usage: alpha worker #1", "Totals", "Calls",
            "Sub-agent Survey the parser module", "Sub-agent Check the public API surface of…");
        Assert.Contains("calls: 5", PopupLines(host.Frame()));
    }

    [Fact]
    public void A_sub_agent_key_set_by_another_page_selects_its_sessions_group_and_its_child_row()
    {
        using var host = StartWithSubAgents();
        host.Type('2');

        host.Type('s');

        Assert.Equal(SubAlphaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal(AlphaSub1Row, SelectedRow(host.Frame(), "Sessions"));
    }

    [Fact]
    public void A_key_with_a_sub_agent_that_its_session_does_not_have_selects_the_session_row()
    {
        using var host = StartWithSubAgents();
        host.Type('2');

        host.Type('u');

        Assert.Equal(SubAlphaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal(AlphaReviewRow, SelectedRow(host.Frame(), "Sessions"));
    }

    [Fact]
    public void A_new_snapshot_keeps_the_selected_child_row()
    {
        using var host = StartWithSubAgents();
        host.Type('2');
        host.Type('s');

        host.SetSnapshot(SampleRun.CreateSubAgents() with { Version = 2 });

        Assert.Equal(SubAlphaRow, SelectedRow(host.Frame(), "Groups"));
        Assert.Equal(AlphaSub1Row, SelectedRow(host.Frame(), "Sessions"));
    }

    [Fact]
    public void Without_sub_agents_the_page_shows_no_sub_column_bars_or_line()
    {
        foreach (var snapshot in new[] { SampleRun.Create(), SampleRun.CreateEnriched() })
        {
            using var host = Start(snapshot);

            Assert.DoesNotContain("sub-agents", host.Frame(), StringComparison.Ordinal);
            Assert.Equal("Group Sess Calls Input C.read C.write Output Cost Prem AIU Lines", Rows(host.Frame(), "Groups")[0]);
        }
    }

    /// <summary>Asserts that the chart's bar right under the group's is "└ sub-agents" with these tokens.</summary>
    private static void AssertSubAgentBar(string frame, string group, string tokens)
    {
        var bar = SubAgentBarUnder(frame, group);
        Assert.True(bar is not null, frame);
        Assert.Contains(tokens, bar, StringComparison.Ordinal);
    }

    /// <summary>The line under the group's bar when it holds a "└ sub-agents" bar, with single spaces; else null.</summary>
    private static string? SubAgentBarUnder(string frame, string group)
    {
        var lines = frame.Split('\n').Select(Spaced).ToArray();
        var bar = Array.FindIndex(lines, line => line.Contains($"{group} █", StringComparison.Ordinal));
        Assert.True(bar >= 0, frame);
        return bar + 1 < lines.Length && lines[bar + 1].Contains("└ sub-agents █", StringComparison.Ordinal) ? lines[bar + 1] : null;
    }

    /// <summary>The enriched run with the alpha worker repeated as attempts 2 to <paramref name="attempts"/>.</summary>
    private static RunSnapshot WithAlphaWorkers(int attempts)
    {
        var snapshot = SampleRun.CreateEnriched();
        var worker = snapshot.Sessions[0];
        var copies = Enumerable.Range(2, attempts - 1).Select(attempt => worker with
        {
            Files = worker.Files with { Key = $"alpha/20261003-120006/attempt-{attempt:00}-worker.json", Attempt = attempt },
        });
        ImmutableArray<Session> sessions = [.. snapshot.Sessions.Concat(copies)
            .OrderBy(session => session.StartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(session => session.Files.Key, StringComparer.Ordinal)];
        return snapshot with { Sessions = sessions };
    }

    /// <summary>The text rows inside the pane with that title, without its borders and scroll bar.</summary>
    private static string[] PaneLines(string frame, string title)
    {
        var lines = frame.Split('\n');
        var top = Array.FindIndex(lines, line => line.Contains($"┌ {title} ", StringComparison.Ordinal));
        Assert.True(top >= 0, frame);
        var left = lines[top].IndexOf($"┌ {title} ", StringComparison.Ordinal);
        var right = lines[top].IndexOf('┐', left);
        var rows = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > left && lines[row][left] == '│'; row++)
        {
            var line = lines[row];
            rows.Add(line.Length <= left + 1 ? "" : line[(left + 1)..Math.Min(right - 1, line.Length)].TrimEnd());
        }
        return [.. rows];
    }

    /// <summary>The pane's rows joined into one line with single spaces.</summary>
    private static string PaneText(string frame, string title) => Spaced(string.Join(' ', PaneLines(frame, title)));

    /// <summary>The non-empty rows of a table pane (header first) without the marker column, with single spaces.</summary>
    private static string[] Rows(string frame, string title) =>
        [.. PaneLines(frame, title).Where(line => line.Trim().Length > 0).Select(line => Spaced(line[2..]))];

    private static string[] SessionRows(string frame) => Rows(frame, "Sessions")[1..];

    /// <summary>The row with the selection arrow in a table pane, with single spaces.</summary>
    private static string? SelectedRow(string frame, string title, bool visible = true)
    {
        var row = PaneLines(frame, title).FirstOrDefault(line => line.StartsWith('→'));
        if (visible)
        {
            Assert.True(row is not null, frame);
        }
        return row is null ? null : Spaced(row[2..]);
    }

    /// <summary>The text rows of the open pop-up, without borders and scroll bar.</summary>
    private static string[] PopupLines(string frame)
    {
        var lines = frame.Split('\n');
        var top = Array.FindIndex(lines, line => line.Contains('┌') && line.Contains("[X]", StringComparison.Ordinal));
        Assert.True(top >= 0, frame);
        var left = lines[top].IndexOf('┌');
        var right = lines[top].IndexOf('┐', left);
        var rows = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > left && lines[row][left] == '│'; row++)
        {
            rows.Add(lines[row][(left + 1)..(right - 1)].TrimEnd());
        }
        return [.. rows];
    }

    private static void AssertPopup(string frame, string title, params string[] headings)
    {
        Assert.Contains(frame.Split('\n'), line => line.Contains($"┌ {title} ", StringComparison.Ordinal) && line.Contains("[X]", StringComparison.Ordinal));
        var popup = PopupLines(frame);
        var positions = headings.Select(heading => Array.IndexOf(popup, heading)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0, frame));
        Assert.Equal(positions.Order(), positions);
    }

    private static string Spaced(string text) => Spaces().Replace(text, " ").Trim();

    [GeneratedRegex(" +")]
    private static partial Regex Spaces();
}

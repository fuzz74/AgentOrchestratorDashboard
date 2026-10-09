using System.Collections.Immutable;
using System.Text.RegularExpressions;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Core.Timeline;
using OrchDash.Pages.Timeline;
using OrchDash.Pages.Timeline.Format;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using Xunit;

namespace OrchDash.Tests.Pages.Timeline;

public sealed partial class TimelinePageTests
{
    private const string FilterRow = "[o] orchestrator  [a] calls  [u] tools  [x] text  [e] prompt/result  task: all  ▶ replay here";
    private const string RunStarted = "12:00:00  orchestrator         orch    Run started: 5 tasks, max 2 in parallel";
    private const string ClaudePath = @"12:00:00  orchestrator         orch    Claude: C:\Users\sample\.local\bin\claude.exe";
    private const string AlphaStarted = @"12:00:05  orchestrator         orch    [alpha] started (fresh) in C:\Work\SampleRepo.worktrees\alpha";
    private const string LastRow = "12:29:30  beta worker #1       tool    Bash dotnet build src/Beta · running";

    // Tall enough for all 60 rows of the sample timeline.
    private const int TallHeight = 80;

    private const string SubAgentFilterRow =
        "[o] orchestrator  [a] calls  [u] tools  [x] text  [e] prompt/result  [s] sub-agents  task: all  ▶ replay here";
    private const string AlphaSub1Path = "alpha worker #1 › Survey the parser module";
    private const string AlphaSub1Result =
        "12:00:58  alpha worker #1 › Survey the parser module          result  result succeeded · The parser module has 3 files.";

    // Tall enough for every row of the sub-agent sample.
    private const int SubAgentsHeight = 110;

    // Wide enough for the whole command bar, which 160 columns cut after the kind keys.
    private const int WideWidth = 260;

    private static UiTestHost Start(RunSnapshot? snapshot = null, int height = 45, int width = 160) =>
        UiTestHost.Start([new TimelinePage(), new ConversationStubPage()], snapshot ?? SampleRun.CreateTimeline(), width, height);

    private static ImmutableArray<TimelineEvent> SampleEvents() => TimelineBuilder.Build(SampleRun.CreateTimeline());

    private static ImmutableArray<TimelineEvent> SubAgentEvents() => TimelineBuilder.Build(SampleRun.CreateSubAgents());

    /// <summary>The list on screen holds the rows of exactly these events; a row wider than the list is cut at its edge.</summary>
    private static void AssertRows(ImmutableArray<TimelineEvent> events, string frame)
    {
        var expected = TimelineText.PlainRows(events);
        var rows = ListRows(frame);
        Assert.Equal(expected.Length, rows.Count);
        Assert.All(rows.Zip(expected), pair => Assert.StartsWith(pair.First, pair.Second, StringComparison.Ordinal));
    }

    private static string[] Lines(string frame) => frame.Split('\n');

    private static int RowOf(string frame, string text)
    {
        var row = Array.FindIndex(Lines(frame), line => line.Contains(text, StringComparison.Ordinal));
        Assert.True(row >= 0, $"'{text}' is not on screen:\n{frame}");
        return row;
    }

    private static int ColumnOf(string frame, string text) => Lines(frame)[RowOf(frame, text)].IndexOf(text, StringComparison.Ordinal);

    // A list row: the left border, the selection marker, a space, the row text, then the scroll bar and the right border.
    [GeneratedRegex(@"^│(?<marker>→| ) (?<row>\d\d:\d\d:\d\d  .*?)\s*[░█]?│$")]
    private static partial Regex ListRow();

    /// <summary>The row texts of the list on screen, top to bottom.</summary>
    private static List<string> ListRows(string frame) =>
        [.. Lines(frame).Select(line => ListRow().Match(line)).Where(m => m.Success).Select(m => m.Groups["row"].Value)];

    /// <summary>The text of the row with the selection marker.</summary>
    private static string SelectedRow(string frame)
    {
        var selected = Lines(frame).Select(line => ListRow().Match(line)).Where(m => m.Success && m.Groups["marker"].Value == "→").ToList();
        Assert.True(selected.Count == 1, $"Expected one selected row:\n{frame}");
        return selected[0].Groups["row"].Value;
    }

    /// <summary>The text lines inside the pop-up, without its borders and scroll bar; empty when no pop-up is open.</summary>
    private static List<string> PopupLines(string frame)
    {
        var lines = Lines(frame);
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

    private static void AssertPopupTitle(string frame, string title)
    {
        var titleRow = Lines(frame).FirstOrDefault(line => line.Contains("[X] ┐", StringComparison.Ordinal)) ?? "";
        Assert.Contains($"┌ {title} ", titleRow, StringComparison.Ordinal);
    }

    private static void AssertNoPopup(string frame) => Assert.DoesNotContain("[X]", frame, StringComparison.Ordinal);

    private static RunSnapshot WithEntry(RunSnapshot run, long version, int minute, int second) => run with
    {
        Version = version,
        Progress = run.Progress.Add(new ProgressEntry(SampleRun.At(12, minute, second), "beta",
            $"acceptance: dotnet test tests/Beta {second}", ProgressKind.Info)),
    };

    private static string BetaRow(int minute, int second) =>
        $"12:{minute:00}:{second:00}  orchestrator         orch    [beta] acceptance: dotnet test tests/Beta {second}";

    [Fact]
    public void The_list_shows_every_event_as_a_row_and_starts_on_the_last_one()
    {
        using var host = Start(height: TallHeight);
        var frame = host.Frame();

        var rows = ListRows(frame);
        Assert.Equal(60, rows.Count);
        Assert.Equal(TimelineText.PlainRows(SampleEvents()), rows);
        Assert.Equal([RunStarted, ClaudePath, AlphaStarted], rows.Take(3));
        Assert.Equal(LastRow, SelectedRow(frame));
        Assert.Contains(FilterRow, frame, StringComparison.Ordinal);
        Assert.True(RowOf(frame, FilterRow) < RowOf(frame, RunStarted), frame);
    }

    [Fact]
    public void The_page_follows_the_end_of_the_list_and_lists_its_keys_in_the_command_bar()
    {
        using var host = Start();
        var frame = host.Frame();

        Assert.Equal(LastRow, SelectedRow(frame));
        Assert.Equal(LastRow, ListRows(frame)[^1]);
        Assert.DoesNotContain(RunStarted, frame, StringComparison.Ordinal);
        Assert.Contains("[End] Follow", frame, StringComparison.Ordinal);
        Assert.Contains("[Enter] Open", frame, StringComparison.Ordinal);
        Assert.Contains("[c] Conversation", frame, StringComparison.Ordinal);
        Assert.Contains("[t] Replay here", frame, StringComparison.Ordinal);
        Assert.Contains("[f] Task filter", frame, StringComparison.Ordinal);
        Assert.Contains("[o] Orchestrator", frame, StringComparison.Ordinal);
        host.SaveSvg("timeline");

        host.Press(TerminalKey.Home);

        frame = host.Frame();
        Assert.Equal(RunStarted, SelectedRow(frame));
        Assert.Equal([RunStarted, ClaudePath, AlphaStarted], ListRows(frame).Take(3));
    }

    [Fact]
    public void U_and_a_click_on_the_tools_label_toggle_the_tool_rows()
    {
        using var host = Start(height: TallHeight);

        host.Type('u');

        var rows = ListRows(host.Frame());
        Assert.Equal(50, rows.Count);
        Assert.DoesNotContain(rows, row => row.Contains("  tool  ", StringComparison.Ordinal));
        Assert.DoesNotContain("Edit src/Alpha/Parser.cs", host.Frame(), StringComparison.Ordinal);

        host.Type('u');
        Assert.Equal(60, ListRows(host.Frame()).Count);

        host.ClickText("[u] tools");
        Assert.Equal(50, ListRows(host.Frame()).Count);
        host.ClickText("[u] tools");
        Assert.Equal(60, ListRows(host.Frame()).Count);
    }

    [Fact]
    public void The_kind_keys_and_labels_hide_their_kinds()
    {
        using var host = Start(height: TallHeight);

        host.Type('o');
        Assert.DoesNotContain(ListRows(host.Frame()), row => row.Contains("  orch  ", StringComparison.Ordinal));
        host.Type('a');
        host.Type('x');
        host.ClickText("[e] prompt/result");

        var rows = ListRows(host.Frame());
        Assert.Equal(10, rows.Count);
        Assert.All(rows, row => Assert.Contains("  tool    ", row, StringComparison.Ordinal));

        host.ClickText("[o] orchestrator");
        Assert.Equal(33, ListRows(host.Frame()).Count);
    }

    [Fact]
    public void F_on_a_gamma_row_keeps_the_gamma_and_run_events_and_f_again_shows_all()
    {
        using var host = Start(height: TallHeight);
        host.ClickText("[gamma] acceptance failed");
        var selected = SelectedRow(host.Frame());
        Assert.StartsWith("12:04:31", selected, StringComparison.Ordinal);

        host.Type('f');

        var frame = host.Frame();
        var events = SampleEvents();
        Assert.Contains("task: gamma", frame, StringComparison.Ordinal);
        Assert.Equal(TimelineText.PlainRows(TimelineText.Visible(events, TimelineText.AllKinds, "gamma")), ListRows(frame));
        Assert.Equal(events.Count(e => e.Group is "gamma" or "run"), ListRows(frame).Count);
        Assert.DoesNotContain("alpha worker", frame, StringComparison.Ordinal);
        Assert.Contains("12:04:31  orchestrator     orch    [gamma] acceptance failed (exit 1)", SelectedRow(frame), StringComparison.Ordinal);
        host.SaveSvg("timeline-filtered");

        host.Type('f');
        Assert.Contains("task: all", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(60, ListRows(host.Frame()).Count);
        Assert.Equal(selected, SelectedRow(host.Frame()));
    }

    [Fact]
    public void A_click_on_the_task_label_toggles_the_task_filter()
    {
        using var host = Start(height: TallHeight);
        host.ClickText("Edit src/Alpha/Parser.cs");

        host.ClickText("task: all");

        Assert.Contains("task: alpha", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("gamma worker", host.Frame(), StringComparison.Ordinal);

        host.ClickText("task: alpha");
        Assert.Contains("task: all", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(60, ListRows(host.Frame()).Count);
    }

    [Fact]
    public void Enter_opens_the_pop_up_of_a_tool_call_a_log_entry_and_a_call()
    {
        using var host = Start(height: TallHeight);
        host.ClickText("Edit src/Alpha/Parser.cs");

        host.Press(TerminalKey.Enter);

        AssertPopupTitle(host.Frame(), "Edit src/Alpha/Parser.cs");
        var popup = PopupLines(host.Frame());
        Assert.Contains("Input", popup);
        Assert.Contains("Result", popup);
        Assert.Contains("Diff", popup);
        host.SaveSvg("timeline-popup");
        host.Press(TerminalKey.Escape);
        AssertNoPopup(host.Frame());

        host.ClickText("Run started");
        host.Press(TerminalKey.Enter);
        AssertPopupTitle(host.Frame(), "Log entry");
        Assert.Contains("Run started: 5 tasks, max 2 in parallel", PopupLines(host.Frame()));
        host.Press(TerminalKey.Escape);

        host.ClickText("call 1 · claude-sonnet-4-5");
        host.Press(TerminalKey.Enter);
        AssertPopupTitle(host.Frame(), "Call 1");
        Assert.Equal(
            ["Call", "number: 1", "model: claude-sonnet-4-5", "started: 12:00:10", "context: 19.2k", "output: 640", "thinking: 210", "stop reason: tool_use", "duration: -"],
            PopupLines(host.Frame()).Where(line => line.Length > 0).Take(9));
    }

    [Fact]
    public void A_click_selects_another_row_and_a_second_click_on_it_opens_its_pop_up()
    {
        using var host = Start(height: TallHeight);

        host.ClickText("[alpha] setup: dotnet restore");

        Assert.Equal("12:00:08  orchestrator         orch    [alpha] setup: dotnet restore Sample.slnx", SelectedRow(host.Frame()));
        AssertNoPopup(host.Frame());

        host.ClickText("[alpha] setup: dotnet restore");
        AssertPopupTitle(host.Frame(), "Log entry");
    }

    [Fact]
    public void C_on_an_alpha_worker_row_shows_that_session_on_the_conversation_page()
    {
        using var host = Start(height: TallHeight);
        host.ClickText("Edit src/Alpha/Parser.cs");

        host.Type('c');

        Assert.Contains(ConversationStubPage.Prefix + SampleRun.AlphaWorkerKey, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void C_on_an_orchestrator_row_of_a_task_shows_the_tasks_last_session()
    {
        using var host = Start(height: TallHeight);
        host.ClickText("[alpha] started (fresh)");

        host.Type('c');

        Assert.Contains(ConversationStubPage.Prefix + SampleRun.AlphaReviewKey, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void C_on_a_run_row_does_nothing()
    {
        using var host = Start(height: TallHeight);
        host.Press(TerminalKey.Home);

        host.Type('c');

        Assert.DoesNotContain(ConversationStubPage.Prefix, host.Frame(), StringComparison.Ordinal);
        Assert.Equal(RunStarted, SelectedRow(host.Frame()));
    }

    [Fact]
    public void T_and_a_click_on_replay_here_replay_at_the_selected_events_time()
    {
        var context = new RecordingAppContext(SampleRun.CreateTimeline());
        using var harness = TerminalHarness.Start(new TimelinePage().Build(context), () => TerminalLoopResult.Continue);

        harness.Type('t');
        Assert.Equal([SampleRun.At(12, 29, 30)], context.Replays);

        harness.Press(TerminalKey.Home);
        harness.ClickText(TimelineText.ReplayHere);
        Assert.Equal([SampleRun.At(12, 29, 30), SampleRun.At(12, 0, 0)], context.Replays);
        Assert.Empty(context.Pages);
        Assert.Empty(context.Popups);
    }

    [Fact]
    public void Without_events_the_list_says_so_and_the_keys_do_nothing()
    {
        var context = new RecordingAppContext(RunSnapshot.Empty(@"C:\Work\Fresh") with { ReadAt = SampleRun.At(12, 30, 0) });
        using var harness = TerminalHarness.Start(new TimelinePage().Build(context), () => TerminalLoopResult.Continue);

        Assert.Contains(TimelineText.NoEvents, harness.Frame(), StringComparison.Ordinal);
        harness.Type('t');
        harness.Type('c');
        harness.Type('f');
        harness.Press(TerminalKey.Enter);
        harness.ClickText(TimelineText.ReplayHere);

        Assert.Empty(context.Replays);
        Assert.Empty(context.Pages);
        Assert.Empty(context.Popups);
        Assert.Contains("task: all", harness.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_run_shows_no_events_yet_in_the_shell()
    {
        using var host = Start(RunSnapshot.Empty(@"C:\Work\Fresh") with { ReadAt = SampleRun.At(12, 30, 0) });

        Assert.Contains(TimelineText.NoEvents, host.Frame(), StringComparison.Ordinal);
        Assert.Contains(FilterRow, host.Frame(), StringComparison.Ordinal);
        Assert.Empty(ListRows(host.Frame()));
    }

    [Fact]
    public void While_the_run_is_active_the_selection_follows_new_rows_until_Up_and_again_after_End()
    {
        var run = SampleRun.CreateTimeline();
        using var host = Start(run);

        var second = WithEntry(run, 2, 29, 45);
        host.SetSnapshot(second);
        Assert.Equal(BetaRow(29, 45), SelectedRow(host.Frame()));

        host.Press(TerminalKey.Up);
        Assert.Equal(LastRow, SelectedRow(host.Frame()));
        var third = WithEntry(second, 3, 29, 50);
        host.SetSnapshot(third);
        Assert.Equal(LastRow, SelectedRow(host.Frame()));

        host.Press(TerminalKey.End);
        Assert.Equal(BetaRow(29, 50), SelectedRow(host.Frame()));
        host.SetSnapshot(WithEntry(third, 4, 29, 55));
        Assert.Equal(BetaRow(29, 55), SelectedRow(host.Frame()));
        Assert.Equal(BetaRow(29, 55), ListRows(host.Frame())[^1]);
    }

    [Fact]
    public void A_click_on_the_last_row_follows_again()
    {
        var run = SampleRun.CreateTimeline();
        using var host = Start(run, height: TallHeight);
        host.Press(TerminalKey.Up);

        host.ClickText("Bash dotnet build src/Beta");
        host.SetSnapshot(WithEntry(run, 2, 29, 45));

        Assert.Equal(BetaRow(29, 45), SelectedRow(host.Frame()));
        AssertNoPopup(host.Frame());
    }

    [Fact]
    public void After_the_run_ended_new_rows_keep_the_selected_last_row()
    {
        var run = SampleRun.CreateTimeline();
        var finished = run with { Run = run.Run with { Phase = RunPhase.Finished } };
        using var host = Start(finished);
        Assert.Equal(LastRow, SelectedRow(host.Frame()));

        host.SetSnapshot(WithEntry(finished, 2, 29, 45));

        Assert.Equal(LastRow, SelectedRow(host.Frame()));
    }

    [Fact]
    public void A_new_snapshot_keeps_the_selected_event_by_key()
    {
        var run = SampleRun.CreateTimeline();
        using var host = Start(run, height: TallHeight);
        host.ClickText("[alpha] review passed");
        const string reviewPassed = "12:09:00  orchestrator         orch    [alpha] review passed";
        Assert.Equal(reviewPassed, SelectedRow(host.Frame()));

        host.SetSnapshot(run with { Version = 2, Sessions = run.Sessions.RemoveAll(s => s.Files.Key == SampleRun.GammaWorker1Key) });

        Assert.Equal(reviewPassed, SelectedRow(host.Frame()));
        Assert.Equal(52, ListRows(host.Frame()).Count);
    }

    [Fact]
    public void A_new_snapshot_without_the_selected_event_selects_the_last_row()
    {
        var run = SampleRun.CreateTimeline();
        using var host = Start(run, height: TallHeight);
        host.ClickText("Write src/Gamma/Formatter.cs");
        Assert.Contains("Write src/Gamma/Formatter.cs", SelectedRow(host.Frame()), StringComparison.Ordinal);

        host.SetSnapshot(run with { Version = 2, Sessions = run.Sessions.RemoveAll(s => s.Files.Key == SampleRun.GammaWorker1Key) });

        Assert.Equal(LastRow, SelectedRow(host.Frame()));
    }

    [Fact]
    public void With_sub_agents_the_rows_show_their_paths_and_the_filter_row_lists_s()
    {
        using var host = Start(SampleRun.CreateSubAgents(), height: SubAgentsHeight);
        var frame = host.Frame();

        AssertRows(SubAgentEvents(), frame);
        Assert.Contains(AlphaSub1Result, ListRows(frame));
        Assert.Contains(ListRows(frame), row => row.Contains("planner #1 › Map the repo › Read the spec", StringComparison.Ordinal));
        Assert.Contains(SubAgentFilterRow, frame, StringComparison.Ordinal);
    }

    [Fact]
    public void With_sub_agents_the_command_bar_lists_s_after_the_kind_keys()
    {
        using var host = Start(SampleRun.CreateSubAgents(), width: WideWidth);

        Assert.Contains("[e] Prompt/result | [s] Sub-agents | ", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_sub_agent_frame_shows_the_paths_of_the_planners_nested_sub_agents()
    {
        using var host = Start(SampleRun.CreateSubAgents());

        host.Press(TerminalKey.Home);

        var frame = host.Frame();
        Assert.Equal("11:59:00  planner #1                                          prompt  prompt sent · 147 chars", SelectedRow(frame));
        Assert.Contains(
            "11:59:11  planner #1 › Map the repo › Read the spec           prompt  sub-agent started · explore · 58 chars",
            ListRows(frame));
        Assert.Contains(ListRows(frame), row => row.StartsWith(
            "11:59:25  planner #1 › Map the repo › Read the spec           result  result succeeded · The spec asks for five tasks",
            StringComparison.Ordinal));
        Assert.Contains(SubAgentFilterRow, frame, StringComparison.Ordinal);
        host.SaveSvg("timeline-subagents");
    }

    [Fact]
    public void S_and_a_click_on_the_sub_agents_label_hide_and_show_the_sub_agent_rows()
    {
        using var host = Start(SampleRun.CreateSubAgents(), height: SubAgentsHeight);
        var events = SubAgentEvents();
        var own = TimelineText.Visible(events, TimelineText.AllKinds, null, subAgents: false);

        host.Type('s');

        var frame = host.Frame();
        AssertRows(own, frame);
        Assert.DoesNotContain(ListRows(frame), row => row.Contains(AgentPath.Separator, StringComparison.Ordinal));
        Assert.Contains(SubAgentFilterRow, frame, StringComparison.Ordinal);

        host.Type('s');
        AssertRows(events, host.Frame());

        host.ClickText("[s] sub-agents");
        AssertRows(own, host.Frame());
        host.ClickText("[s] sub-agents");
        AssertRows(events, host.Frame());
    }

    [Fact]
    public void The_kind_filters_apply_to_sub_agent_rows_too()
    {
        using var host = Start(SampleRun.CreateSubAgents(), height: SubAgentsHeight);

        host.Type('u');

        var withoutTools = TimelineText.Visible(SubAgentEvents(), TimelineText.Toggle(TimelineText.Filters.Single(f => f.Key == 'u'), TimelineText.AllKinds), null);
        AssertRows(withoutTools, host.Frame());
        Assert.DoesNotContain("Glob src/Alpha/**", host.Frame(), StringComparison.Ordinal);
        Assert.Contains(AlphaSub1Result, ListRows(host.Frame()));
    }

    [Fact]
    public void Without_sub_agents_the_page_has_no_sub_agents_label_command_or_key()
    {
        using var host = Start(height: TallHeight, width: WideWidth);
        Assert.Contains(FilterRow, host.Frame(), StringComparison.Ordinal);
        Assert.Contains("[e] Prompt/result | [1] Timeline", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("[s]", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("ub-agents", host.Frame(), StringComparison.Ordinal);

        host.Type('s');

        Assert.Equal(TimelineText.PlainRows(SampleEvents()), ListRows(host.Frame()));
        Assert.Equal(LastRow, SelectedRow(host.Frame()));
    }

    [Fact]
    public void C_on_a_sub_agent_row_shows_that_sub_agent_on_the_conversation_page()
    {
        using var host = Start(SampleRun.CreateSubAgents(), height: SubAgentsHeight);
        host.ClickText("Glob src/Alpha/**");
        Assert.Contains(AlphaSub1Path, SelectedRow(host.Frame()), StringComparison.Ordinal);

        host.Type('c');

        Assert.Contains(ConversationStubPage.Prefix + SampleRun.AlphaWorkerKey + "|" + SampleRun.AlphaSub1Id, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_on_a_sub_agent_result_row_opens_its_report_and_on_its_prompt_row_its_prompt()
    {
        var run = SampleRun.CreateSubAgents();
        var sub = SubAgents.Find(run.Sessions.Single(s => s.Files.Key == SampleRun.AlphaWorkerKey).Content, SampleRun.AlphaSub1Id)!;
        var context = new RecordingAppContext(run);
        using var harness = TerminalHarness.Start(new TimelinePage().Build(context), () => TerminalLoopResult.Continue, height: SubAgentsHeight);

        harness.ClickText("result succeeded · The parser module has 3 files.");
        harness.Press(TerminalKey.Enter);

        Assert.Equal(["Result"], context.Popups);
        Assert.Equal([new PopupSection("Report", "The parser module has 3 files.")], Assert.Single(context.PopupSections));

        harness.ClickText($"sub-agent started · Explore · {sub.Prompt.Length} chars");
        harness.Press(TerminalKey.Enter);

        Assert.Equal(["Result", "Prompt"], context.Popups);
        Assert.Equal([new PopupSection("Prompt", sub.Prompt)], context.PopupSections[1]);
        Assert.Empty(context.Pages);
    }

    [Fact]
    public void F_on_an_alpha_sub_agent_row_keeps_the_alpha_sub_agent_rows()
    {
        using var host = Start(SampleRun.CreateSubAgents(), height: SubAgentsHeight);
        host.ClickText("Glob src/Alpha/**");

        host.Type('f');

        var frame = host.Frame();
        Assert.Contains("task: alpha", frame, StringComparison.Ordinal);
        AssertRows(TimelineText.Visible(SubAgentEvents(), TimelineText.AllKinds, "alpha"), frame);
        Assert.Contains(ListRows(frame), row => row.Contains(AlphaSub1Path, StringComparison.Ordinal));
        Assert.Contains(ListRows(frame), row => row.Contains("alpha worker #1 › Check the public API surface of…", StringComparison.Ordinal));
        Assert.DoesNotContain("planner #1", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("beta worker", frame, StringComparison.Ordinal);
        Assert.Contains("Glob src/Alpha/**", SelectedRow(frame), StringComparison.Ordinal);
    }

    [Fact]
    public void The_wheel_scrolls_the_list()
    {
        using var host = Start();
        var frame = host.Frame();
        Assert.DoesNotContain(RunStarted, frame, StringComparison.Ordinal);

        host.Wheel(ColumnOf(frame, LastRow), RowOf(frame, LastRow), 40);

        frame = host.Frame();
        Assert.Equal([RunStarted, ClaudePath, AlphaStarted], ListRows(frame).Take(3));
        Assert.DoesNotContain(LastRow, frame, StringComparison.Ordinal);

        host.Wheel(ColumnOf(frame, RunStarted), RowOf(frame, RunStarted), -40);
        Assert.Equal(LastRow, SelectedRow(host.Frame()));
    }
}

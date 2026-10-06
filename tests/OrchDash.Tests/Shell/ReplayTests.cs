using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Shell;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.Shell;

// 32.1-32.7, 32.10 and 32.12: the time bar, the replay cursor and its keys, on SampleRun.CreateTimeline() (60 events
// from 12:00:00 to 12:29:30, read at 12:30:00).
public sealed class ReplayTests
{
    private const int BarRow = 1;
    private static readonly string Live = "12:00:00 " + new string('━', 49) + "● 12:30:00  live";

    private static IPage[] Pages() =>
    [
        new ReplayStubPage(),
        new StubPage("two", "Second", "beta page", withCommands: true),
    ];

    private static UiTestHost Start(RunSnapshot? snapshot = null, IRunHost? runs = null) =>
        UiTestHost.Start(Pages(), snapshot ?? SampleRun.CreateTimeline(), runs: runs);

    private static string[] Lines(UiTestHost host) => host.Frame().Split('\n');

    private static string Bar(UiTestHost host) => Lines(host)[BarRow];

    /// <summary>The state text of the bar: what follows the end clock.</summary>
    private static string State(UiTestHost host) => Bar(host)[TimeBarText.StateColumn..];

    private static string CommandBar(UiTestHost host) => Lines(host)[^1].Trim();

    private static void ClickCell(UiTestHost host, int cell) => host.Click(TimeBarText.BarColumn + cell, BarRow);

    [Fact]
    public void The_time_bar_is_the_row_under_the_header_and_the_tabs_follow()
    {
        using var host = Start();
        var lines = Lines(host);

        Assert.Equal("SampleRepo  Running  30m00s  2 problems  quit", lines[0].Trim());
        Assert.Equal(Live, lines[BarRow]);
        Assert.Contains("│ Replay │ │ Second │", lines[3], StringComparison.Ordinal);
        host.SaveSvg("shell-timebar");
    }

    [Fact]
    public void Left_steps_back_event_by_event_and_every_page_sees_the_run_at_T()
    {
        using var host = Start();

        host.Press(TerminalKey.Left);
        Assert.Equal("replay 12:29:30 · 60 of 60 events · git live", State(host));
        Assert.Equal("SampleRepo  Running  29m30s  2 problems  quit", Lines(host)[0].Trim());
        Assert.Contains("snapshot at 12:29:30 now 12:29:30", host.Frame(), StringComparison.Ordinal);
        Assert.StartsWith("12:00:00 " + new string('━', 48) + "●─ 12:30:00", Bar(host), StringComparison.Ordinal);
        host.SaveSvg("shell-replay");

        host.Press(TerminalKey.Left);
        Assert.Equal("replay 12:20:00 · 59 of 60 events · git live", State(host));
    }

    [Fact]
    public void Right_steps_forward_and_returns_to_live_after_the_last_event()
    {
        using var host = Start();
        host.Press(TerminalKey.Right);
        Assert.Equal(Live, Bar(host));
        host.Press(TerminalKey.Left);
        host.Press(TerminalKey.Left);

        host.Press(TerminalKey.Right);
        Assert.Equal("replay 12:29:30 · 60 of 60 events · git live", State(host));
        host.Press(TerminalKey.Right);
        Assert.Equal(Live, Bar(host));
        Assert.Contains("snapshot at 12:30:00 now 12:30:00", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Shift_Left_and_Shift_Right_move_by_a_minute_within_the_span()
    {
        using var host = Start();

        host.Press(TerminalKey.Left, TerminalModifiers.Shift);
        Assert.Equal("replay 12:29:00 · 59 of 60 events · git live", State(host));
        host.Press(TerminalKey.Left, TerminalModifiers.Shift);
        Assert.Equal("replay 12:28:00 · 59 of 60 events · git live", State(host));
        host.Press(TerminalKey.Right, TerminalModifiers.Shift);
        Assert.Equal("replay 12:29:00 · 59 of 60 events · git live", State(host));
        host.Press(TerminalKey.Right, TerminalModifiers.Shift);
        Assert.Equal(Live, Bar(host));
        host.Press(TerminalKey.Right, TerminalModifiers.Shift);
        Assert.Equal(Live, Bar(host));
    }

    [Fact]
    public void Shift_Left_stops_at_the_first_event()
    {
        using var host = Start();
        host.Type('d');
        Assert.Equal("replay 12:00:00 · 2 of 60 events · git live", State(host));

        host.Press(TerminalKey.Left, TerminalModifiers.Shift);
        Assert.Equal("replay 12:00:00 · 2 of 60 events · git live", State(host));
        host.Press(TerminalKey.Left);
        Assert.Equal("replay 12:00:00 · 2 of 60 events · git live", State(host));
    }

    [Fact]
    public void Escape_returns_to_live()
    {
        using var host = Start();
        host.Press(TerminalKey.Left);

        host.Press(TerminalKey.Escape);

        Assert.Equal(Live, Bar(host));
        Assert.Equal("SampleRepo  Running  30m00s  2 problems  quit", Lines(host)[0].Trim());
    }

    [Fact]
    public void Escape_closes_an_open_popup_and_keeps_the_replay()
    {
        using var host = Start();
        host.Type('2');
        host.Press(TerminalKey.Left);
        host.Type('o');
        Assert.Contains($"┌ {StubPage.PopupTitle} ", host.Frame(), StringComparison.Ordinal);

        host.Press(TerminalKey.Left);
        host.Press(TerminalKey.Escape);

        Assert.DoesNotContain(StubPage.PopupTitle, host.Frame(), StringComparison.Ordinal);
        Assert.Equal("replay 12:29:30 · 60 of 60 events · git live", State(host));
    }

    [Fact]
    public void A_click_on_a_cell_replays_at_its_time_and_the_last_cell_returns_to_live()
    {
        using var host = Start();

        ClickCell(host, 0);
        Assert.Equal("replay 12:00:00 · 2 of 60 events · git live", State(host));
        Assert.StartsWith("12:00:00 ●─", Bar(host), StringComparison.Ordinal);

        // 30 minutes × 10 / 49 = 6m07s.
        ClickCell(host, 10);
        Assert.StartsWith("replay 12:06:07 · ", State(host), StringComparison.Ordinal);

        ClickCell(host, 49);
        Assert.Equal(Live, Bar(host));
    }

    [Fact]
    public void A_click_on_the_live_text_returns_to_live_and_clicks_on_the_clocks_do_nothing()
    {
        using var host = Start();
        host.ClickText("live");
        Assert.Equal(Live, Bar(host));

        host.Press(TerminalKey.Left);
        host.Click(3, BarRow);
        host.Click(TimeBarText.BarColumn + 52, BarRow);
        Assert.Equal("replay 12:29:30 · 60 of 60 events · git live", State(host));

        host.ClickText("live");
        Assert.Equal(Live, Bar(host));
    }

    [Fact]
    public void Replay_from_a_page_sets_T_clamps_it_to_the_first_event_and_returns_to_live_for_null_or_after_the_end()
    {
        using var host = Start();

        host.Type('a');
        Assert.Equal("replay 12:05:00 · 24 of 60 events · git live", State(host));
        Assert.Contains("snapshot at 12:05:00 now 12:05:00", host.Frame(), StringComparison.Ordinal);
        Assert.Equal("SampleRepo  Running  5m00s  2 problems  quit", Lines(host)[0].Trim());

        host.Type('b');
        Assert.Equal(Live, Bar(host));

        host.Type('a');
        host.Type('c');
        Assert.Equal(Live, Bar(host));

        host.Type('d');
        Assert.Equal("replay 12:00:00 · 2 of 60 events · git live", State(host));
    }

    [Fact]
    public void Left_steps_while_the_page_scroll_viewer_has_the_focus_and_does_not_scroll_it()
    {
        using var host = Start();
        // The page's command reaches the page only while its scroll viewer has the focus.
        host.Type('k');
        Assert.Contains($"session [{ReplayStubPage.SessionKey}]", host.Frame(), StringComparison.Ordinal);

        host.Press(TerminalKey.Left);

        Assert.Equal("replay 12:29:30 · 60 of 60 events · git live", State(host));
        Assert.Contains(Lines(host), line => line.StartsWith("session [", StringComparison.Ordinal));
    }

    [Fact]
    public void A_new_snapshot_while_replaying_is_replayed_at_T()
    {
        var first = SampleRun.CreateTimeline();
        using var host = Start(first);
        host.Type('a');

        host.SetSnapshot(first with { ReadAt = SampleRun.At(12, 31, 0), Problems = [] });

        // 49 × 5 / 31 = 7.9: cell 8.
        Assert.Equal(
            "12:00:00 " + new string('━', 8) + "●" + new string('─', 41) + " 12:31:00  replay 12:05:00 · 24 of 60 events · git live",
            Bar(host));
        Assert.Equal("SampleRepo  Running  5m00s  quit", Lines(host)[0].Trim());
        Assert.Contains("snapshot at 12:05:00 now 12:05:00", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_of_another_run_ends_the_replay_and_clears_the_selected_session()
    {
        var first = SampleRun.CreateTimeline();
        using var host = Start(first);
        host.Type('k');
        host.Press(TerminalKey.Left);

        host.SetSnapshot(first with { RepoPath = @"C:\Work\SampleRepo.runs\20261003-110000" });

        Assert.Equal(Live, Bar(host));
        Assert.Contains("session []", host.Frame(), StringComparison.Ordinal);
        Assert.StartsWith("SampleRepo · 20261003-110000  Running  30m00s", Lines(host)[0].Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_of_the_same_run_keeps_the_selected_session()
    {
        var first = SampleRun.CreateTimeline();
        using var host = Start(first);
        host.Type('k');

        host.SetSnapshot(first with { Problems = [] });

        Assert.Contains($"session [{ReplayStubPage.SessionKey}]", host.Frame(), StringComparison.Ordinal);
        Assert.Equal("SampleRepo  Running  30m00s  quit", Lines(host)[0].Trim());
    }

    [Fact]
    public void The_header_of_an_archived_run_shows_its_stamp()
    {
        using var host = Start(SampleRun.CreateTimeline() with { RepoPath = @"C:\Work\SampleRepo.runs\20261003-120000" });

        Assert.Equal("SampleRepo · 20261003-120000  Running  30m00s  2 problems  quit", Lines(host)[0].Trim());
    }

    [Fact]
    public void The_bar_shows_what_the_run_host_is_loading_and_its_problem()
    {
        var runs = new FakeRunHost();
        using var host = Start(runs: runs);

        runs.Loading = "20261003-110000";
        runs.Problem = "The run cannot be read.";
        host.Pump();

        Assert.Equal(Live + " · loading 20261003-110000 · The run cannot be read.", Bar(host));

        runs.Loading = null;
        runs.Problem = null;
        host.Pump();
        Assert.Equal(Live, Bar(host));
    }

    [Fact]
    public void Without_events_the_bar_says_no_events_yet_and_the_keys_and_clicks_do_nothing()
    {
        using var host = Start(RunSnapshot.Empty(SampleRun.RepoPath) with { ReadAt = SampleRun.At(12, 30, 0) });
        Assert.Equal("no events yet", Bar(host));

        host.Press(TerminalKey.Left);
        host.Press(TerminalKey.Left, TerminalModifiers.Shift);
        host.Type('a');
        ClickCell(host, 0);

        Assert.Equal("no events yet", Bar(host));
        Assert.Contains("snapshot at 12:30:00 now 12:30:00", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_bar_lists_Left_always_Escape_while_replaying_and_r_with_a_run_host()
    {
        using (var host = UiTestHost.Start([new StubPage("one", "First", "alpha page"), new StubPage("two", "Second", "beta page")], SampleRun.CreateTimeline()))
        {
            Assert.Equal("[1] First | [2] Second | [Left] Step | [p] Problems | [q] Quit | [Ctrl+Q] Quit", CommandBar(host));
            host.Press(TerminalKey.Left);
            Assert.Equal("[1] First | [2] Second | [Left] Step | [Escape] Live | [p] Problems | [q] Quit | [Ctrl+Q] Quit", CommandBar(host));
            host.Press(TerminalKey.Escape);
            Assert.Equal("[1] First | [2] Second | [Left] Step | [p] Problems | [q] Quit | [Ctrl+Q] Quit", CommandBar(host));
        }

        using (var host = UiTestHost.Start([new StubPage("one", "First", "alpha page")], SampleRun.CreateTimeline(), runs: new FakeRunHost()))
        {
            Assert.Equal("[1] First | [Left] Step | [r] Runs | [p] Problems | [q] Quit | [Ctrl+Q] Quit", CommandBar(host));
            host.Press(TerminalKey.Left);
            Assert.Equal("[1] First | [Left] Step | [Escape] Live | [r] Runs | [p] Problems | [q] Quit | [Ctrl+Q] Quit", CommandBar(host));
        }
    }
}

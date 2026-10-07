using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.Shell;

public sealed class AppShellTests
{
    private const string ProblemLine = "state.json: The process cannot access the file because it is being used by another process.";

    private static IPage[] Pages(string goTo = "one") =>
    [
        new StubPage("one", "First", "alpha page"),
        new StubPage("two", "Second", "beta page", withCommands: true, goTo: goTo),
    ];

    private static string[] Lines(UiTestHost host) => host.Frame().Split('\n');

    /// <summary>Opens the stub pop-up from the second page.</summary>
    private static UiTestHost StartWithPopup()
    {
        var host = UiTestHost.Start(Pages(), SampleRun.Create());
        host.Type('2');
        host.Type('o');
        Assert.Contains(StubPage.PopupTitle, host.Frame(), StringComparison.Ordinal);
        return host;
    }

    /// <summary>The first text line inside the pop-up: the row under its title, without borders and scroll bar.</summary>
    private static string FirstPopupLine(UiTestHost host)
    {
        var lines = Lines(host);
        var titleRow = Array.FindIndex(lines, line => line.Contains("┌ ", StringComparison.Ordinal) && line.Contains("[X]", StringComparison.Ordinal));
        Assert.True(titleRow >= 0, host.Frame());
        var left = lines[titleRow].IndexOf('┌', StringComparison.Ordinal);
        var right = lines[titleRow].IndexOf('┐', StringComparison.Ordinal);
        // The column left of the right border is the scroll bar.
        return lines[titleRow + 1][(left + 1)..(right - 1)].TrimEnd();
    }

    [Fact]
    public void Frame_shows_the_header_the_tabs_the_first_page_and_the_command_bar()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());
        var lines = Lines(host);

        Assert.Equal("SampleRepo  Running  30m00s  1 problems  quit", lines[0].Trim());
        Assert.StartsWith("12:00:00 ", lines[1], StringComparison.Ordinal);
        Assert.Contains("│ First │", lines[3], StringComparison.Ordinal);
        Assert.Contains("│ Second │", lines[3], StringComparison.Ordinal);
        Assert.True(lines[3].IndexOf("First", StringComparison.Ordinal) < lines[3].IndexOf("Second", StringComparison.Ordinal));
        Assert.Contains("alpha page version 1", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("beta page", host.Frame(), StringComparison.Ordinal);
        Assert.Equal("[1] First | [2] Second | [Left] Step | [p] Problems | [q] Quit | [Ctrl+Q] Quit", lines[^1].Trim());
        host.SaveSvg("shell");
    }

    [Fact]
    public void A_digit_shows_the_page_at_that_position_and_its_keys_reach_that_page()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.Type('2');
        Assert.Contains("beta page version 1", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("alpha page", host.Frame(), StringComparison.Ordinal);
        Assert.Contains("[o] Open", host.Frame(), StringComparison.Ordinal);
        host.Type('x');
        Assert.Contains("beta page keys [x]", host.Frame(), StringComparison.Ordinal);

        host.Type('1');
        Assert.Contains("alpha page version 1", host.Frame(), StringComparison.Ordinal);
        host.Type('y');
        Assert.Contains("alpha page keys [y]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_digit_without_a_page_does_nothing()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.Type('3');

        Assert.Contains("alpha page version 1", host.Frame(), StringComparison.Ordinal);
        Assert.False(host.Exited);
    }

    [Fact]
    public void A_click_on_a_tab_shows_its_page_and_its_keys_reach_that_page()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.ClickText("Second");
        Assert.Contains("beta page version 1", host.Frame(), StringComparison.Ordinal);
        host.Type('x');
        Assert.Contains("beta page keys [x]", host.Frame(), StringComparison.Ordinal);

        host.ClickText("First");
        Assert.Contains("alpha page version 1", host.Frame(), StringComparison.Ordinal);
        host.Type('y');
        Assert.Contains("alpha page keys [y]", host.Frame(), StringComparison.Ordinal);

        host.ClickText("Second");
        host.Type('z');
        Assert.Contains("beta page keys [xz]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void ShowPage_shows_the_page_with_that_id()
    {
        using var host = UiTestHost.Start(Pages(goTo: "one"), SampleRun.Create());
        host.Type('2');

        host.Type('g');

        Assert.Contains("alpha page version 1", host.Frame(), StringComparison.Ordinal);
        host.Type('y');
        Assert.Contains("alpha page keys [y]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void ShowPage_with_an_unknown_id_does_nothing()
    {
        using var host = UiTestHost.Start(Pages(goTo: "nowhere"), SampleRun.Create());
        host.Type('2');

        host.Type('g');

        Assert.Contains("beta page version 1", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_with_a_new_version_reaches_the_page_and_a_later_ReadAt_moves_the_elapsed_time()
    {
        var first = SampleRun.Create();
        using var host = UiTestHost.Start(Pages(), first);

        host.SetSnapshot(first with { Version = 2 });
        Assert.Contains("alpha page version 2", host.Frame(), StringComparison.Ordinal);
        Assert.Contains("30m00s", Lines(host)[0], StringComparison.Ordinal);

        host.SetSnapshot(first with { Version = 2, ReadAt = SampleRun.At(12, 35, 7) });
        Assert.Contains("35m07s", Lines(host)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_snapshot_instance_with_the_same_version_is_taken()
    {
        var first = SampleRun.Create();
        using var host = UiTestHost.Start(Pages(), first);

        host.SetSnapshot(first with { Problems = [] });

        Assert.Equal("SampleRepo  Running  30m00s  quit", Lines(host)[0].Trim());
    }

    [Fact]
    public void The_popup_shows_its_title_the_close_button_and_every_heading()
    {
        using var host = StartWithPopup();
        var frame = host.Frame();

        Assert.Contains($"┌ {StubPage.PopupTitle} ", frame, StringComparison.Ordinal);
        Assert.Contains("[X] ┐", frame, StringComparison.Ordinal);
        Assert.Equal("Plain heading", FirstPopupLine(host));
        Assert.Contains("literal [bold]x[/] text", frame, StringComparison.Ordinal);
        host.SaveSvg("shell-popup");

        host.Press(TerminalKey.End);
        frame = host.Frame();
        Assert.Contains("Json heading", frame, StringComparison.Ordinal);
        Assert.Contains("  \"name\": \"stub\",", frame, StringComparison.Ordinal);
        Assert.Contains("Diff heading", frame, StringComparison.Ordinal);
        Assert.Contains("+new line", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void The_popup_takes_90_percent_of_the_screen()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create(), width: 100, height: 30);
        host.Type('2');
        host.Type('o');
        var lines = Lines(host);

        var top = Array.FindIndex(lines, line => line.Contains("[X] ┐", StringComparison.Ordinal));
        var bottom = Array.FindIndex(lines, line => line.Contains('┘', StringComparison.Ordinal));
        var left = lines[top].IndexOf('┌', StringComparison.Ordinal);
        var right = lines[top].IndexOf('┐', StringComparison.Ordinal);
        Assert.Equal(90, right - left + 1);
        Assert.Equal(27, bottom - top + 1);
    }

    [Fact]
    public void Every_scroll_key_and_the_wheel_scroll_the_popup()
    {
        using var host = StartWithPopup();

        host.Press(TerminalKey.Down);
        Assert.Equal("literal [bold]x[/] text", FirstPopupLine(host));
        host.Press(TerminalKey.Up);
        Assert.Equal("Plain heading", FirstPopupLine(host));

        host.Press(TerminalKey.PageDown);
        Assert.StartsWith("plain line ", FirstPopupLine(host), StringComparison.Ordinal);
        Assert.NotEqual("plain line 01", FirstPopupLine(host));
        host.Press(TerminalKey.PageUp);
        Assert.Equal("Plain heading", FirstPopupLine(host));

        host.Press(TerminalKey.End);
        Assert.Contains(" same line", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("Plain heading", host.Frame(), StringComparison.Ordinal);
        host.Press(TerminalKey.Home);
        Assert.Equal("Plain heading", FirstPopupLine(host));

        host.Wheel(80, 20, -3);
        Assert.Equal("plain line 02", FirstPopupLine(host));
        host.Wheel(80, 20, 1);
        Assert.Equal("plain line 01", FirstPopupLine(host));
    }

    [Fact]
    public void Escape_closes_the_popup_and_the_focus_goes_back_to_the_page()
    {
        using var host = StartWithPopup();

        host.Press(TerminalKey.Escape);

        Assert.DoesNotContain(StubPage.PopupTitle, host.Frame(), StringComparison.Ordinal);
        Assert.Contains("beta page version 1", host.Frame(), StringComparison.Ordinal);
        host.Type('w');
        Assert.Contains("beta page keys [w]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Q_closes_the_popup_without_quitting()
    {
        using var host = StartWithPopup();

        host.Type('q');

        Assert.False(host.Exited);
        Assert.DoesNotContain(StubPage.PopupTitle, host.Frame(), StringComparison.Ordinal);
        host.Type('w');
        Assert.Contains("beta page keys [w]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_the_close_button_closes_the_popup()
    {
        using var host = StartWithPopup();

        host.ClickText("[X]");

        Assert.DoesNotContain(StubPage.PopupTitle, host.Frame(), StringComparison.Ordinal);
        host.Type('w');
        Assert.Contains("beta page keys [w]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void While_the_popup_is_open_digits_p_and_clicks_outside_it_do_nothing()
    {
        using var host = StartWithPopup();

        host.Type('1');
        host.Type('p');
        host.Type('x');
        host.ClickText("Fir");
        host.ClickText("1 problems");
        host.ClickText("quit");

        Assert.False(host.Exited);
        Assert.Contains($"┌ {StubPage.PopupTitle} ", host.Frame(), StringComparison.Ordinal);
        host.Press(TerminalKey.Escape);
        Assert.Contains("beta page version 1", host.Frame(), StringComparison.Ordinal);
        Assert.Contains("beta page keys []", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_Q_exits_while_the_popup_is_open()
    {
        using var host = StartWithPopup();

        host.PressCtrl('q');

        Assert.True(host.Exited);
    }

    [Fact]
    public void A_second_popup_replaces_the_first()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());
        host.Type('2');

        host.Type('r');

        Assert.Contains("┌ Replacing popup ", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("Replaced popup", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("Old heading", host.Frame(), StringComparison.Ordinal);
        host.Press(TerminalKey.Escape);
        Assert.DoesNotContain("New heading", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("Old heading", host.Frame(), StringComparison.Ordinal);
        host.Type('w');
        Assert.Contains("beta page keys [w]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void P_opens_the_problems_popup()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.Type('p');

        Assert.Contains("┌ Problems ", host.Frame(), StringComparison.Ordinal);
        Assert.Contains(ProblemLine, host.Frame(), StringComparison.Ordinal);
        host.SaveSvg("shell-problems");
        host.Press(TerminalKey.Escape);
        host.Type('w');
        Assert.Contains("alpha page keys [w]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_the_problem_count_opens_the_problems_popup()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.ClickText("1 problems");

        Assert.Contains("┌ Problems ", host.Frame(), StringComparison.Ordinal);
        Assert.Contains(ProblemLine, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_problems_the_count_is_hidden_and_the_popup_says_so()
    {
        var snapshot = SampleRun.Create() with { Problems = [] };
        using var host = UiTestHost.Start(Pages(), snapshot);
        Assert.DoesNotContain("problems", Lines(host)[0], StringComparison.Ordinal);

        host.Type('p');

        Assert.Contains("┌ Problems ", host.Frame(), StringComparison.Ordinal);
        Assert.Contains("No problems", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Q_quits()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.Type('q');

        Assert.True(host.Exited);
    }

    [Fact]
    public void A_click_on_quit_quits()
    {
        using var host = UiTestHost.Start(Pages(), SampleRun.Create());

        host.ClickText("quit");

        Assert.True(host.Exited);
    }

    [Fact]
    public void The_header_has_no_elapsed_time_before_the_run_starts()
    {
        var snapshot = RunSnapshot.Empty(@"C:\Work\Fresh") with { ReadAt = SampleRun.At(12, 30, 0) };
        using var host = UiTestHost.Start(Pages(), snapshot);

        Assert.Equal("Fresh  NotStarted  quit", Lines(host)[0].Trim());
    }
}

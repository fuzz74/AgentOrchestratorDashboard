using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Shell;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.Shell;

// 32.8, 32.9 and N.13: the Runs dialog, opened by r or a click on the header's run text.
public sealed class RunsDialogTests
{
    private const string ArchivePath = @"C:\Work\SampleRepo.runs\20261003-110000";
    private const string HeaderRow = "run              spec     started           finished          tasks                        provider";
    private const string CurrentRow = "●  current          spec.md  2026-10-03 12:00  -                 1 done · 1 failed · 5 tasks  Claude";
    private const string ArchiveRow = "   20261003-110000  -        2026-10-03 11:00  2026-10-03 11:53  5 done · 0 failed · 5 tasks  Copilot";

    private static readonly RunCatalog Catalog = new(
    [
        new RunEntry(SampleRun.RepoPath, null, "spec.md", SampleRun.At(12, 0, 0), null, 5, 1, 1, Provider.Claude, null, null),
        new RunEntry(ArchivePath, "20261003-110000", null, SampleRun.At(11, 0, 0), SampleRun.At(11, 53, 38), 5, 5, 0, Provider.Copilot, null, null),
    ], null);

    private static UiTestHost Start(FakeRunHost runs) =>
        UiTestHost.Start([new StubPage("one", "First", "alpha page"), new StubPage("two", "Second", "beta page")], SampleRun.CreateTimeline(), runs: runs);

    private static bool IsOpen(UiTestHost host) => host.Frame().Contains("┌ Runs ", StringComparison.Ordinal);

    /// <summary>The selected row of the list, which the list marks with an arrow.</summary>
    private static string SelectedRow(UiTestHost host)
    {
        var line = host.Frame().Split('\n').Single(l => l.Contains("│→ ", StringComparison.Ordinal));
        var start = line.IndexOf("│→ ", StringComparison.Ordinal) + 3;
        return line[start..line.IndexOf('│', start)].TrimEnd();
    }

    /// <summary>Checks that the dialog is closed and that the keyboard focus is back on the page, which then has <paramref name="keys"/>.</summary>
    private static void AssertClosedWithPageFocus(UiTestHost host, string keys = "w")
    {
        Assert.False(IsOpen(host));
        Assert.False(host.Exited);
        host.Type('w');
        Assert.Contains($"alpha page keys [{keys}]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void R_refreshes_the_runs_and_opens_the_dialog_with_the_shown_run_selected()
    {
        var runs = new FakeRunHost { Runs = Catalog };
        using var host = Start(runs);

        host.Type('r');

        Assert.True(IsOpen(host));
        Assert.Equal(1, runs.Refreshes);
        var frame = host.Frame();
        Assert.Contains("   " + HeaderRow, frame, StringComparison.Ordinal);
        Assert.Contains(ArchiveRow, frame, StringComparison.Ordinal);
        Assert.Contains("[X] ┐", frame, StringComparison.Ordinal);
        Assert.Equal(CurrentRow, SelectedRow(host));
        host.SaveSvg("shell-runs");
    }

    [Fact]
    public void A_click_on_the_run_text_opens_the_dialog()
    {
        var runs = new FakeRunHost { Runs = Catalog };
        using var host = Start(runs);

        host.ClickText("SampleRepo");

        Assert.True(IsOpen(host));
        Assert.Equal(1, runs.Refreshes);
        Assert.Equal(CurrentRow, SelectedRow(host));
    }

    [Fact]
    public void Without_a_run_host_r_and_a_click_on_the_run_text_do_nothing()
    {
        using var host = UiTestHost.Start([new StubPage("one", "First", "alpha page")], SampleRun.CreateTimeline());

        host.Type('r');
        host.ClickText("SampleRepo");

        Assert.False(IsOpen(host));
        Assert.Contains("alpha page keys [r]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_says_loading_until_the_host_has_runs_and_then_shows_them()
    {
        var runs = new FakeRunHost();
        using var host = Start(runs);

        host.Type('r');
        Assert.Equal("loading…", SelectedRow(host));

        runs.Runs = Catalog;
        host.Pump();
        Assert.Equal(CurrentRow, SelectedRow(host));
        Assert.Contains(ArchiveRow, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_catalog_while_open_replaces_the_rows_and_keeps_the_selected_run()
    {
        var runs = new FakeRunHost { Runs = Catalog };
        using var host = Start(runs);
        host.Type('r');
        host.Press(TerminalKey.Down);
        Assert.Equal(ArchiveRow, SelectedRow(host));

        runs.Runs = Catalog with { Problem = @"C:\Work\SampleRepo.runs: Access denied." };
        host.Pump();

        Assert.Contains(@"C:\Work\SampleRepo.runs: Access denied.", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(ArchiveRow, SelectedRow(host));
    }

    [Fact]
    public void Enter_on_another_run_closes_the_dialog_and_switches_to_it()
    {
        var runs = new FakeRunHost { Runs = Catalog };
        using var host = Start(runs);
        host.Type('r');

        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Enter);

        Assert.Equal([ArchivePath], runs.SwitchedTo);
        AssertClosedWithPageFocus(host);
    }

    [Fact]
    public void A_click_on_another_run_closes_the_dialog_and_switches_to_it()
    {
        var runs = new FakeRunHost { Runs = Catalog };
        using var host = Start(runs);
        host.Type('r');

        host.ClickText("20261003-110000");

        Assert.Equal([ArchivePath], runs.SwitchedTo);
        AssertClosedWithPageFocus(host);
    }

    [Fact]
    public void Enter_or_a_click_on_the_shown_run_only_closes_the_dialog()
    {
        var runs = new FakeRunHost { Runs = Catalog };
        using var host = Start(runs);

        host.Type('r');
        host.Press(TerminalKey.Enter);
        AssertClosedWithPageFocus(host);

        host.Type('r');
        host.ClickText("current");
        AssertClosedWithPageFocus(host, "ww");

        Assert.Empty(runs.SwitchedTo);
    }

    [Fact]
    public void Enter_or_a_click_on_the_header_row_or_a_problem_row_does_nothing()
    {
        var runs = new FakeRunHost { Runs = Catalog with { Problem = "listing failed" } };
        using var host = Start(runs);
        host.Type('r');

        host.Press(TerminalKey.Home);
        host.Press(TerminalKey.Enter);
        host.ClickText("finished");
        host.Press(TerminalKey.End);
        host.Press(TerminalKey.Enter);
        host.ClickText("listing failed");

        Assert.True(IsOpen(host));
        Assert.Empty(runs.SwitchedTo);
    }

    [Fact]
    public void Escape_q_and_the_close_button_close_the_dialog()
    {
        var runs = new FakeRunHost { Runs = Catalog };
        using var host = Start(runs);

        host.Type('r');
        host.Press(TerminalKey.Escape);
        AssertClosedWithPageFocus(host);

        host.Type('r');
        host.Type('q');
        AssertClosedWithPageFocus(host, "ww");

        host.Type('r');
        host.ClickText("[X]");
        AssertClosedWithPageFocus(host, "www");

        Assert.Empty(runs.SwitchedTo);
        Assert.Equal(3, runs.Refreshes);
    }

    [Fact]
    public void While_the_dialog_is_open_digits_p_Left_and_clicks_outside_it_do_nothing()
    {
        var runs = new FakeRunHost { Runs = Catalog };
        using var host = Start(runs);
        host.Type('r');

        host.Type('2');
        host.Type('p');
        host.Press(TerminalKey.Left);
        host.Click(TimeBarText.BarColumn, 1);
        host.ClickText("quit");

        Assert.True(IsOpen(host));
        Assert.DoesNotContain("┌ Problems ", host.Frame(), StringComparison.Ordinal);
        host.Press(TerminalKey.Escape);
        Assert.Contains("alpha page version 1", host.Frame(), StringComparison.Ordinal);
        Assert.EndsWith("  live", host.Frame().Split('\n')[1], StringComparison.Ordinal);
    }
}

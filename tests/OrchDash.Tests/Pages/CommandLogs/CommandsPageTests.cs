using System.Text;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.CommandLogs;
using OrchDash.Shell;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.Pages.CommandLogs;

public sealed class CommandsPageTests
{
    private const string GammaAttempt3Row = "✖ 12:19:50 gamma acceptance #3 406 B";
    private const string GammaAttempt2Row = "✖ 12:12:30 gamma acceptance #2";
    private const string BetaSetupRow = "✔ 12:10:05 beta setup 202 B";
    private const string GammaAttempt3Key = "gamma/20261003-120005/attempt-3-acceptance.log";
    private const string StderrLine = "── stderr ──";

    private static UiTestHost Start(RunSnapshot? snapshot = null, int height = 45) =>
        UiTestHost.Start([new CommandsPage()], snapshot ?? SampleRun.CreateInsight(), height: height);

    [Fact]
    public void The_list_shows_every_log_in_order_and_the_output_of_the_first()
    {
        using var host = Start();

        var logs = SampleRun.CreateInsight().Commands;
        string Bytes(int i) => Look.Bytes(logs[i].Length);
        Assert.Equal(
        [
            GammaAttempt3Row,
            $"✖ 12:12:30 gamma acceptance #2 {Bytes(1)}",
            BetaSetupRow,
            $"✔ 12:09:28 alpha integration check {Bytes(3)}",
            $"✔ 12:09:26 alpha integration setup {Bytes(4)}",
            $"✔ 12:08:50 alpha acceptance #1 {Bytes(5)}",
            $"✖ 12:04:30 gamma acceptance #1 {Bytes(6)}",
            $"✔ 12:00:10 gamma setup {Bytes(7)}",
            $"✔ 12:00:08 alpha setup {Bytes(8)}",
            $"✔ 11:56:00 bootstrap bootstrap check #1 {Bytes(9)}",
            $"✔ 11:55:30 bootstrap bootstrap setup #1 {Bytes(10)}",
        ], Rows(host.Frame()));
        Assert.Equal(GammaAttempt3Row, Selected(host));

        var output = OutputLines(host.Frame());
        Assert.Equal(["dotnet test tests/Gamma", "failed (exit 1)", GammaAttempt3Key, "406 B"], output[..4]);
        Assert.Equal("Determining projects to restore...", output[4].Trim());
        var stderr = Array.IndexOf(output, StderrLine);
        Assert.True(stderr > 4, host.Frame());
        Assert.Contains(output[4..stderr],
            line => line.StartsWith("Failed!  - Failed:     1, Passed:     5", StringComparison.Ordinal));
        Assert.Equal("error: 1 test failed", output[stderr + 1]);
        Assert.Contains("[Enter] Open", host.Frame(), StringComparison.Ordinal);
        Assert.Contains("[Tab] Switch list", host.Frame(), StringComparison.Ordinal);
        host.SaveSvg("commands");
    }

    [Fact]
    public void Down_selects_the_next_log_and_shows_its_output_without_stderr()
    {
        using var host = Start();

        host.Press(TerminalKey.Down);

        Assert.StartsWith(GammaAttempt2Row, Selected(host), StringComparison.Ordinal);
        var output = OutputLines(host.Frame());
        Assert.Equal("gamma/20261003-120005/attempt-2-acceptance.log", output[2]);
        Assert.Contains(output, line => line.StartsWith("Failed!  - Failed:     2, Passed:     4", StringComparison.Ordinal));
        Assert.DoesNotContain(StderrLine, output);
    }

    [Fact]
    public void Enter_opens_the_logs_popup_and_Escape_closes_it()
    {
        using var host = Start();

        host.Press(TerminalKey.Enter);

        AssertPopup(host, "acceptance gamma #3", "Command", "Outcome", "Output", "Stderr");
        var popup = PopupLines(host);
        Assert.Contains("dotnet test tests/Gamma", popup);
        Assert.Contains("failed (exit 1)", popup);
        Assert.Contains("error: 1 test failed", popup);
        host.SaveSvg("commands-popup");

        host.Press(TerminalKey.Escape);

        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(GammaAttempt3Row, Selected(host));
    }

    [Fact]
    public void A_click_selects_a_row_and_a_click_on_the_selected_row_opens_it()
    {
        using var host = Start();

        host.ClickText("12:10:05 beta setup");

        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(BetaSetupRow, Selected(host));
        Assert.Equal(["dotnet restore Sample.slnx", "passed", "beta/20261003-121000/setup.log", "202 B"],
            OutputLines(host.Frame())[..4]);

        host.ClickText("12:10:05 beta setup");

        AssertPopup(host, "setup beta", "Command", "Outcome", "Output");
        Assert.DoesNotContain("Stderr", PopupLines(host));
        Assert.DoesNotContain("┌ setup beta #", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_moves_the_keys_to_the_output_and_back()
    {
        using var host = Start();
        Assert.DoesNotContain("[End] Follow", host.Frame(), StringComparison.Ordinal);

        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);

        Assert.Contains("[End] Follow", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(GammaAttempt3Row, Selected(host));

        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);

        Assert.DoesNotContain("[End] Follow", host.Frame(), StringComparison.Ordinal);
        Assert.StartsWith(GammaAttempt2Row, Selected(host), StringComparison.Ordinal);
    }

    [Fact]
    public void Shift_Tab_moves_the_keys_to_the_output_and_back()
    {
        var snapshot = SampleRun.CreateInsight();
        var shell = new AppShell([new CommandsPage()], () => snapshot, new FixedTimeProvider(snapshot.ReadAt));
        using var harness = TerminalHarness.Start(shell.Root, shell.OnUpdate);

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        harness.Press(TerminalKey.Down);

        Assert.Contains("[End] Follow", harness.Frame(), StringComparison.Ordinal);
        Assert.Equal(GammaAttempt3Row, Selected(harness.Frame()));

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        harness.Press(TerminalKey.Down);

        Assert.StartsWith(GammaAttempt2Row, Selected(harness.Frame()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_running_log_follows_its_output_until_the_user_scrolls_up_and_End_resumes()
    {
        using var host = Start(WithRunningOutput(1, 60), height: 20);
        Assert.Contains(OutputLine(60), OutputLines(host.Frame()));
        Assert.DoesNotContain(OutputLine(1), OutputLines(host.Frame()));

        host.SetSnapshot(WithRunningOutput(2, 80));
        Assert.Contains(OutputLine(80), OutputLines(host.Frame()));

        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Up);
        host.SetSnapshot(WithRunningOutput(3, 100));
        Assert.DoesNotContain(OutputLine(100), OutputLines(host.Frame()));
        Assert.Contains(OutputLine(79), OutputLines(host.Frame()));

        host.Press(TerminalKey.End);
        Assert.Contains(OutputLine(100), OutputLines(host.Frame()));
        host.SetSnapshot(WithRunningOutput(4, 120));
        Assert.Contains(OutputLine(120), OutputLines(host.Frame()));
    }

    [Fact]
    public void The_wheel_scrolls_the_output_up_and_stops_following()
    {
        using var host = Start(WithRunningOutput(1, 60), height: 20);
        var (x, y) = OutputOrigin(host.Frame());

        host.Wheel(x + 10, y + 8, 3);
        Assert.DoesNotContain(OutputLine(60), OutputLines(host.Frame()));
        host.SetSnapshot(WithRunningOutput(2, 80));

        Assert.DoesNotContain(OutputLine(80), OutputLines(host.Frame()));
        host.Wheel(x + 10, y + 8, -100);
        Assert.Contains(OutputLine(80), OutputLines(host.Frame()));
    }

    [Fact]
    public void A_log_that_is_not_running_shows_the_start_of_its_output()
    {
        var run = WithRunningOutput(1, 60);
        var failed = run.Commands[0] with { Outcome = CommandOutcome.Failed };
        using var host = Start(run with { Commands = run.Commands.SetItem(0, failed) }, height: 20);

        Assert.Contains(OutputLine(1), OutputLines(host.Frame()));
        Assert.DoesNotContain(OutputLine(60), OutputLines(host.Frame()));
    }

    [Fact]
    public void A_new_snapshot_keeps_the_selected_log_by_key()
    {
        using var host = Start();
        host.ClickText("12:10:05 beta setup");
        var run = SampleRun.CreateInsight();

        host.SetSnapshot(run with { Version = 2, Commands = run.Commands.RemoveAt(0) });

        Assert.Equal(BetaSetupRow, Selected(host));
        Assert.Equal(10, Rows(host.Frame()).Length);
        Assert.Equal("beta/20261003-121000/setup.log", OutputLines(host.Frame())[2]);
    }

    [Fact]
    public void Without_a_selection_or_when_the_selected_key_is_gone_the_first_log_is_selected()
    {
        using var host = Start();
        var run = SampleRun.CreateInsight();

        host.SetSnapshot(run with { Version = 2, Commands = run.Commands.RemoveAt(0) });
        Assert.StartsWith(GammaAttempt2Row, Selected(host), StringComparison.Ordinal);

        host.ClickText("12:10:05 beta setup");
        host.SetSnapshot(run with { Version = 3, Commands = run.Commands.RemoveAt(2) });
        Assert.Equal(GammaAttempt3Row, Selected(host));
    }

    [Fact]
    public void A_run_without_command_logs_shows_no_command_logs_yet()
    {
        using var host = Start();

        host.SetSnapshot(SampleRun.CreateInsight() with { Version = 2, Commands = [] });

        Assert.Equal("No command logs yet", PaneLines(host.Frame(), "Logs")[0].Trim());
        host.Press(TerminalKey.Enter);
        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
    }

    private static string OutputLine(int n) => $"output line {n:000}";

    /// <summary>The insight run whose newest log runs and has <paramref name="lines"/> lines of output.</summary>
    private static RunSnapshot WithRunningOutput(long version, int lines)
    {
        var run = SampleRun.CreateInsight();
        var text = string.Join('\n', Enumerable.Range(1, lines).Select(OutputLine));
        var running = run.Commands[0] with
        {
            Outcome = CommandOutcome.Running,
            ExitCode = null,
            Text = text,
            StderrText = "",
            Length = Encoding.UTF8.GetByteCount(text),
        };
        return run with { Version = version, Commands = run.Commands.SetItem(0, running) };
    }

    /// <summary>The cell column and row of the top left corner of the output pane.</summary>
    private static (int X, int Y) OutputOrigin(string frame)
    {
        var lines = frame.Split('\n');
        var top = Array.FindIndex(lines, line => line.Contains("┌ Output", StringComparison.Ordinal));
        Assert.True(top >= 0, frame);
        return (AnsiScreen.CellColumn(lines[top], lines[top].IndexOf("┌ Output", StringComparison.Ordinal)), top);
    }

    /// <summary>The text rows inside the pane with that title, without borders and scroll bar.</summary>
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

    /// <summary>The non-empty rows of the output pane.</summary>
    private static string[] OutputLines(string frame) => [.. PaneLines(frame, "Output").Where(line => line.Length > 0)];

    /// <summary>The non-empty rows of the list without the marker column.</summary>
    private static string[] Rows(string frame) =>
        [.. PaneLines(frame, "Logs").Where(line => line.Trim().Length > 0).Select(line => line[2..])];

    /// <summary>The selected row of the list: the row with the arrow.</summary>
    private static string Selected(string frame)
    {
        var row = PaneLines(frame, "Logs").FirstOrDefault(line => line.StartsWith('→'));
        Assert.True(row is not null, frame);
        return row[2..];
    }

    private static string Selected(UiTestHost host)
    {
        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
        return Selected(host.Frame());
    }

    /// <summary>The text rows of the open pop-up, without borders and scroll bar.</summary>
    private static string[] PopupLines(UiTestHost host)
    {
        var lines = host.Frame().Split('\n');
        var top = Array.FindIndex(lines, line => line.Contains('┌') && line.Contains("[X]", StringComparison.Ordinal));
        Assert.True(top >= 0, host.Frame());
        var left = lines[top].IndexOf('┌');
        var right = lines[top].IndexOf('┐', left);
        var rows = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > left && lines[row][left] == '│'; row++)
        {
            rows.Add(lines[row][(left + 1)..(right - 1)].TrimEnd());
        }
        return [.. rows];
    }

    /// <summary>Asserts that the pop-up with the title is open and shows the headings in that order.</summary>
    private static void AssertPopup(UiTestHost host, string title, params string[] headings)
    {
        var lines = host.Frame().Split('\n');
        Assert.Contains(lines, line => line.Contains($"┌ {title} ", StringComparison.Ordinal) && line.Contains("[X]", StringComparison.Ordinal));
        var popup = PopupLines(host);
        var positions = headings.Select(heading => Array.IndexOf(popup, heading)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0, host.Frame()));
        Assert.Equal(positions.Order(), positions);
    }
}

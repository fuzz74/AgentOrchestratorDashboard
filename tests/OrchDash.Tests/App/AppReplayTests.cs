using System.Diagnostics;
using OrchDash.App;
using OrchDash.Tests.Host;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.App;

// Spec 32 and 34 end to end: replay and the run picker in the real UI on the claude-run fixture, through AppRunner.Run
// with the fixture store folders and the command-log reader only.
public sealed class AppReplayTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // The tasks column of the Runs dialog's row of the fixture run.
    private const string CurrentRunTasks = "24 done · 0 failed · 24 tasks";

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private int Run(Action<TerminalHarness> drive) =>
        AppRunner.Run([FixtureRuns.ClaudeRepo], _temp.Path, TextWriter.Null, (root, onUpdate) =>
        {
            using var harness = TerminalHarness.Start(root, onUpdate);
            drive(harness);
            harness.Type('q');
        }, FixtureRuns.ClaudeStore, FixtureRuns.CopilotStore, FixtureRuns.CommandLogsOnly());

    /// <summary>Row 1 of the frame: the time bar.</summary>
    private static string TimeBar(TerminalHarness harness) => harness.Frame().Split('\n')[1];

    [Fact]
    public void On_every_page_Left_enters_replay_and_Escape_returns_to_live()
    {
        var bars = new List<(char Page, string Live, string Replay, string Back)>();

        var code = Run(harness =>
        {
            foreach (var page in "12345678")
            {
                harness.Type(page);
                var live = TimeBar(harness);
                harness.Press(TerminalKey.Left);
                var replay = TimeBar(harness);
                harness.Press(TerminalKey.Escape);
                bars.Add((page, live, replay, TimeBar(harness)));
            }
        });

        Assert.Equal(0, code);
        Assert.Equal(8, bars.Count);
        Assert.All(bars, bar =>
        {
            Assert.True(bar.Live.TrimEnd().EndsWith("  live", StringComparison.Ordinal), $"page {bar.Page} before Left: {bar.Live}");
            Assert.True(bar.Replay.Contains("replay ", StringComparison.Ordinal), $"page {bar.Page} after Left: {bar.Replay}");
            Assert.Contains(" events · git live", bar.Replay, StringComparison.Ordinal);
            Assert.True(bar.Back.TrimEnd().EndsWith("  live", StringComparison.Ordinal), $"page {bar.Page} after Escape: {bar.Back}");
        });
    }

    [Fact]
    public void R_opens_the_runs_dialog_with_the_current_run_and_nothing_is_written()
    {
        var before = FixtureRuns.Listing(FixtureRuns.ClaudeRepo);
        string? dialog = null, closed = null, replay = null;

        var code = Run(harness =>
        {
            harness.Type('r');
            // The listing runs on a thread-pool thread; the shell shows it on a later tick.
            var watch = Stopwatch.StartNew();
            while (!harness.Frame().Contains(CurrentRunTasks, StringComparison.Ordinal) && watch.Elapsed < Patience)
            {
                harness.Pump();
            }
            dialog = harness.Frame();
            harness.Press(TerminalKey.Escape);
            closed = harness.Frame();
            harness.Press(TerminalKey.Left);
            replay = TimeBar(harness);
        });

        Assert.Equal(0, code);
        Assert.Contains("┌ Runs ─", dialog, StringComparison.Ordinal);
        Assert.Contains("→ ●  current  spec.md  ", dialog, StringComparison.Ordinal);
        Assert.Contains(CurrentRunTasks + "  Claude", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("┌ Runs ─", closed, StringComparison.Ordinal);
        Assert.DoesNotContain(CurrentRunTasks, closed, StringComparison.Ordinal);
        Assert.Contains("replay ", replay, StringComparison.Ordinal);
        Assert.Equal(before, FixtureRuns.Listing(FixtureRuns.ClaudeRepo));
    }
}

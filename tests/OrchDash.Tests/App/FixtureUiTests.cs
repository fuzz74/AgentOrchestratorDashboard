using OrchDash.Pages.Conversation;
using OrchDash.Pages.Overview;
using OrchDash.Tests.Host;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.App;

// End to end: the snapshot of a store on each fixture in the real pages, from the overview to a conversation pop-up
// and out with q; the fixture folder is the same afterwards (N.5).
public sealed class FixtureUiTests
{
    [Fact]
    public void The_claude_run_shows_its_overview_and_conversation_and_writes_nothing() => Walk(
        FixtureRuns.ClaudeRepo, "claude",
        taskRow: @"✔  audio-synth +W2 ",
        lastLog: "11:19:15 Run finished: 24 done, 0 failed, 0 blocked",
        firstSession: "✔ bootstrap bootstrap #1 claude-opus-5-5",
        secondSession: "✔ audio-synth worker #1 claude-opus-5-5",
        secondKey: "audio-synth/20261001-104634/attempt-1-worker.json");

    [Fact]
    public void The_copilot_run_shows_its_overview_and_conversation_and_writes_nothing() => Walk(
        FixtureRuns.CopilotRepo, "copilot",
        taskRow: @"✔  e2e +W3 ",
        lastLog: "11:53:38 Run finished: 5 done, 0 failed, 0 blocked",
        firstSession: "✔ bootstrap bootstrap #1 gpt-6-sol",
        secondSession: "✔ planner planner #1 gpt-6-sol",
        secondKey: "planner-20261003-110111-1.json");

    private static void Walk(string repo, string name, string taskRow, string lastLog, string firstSession, string secondSession, string secondKey)
    {
        var before = FixtureRuns.Listing(repo);
        var snapshot = FixtureRuns.Poll(repo);

        using (var host = UiTestHost.Start([new OverviewPage(), new ConversationPage()], snapshot))
        {
            var overview = host.Frame();
            Assert.StartsWith($"{Path.GetFileName(repo)}  Finished", overview, StringComparison.Ordinal);
            Assert.Contains("Phase: Finished", overview, StringComparison.Ordinal);
            Assert.Matches(taskRow, overview);
            Assert.Contains("→ " + lastLog, overview, StringComparison.Ordinal);
            host.SaveSvg($"app-{name}-overview");

            host.Type('2');
            Assert.Contains("┌ Sessions", host.Frame(), StringComparison.Ordinal);
            Assert.Contains("→ " + firstSession, host.Frame(), StringComparison.Ordinal);
            Assert.Contains("  " + secondSession, host.Frame(), StringComparison.Ordinal);

            host.Press(TerminalKey.Down);
            Assert.Contains("→ " + secondSession, host.Frame(), StringComparison.Ordinal);
            host.Press(TerminalKey.Enter);
            Assert.Contains("[Enter] Open", host.Frame(), StringComparison.Ordinal);
            host.SaveSvg($"app-{name}-conversation");

            host.Press(TerminalKey.Enter);
            Assert.Contains("[X]", host.Frame(), StringComparison.Ordinal);
            Assert.Contains("Key: " + secondKey, host.Frame(), StringComparison.Ordinal);
            host.Press(TerminalKey.Escape);
            Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
            Assert.Contains("→ " + secondSession, host.Frame(), StringComparison.Ordinal);

            host.Type('q');
            Assert.True(host.Exited);
        }

        Assert.Equal(before, FixtureRuns.Listing(repo));
    }
}

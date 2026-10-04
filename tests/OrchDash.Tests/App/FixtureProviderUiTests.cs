using OrchDash.App;
using OrchDash.Tests.Host;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.App;

// End to end: the snapshot of a store on each fixture, with its provider stores, in the app's own pages: key 3 shows
// the Context page, key 4 the Usage page, each with a pop-up opened and closed, and q exits; the whole fixture folder,
// provider stores included, is the same afterwards (N.5).
public sealed class FixtureProviderUiTests
{
    [Fact]
    public void The_claude_run_shows_its_context_and_usage_pages() => Walk(
        FixtureRuns.ClaudeRepo, "claude",
        header: "Claude · bootstrap · claude-opus-5-5 · 8 calls · peak 52.0k · context 52.0k of 1.0M (5 %)",
        calls: 8,
        versions: "Versions: Claude Code 2.1.285 (made for 2.1.285)",
        groups: [("bootstrap", 1, 8), ("audio-synth", 2, 10)]);

    [Fact]
    public void The_copilot_run_shows_its_context_and_usage_pages() => Walk(
        FixtureRuns.CopilotRepo, "copilot",
        header: "Copilot · bootstrap · gpt-6-sol · 7 calls · peak 13.5k · context 13.5k",
        calls: 7,
        versions: "Versions: Copilot CLI 1.0.91 (made for 1.0.91)",
        groups: [("bootstrap", 1, 7), ("planner", 1, 4), ("core", 2, 11), ("count", 6, 28)]);

    /// <param name="header">The first header line of the Context page for the first session, bootstrap.</param>
    /// <param name="calls">The number of model calls of bootstrap; the page selects the last one.</param>
    /// <param name="groups">The rows of the Usage page's group table: name, sessions and calls.</param>
    private static void Walk(string repo, string name, string header, int calls, string versions,
        (string Name, int Sessions, int Calls)[] groups)
    {
        var before = FixtureRuns.Listing(FixtureRuns.Root);
        var snapshot = FixtureRuns.Poll(repo);

        using (var host = UiTestHost.Start(AppRunner.CreatePages(), snapshot))
        {
            host.Type('3');
            var context = host.Frame();
            Assert.Contains("┊" + header + "\n", context, StringComparison.Ordinal);
            Assert.Contains("┌ Context per call", context, StringComparison.Ordinal);
            Assert.Contains("┌ Calls", context, StringComparison.Ordinal);
            Assert.Contains($"│→ call {calls}  ", context, StringComparison.Ordinal);
            Assert.Contains($"┌ Make-up at call {calls} ", context, StringComparison.Ordinal);
            host.SaveSvg($"app-{name}-context");

            host.Type('s');
            Assert.Contains("┌ System prompt ", host.Frame(), StringComparison.Ordinal);
            Assert.Contains("[X]", host.Frame(), StringComparison.Ordinal);
            Assert.Contains("│Block 1, ", host.Frame(), StringComparison.Ordinal);
            host.Press(TerminalKey.Escape);
            Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
            Assert.Contains("┊" + header + "\n", host.Frame(), StringComparison.Ordinal);

            host.Type('4');
            var usage = host.Frame();
            Assert.Contains("│" + versions + " ", usage, StringComparison.Ordinal);
            Assert.Contains("┌ Groups", usage, StringComparison.Ordinal);
            for (var i = 0; i < groups.Length; i++)
            {
                var (group, sessions, groupCalls) = groups[i];
                Assert.Matches($@"│{(i == 0 ? "→" : " ")} {group} +{sessions} +{groupCalls} ", usage);
            }
            Assert.Contains("Tokens per group", usage, StringComparison.Ordinal);
            host.SaveSvg($"app-{name}-usage");

            // Enter moves from the group table to the session table, a second Enter opens the session's pop-up.
            host.Press(TerminalKey.Enter);
            host.Press(TerminalKey.Enter);
            Assert.Contains("┌ Usage: bootstrap bootstrap #1 ", host.Frame(), StringComparison.Ordinal);
            Assert.Contains("[X]", host.Frame(), StringComparison.Ordinal);
            Assert.Contains($"│calls: {calls} ", host.Frame(), StringComparison.Ordinal);
            host.Press(TerminalKey.Escape);
            Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
            Assert.Contains("│" + versions + " ", host.Frame(), StringComparison.Ordinal);

            host.Type('q');
            Assert.True(host.Exited);
        }

        Assert.Equal(before, FixtureRuns.Listing(FixtureRuns.Root));
    }
}

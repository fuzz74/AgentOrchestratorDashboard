using OrchDash.Core.Claude;
using OrchDash.Core.Copilot;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using OrchDash.Core.Store;
using OrchDash.Core.Tests.Fixtures;
using OrchDash.Core.Timeline;
using Xunit;

namespace OrchDash.Core.Tests.Timeline;

public sealed class TimelineFixtureTests
{
    [Theory]
    [InlineData("claude", 280, 102)]
    [InlineData("copilot", 82, 19)]
    public void A_fixture_run_gives_one_event_per_row_of_the_event_table(string run, int entries, int activities)
    {
        var snapshot = Read(run == "claude" ? FixturePaths.ClaudeRepo : FixturePaths.CopilotRepo);

        var events = TimelineBuilder.Build(snapshot);

        Assert.Equal(entries, snapshot.Progress.Length);
        Assert.Equal(activities, snapshot.Progress.Count(p => p.Kind == ProgressKind.Activity));
        Assert.Equal(entries - activities, events.Count(e => e.Kind == TimelineKind.Orchestrator));
        Assert.NotEmpty(snapshot.Sessions);
        foreach (var session in snapshot.Sessions)
        {
            var expected = session.StartedAt is null
                ? 0
                : 1 + session.Content.Calls.Length
                  + session.Content.Items.Count(i => i is ToolCall or AssistantText)
                  + (session.Content.Result is null ? 0 : 1);
            Assert.Equal(expected, events.Count(e => ReferenceEquals(e.Session, session)));
        }
        Assert.Equal(events.Length, events.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(events.Select(e => e.Time).Order(), events.Select(e => e.Time));
    }

    private static RunSnapshot Read(string repoPath)
    {
        using var store = new RunStore(repoPath, new RunFolderReader(),
            (p, w) => p == Provider.Claude ? new ClaudeSessionParser(w) : new CopilotSessionParser(w));
        store.Poll();
        return store.Current;
    }
}

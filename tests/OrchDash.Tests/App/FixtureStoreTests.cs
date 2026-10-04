using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.App;

// End to end through RunStore with the real reader, parsers and provider stores on the fixture store folders: the
// "facts tests can rely on" of spec 4.3, and N.5.
// Where the notes of earlier tasks found a fact in the spec that differs from the files, the files win (see below).
public sealed class FixtureStoreTests
{
    private const string AudioSynthWorker = "audio-synth/20261001-104634/attempt-1-worker.json";
    private const string CoreWorker = "core/20261003-113444/attempt-1-worker.json";

    [Fact]
    public void The_claude_run_gives_the_known_facts()
    {
        var s = FixtureRuns.Poll(FixtureRuns.ClaudeRepo);

        Assert.Equal(24, s.Tasks.Length);
        Assert.All(s.Tasks, t => Assert.Equal(TaskState.Done, t.Status));
        Assert.Equal(RunPhase.Finished, s.Run.Phase);
        Assert.Equal(new DateTime(2026, 10, 1, 10, 42, 1), s.Run.StartedAt?.DateTime);
        Assert.Equal(new DateTime(2026, 10, 1, 11, 19, 15), s.Run.FinishedAt?.DateTime);
        Assert.Equal(10, s.Run.MaxParallel);
        Assert.Equal(Provider.Claude, s.Run.Provider);
        Assert.Equal(3, s.Sessions.Length);
        Assert.Equal(2, TaskOf(s, "audio-synth").Wave);

        var worker = SessionOf(s, AudioSynthWorker);
        Assert.Equal(SessionState.Succeeded, worker.State);
        Assert.Equal(Provider.Claude, worker.Provider);
        Assert.Equal(8, worker.Content.Calls.Length);
        var tools = worker.Content.Items.OfType<ToolCall>().ToArray();
        Assert.Equal(13, tools.Length);
        // The spec says 5 tool calls with a diff; the log has one: the Edit of SynthTests.cs. The 4 Write calls
        // create files and have an empty structuredPatch (notes of the fixtures and claude-log tasks).
        var diff = Assert.Single(tools, t => t.Result?.Diff is not null);
        Assert.Equal("Edit", diff.Name);

        AssertCommonFacts(s, FixtureRuns.ClaudeRepo);
    }

    [Fact]
    public void The_copilot_run_gives_the_known_facts()
    {
        var s = FixtureRuns.Poll(FixtureRuns.CopilotRepo);

        Assert.Equal(5, s.Tasks.Length);
        Assert.All(s.Tasks, t => Assert.Equal(TaskState.Done, t.Status));
        Assert.Equal(3, TaskOf(s, "e2e").Wave);
        Assert.Equal(RunPhase.Finished, s.Run.Phase);
        Assert.Equal(new DateTime(2026, 10, 3, 11, 49, 55), s.Run.StartedAt?.DateTime);
        Assert.Equal(new DateTime(2026, 10, 3, 11, 53, 38), s.Run.FinishedAt?.DateTime);
        Assert.Equal(3, s.Run.MaxParallel);
        Assert.Equal(Provider.Copilot, s.Run.Provider);
        Assert.Equal(10, s.Sessions.Length);
        Assert.Equal(2, s.Sessions.Count(x => x.Files.Role == AgentRole.Resolver));
        Assert.Equal(2, TaskOf(s, "count").SyncRuns);

        var worker = SessionOf(s, CoreWorker);
        Assert.Equal(Provider.Copilot, worker.Provider);
        Assert.Equal(10, worker.Content.Calls.Length);
        Assert.Equal(13, worker.Content.Items.OfType<ToolCall>().Count());

        AssertCommonFacts(s, FixtureRuns.CopilotRepo);
    }

    [Fact]
    public void A_second_poll_of_a_fixture_keeps_version_1()
    {
        foreach (var repo in new[] { FixtureRuns.ClaudeRepo, FixtureRuns.CopilotRepo })
        {
            using var store = FixtureRuns.CreateStore(repo);
            store.Poll();
            var first = store.Current;

            store.Poll();

            Assert.Equal(1, store.Current.Version);
            Assert.Same(first, store.Current);
        }
    }

    [Fact]
    public void Starting_a_store_on_the_fixtures_writes_no_file()
    {
        // N.5: the whole fixture folder, run folders and provider stores alike, so also no SQLite -shm, -wal or journal.
        var before = FixtureRuns.Listing(FixtureRuns.Root);

        using (var claudeStore = FixtureRuns.CreateStore(FixtureRuns.ClaudeRepo, TimeSpan.FromMilliseconds(20)))
        using (var copilotStore = FixtureRuns.CreateStore(FixtureRuns.CopilotRepo, TimeSpan.FromMilliseconds(20)))
        {
            claudeStore.Start();
            copilotStore.Start();
            Thread.Sleep(200);
            Assert.Equal(1, claudeStore.Current.Version);
            Assert.Equal(1, copilotStore.Current.Version);
            Assert.All(claudeStore.Current.Sessions, x => Assert.NotEmpty(x.Stores.SystemPrompt));
            Assert.All(copilotStore.Current.Sessions, x => Assert.NotEmpty(x.Stores.Calls));
        }

        Assert.Equal(before, FixtureRuns.Listing(FixtureRuns.Root));
    }

    private static void AssertCommonFacts(RunSnapshot s, string repo)
    {
        Assert.Equal(1, s.Version);
        Assert.Equal(repo, s.RepoPath);
        Assert.Empty(s.Problems);
        Assert.All(s.Sessions, x => Assert.Equal(0, x.Content.UnparsedLines));
        Assert.All(s.Sessions, x => Assert.Empty(x.Unavailable));

        // Tasks by Wave, then Id (ordinal); sessions by StartedAt (null last), then Files.Key (ordinal).
        Assert.Equal(
            s.Tasks.OrderBy(t => t.Wave).ThenBy(t => t.Id, StringComparer.Ordinal).Select(t => t.Id),
            s.Tasks.Select(t => t.Id));
        Assert.Equal(
            s.Sessions.OrderBy(x => x.StartedAt is null).ThenBy(x => x.StartedAt).ThenBy(x => x.Files.Key, StringComparer.Ordinal).Select(x => x.Files.Key),
            s.Sessions.Select(x => x.Files.Key));
    }

    private static TaskView TaskOf(RunSnapshot s, string id) => Assert.Single(s.Tasks, t => t.Id == id);

    private static Session SessionOf(RunSnapshot s, string key) => Assert.Single(s.Sessions, x => x.Files.Key == key);
}

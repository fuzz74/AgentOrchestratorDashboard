using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using OrchDash.Core.Tests.Fixtures;
using Xunit;

namespace OrchDash.Core.Tests.RunFolder;

public sealed class RunFolderReaderFixtureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Claude_run_facts()
    {
        var data = new RunFolderReader().Read(FixturePaths.ClaudeRunDir, Now);

        Assert.Empty(data.Problems);
        Assert.Equal(RunPhase.Finished, data.Run.Phase);
        Assert.Equal(new DateTime(2026, 10, 1, 10, 42, 1), data.Run.StartedAt!.Value.DateTime);
        Assert.Equal(new DateTime(2026, 10, 1, 11, 19, 15), data.Run.FinishedAt!.Value.DateTime);
        Assert.Equal((10, Provider.Claude, false), (data.Run.MaxParallel, data.Run.Provider, data.Run.StopRequested));

        Assert.Equal((".orchestrator/spec.md", "main", "orch/integration"),
            (data.Plan!.Spec, data.Plan.BaseBranch, data.Plan.IntegrationBranch));
        Assert.Equal("3", data.Plan.Settings["maxAttempts"]);
        Assert.Equal("\"claude-opus-5-5\"", data.Plan.Settings["model"]);

        Assert.Equal(24, data.Tasks.Length);
        Assert.All(data.Tasks, task => Assert.Equal(TaskState.Done, task.Status));
        Assert.All(data.Tasks, task => Assert.Matches(@"^[0-9]+\.[0-9]{2} USD, [0-9]+ attempt\(s\)$", task.Detail));
        var audioSynth = Assert.Single(data.Tasks, task => task.Id == "audio-synth");
        Assert.Equal(2, audioSynth.Wave);
        Assert.Equal(1, data.Tasks.Single(task => task.Id == "contracts").Wave);

        Assert.Equal(3, data.Sessions.Length);
        Assert.Equal(
            [AgentRole.Reviewer, AgentRole.Worker],
            data.Sessions.Where(s => s.TaskId == "audio-synth" && s.StartFolder == "20261001-104634").Select(s => s.Role));
        Assert.Equal(AgentRole.Bootstrap, data.Sessions.Single(s => s.TaskId is null).Role);
    }

    [Fact]
    public void Copilot_run_facts()
    {
        var data = new RunFolderReader().Read(FixturePaths.CopilotRunDir, Now);

        Assert.Empty(data.Problems);
        Assert.Equal(RunPhase.Finished, data.Run.Phase);
        Assert.Equal(3, data.Progress.Count(entry => entry.Source is null && entry.Message.StartsWith("Run started: ", StringComparison.Ordinal)));
        Assert.Equal(new DateTime(2026, 10, 3, 11, 49, 55), data.Run.StartedAt!.Value.DateTime);
        Assert.Equal(new DateTime(2026, 10, 3, 11, 53, 38), data.Run.FinishedAt!.Value.DateTime);
        Assert.Equal((3, Provider.Copilot), (data.Run.MaxParallel, data.Run.Provider));

        Assert.Equal(
            [("core", 1, 4), ("count", 2, 1), ("find", 2, 1), ("freq", 2, 1), ("e2e", 3, 0)],
            data.Tasks.Select(task => (task.Id, task.Wave, task.Dependents)));
        Assert.All(data.Tasks, task => Assert.Equal(TaskState.Done, task.Status));
        Assert.Equal(["core", "count", "find", "freq"], data.Tasks.Single(task => task.Id == "e2e").Deps);
        var count = data.Tasks.Single(task => task.Id == "count");
        Assert.Equal(2, count.SyncRuns);
        Assert.Equal("0.00 USD, 1 attempt(s)", count.Detail);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 11, 51, 26, TimeSpan.FromHours(2)).AddTicks(2930734), count.StartedAt);

        Assert.Equal(10, data.Sessions.Length);
        Assert.Equal(2, data.Sessions.Count(s => s.TaskId == "core"));
        var countSessions = data.Sessions.Where(s => s.TaskId == "count").ToArray();
        Assert.Equal(6, countSessions.Length);
        Assert.Equal(["20261003-114955", "20261003-115046", "20261003-115126"], countSessions.Select(s => s.StartFolder).Distinct());
        Assert.Equal(2, countSessions.Count(s => s.Role == AgentRole.Resolver));
    }

    [Fact]
    public void Reading_a_fixture_changes_no_file()
    {
        var before = FileFacts(FixturePaths.Root);

        new RunFolderReader().Read(FixturePaths.ClaudeRunDir, Now);
        new RunFolderReader().Read(FixturePaths.ClaudeRunDir, Now);
        new RunFolderReader().Read(FixturePaths.CopilotRunDir, Now);
        new RunFolderReader().Read(FixturePaths.CopilotRunDir, Now);

        Assert.Equal(before, FileFacts(FixturePaths.Root));
    }

    private static List<(string Path, long Size, DateTime LastWriteUtc)> FileFacts(string root) =>
        [.. new DirectoryInfo(root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(file => (Path.GetRelativePath(root, file.FullName), file.Length, file.LastWriteTimeUtc))
            .OrderBy(fact => fact.Item1, StringComparer.Ordinal)];
}

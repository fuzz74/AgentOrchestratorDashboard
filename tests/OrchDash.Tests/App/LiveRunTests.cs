using System.Diagnostics;
using OrchDash.App;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.App;

// N.2 with the real reader and parsers and the default poll interval: an appended log line and a changed state.json
// are in the store's snapshot within 2 seconds. The provider stores are on empty temp folders.
public sealed class LiveRunTests : IDisposable
{
    private const string WorkerKey = "alpha/20261003-120000/attempt-1-worker.json";
    private static readonly string FixtureEvents = Path.Combine(
        FixtureRuns.ClaudeRepo, ".orchestrator", "logs", "audio-synth", "20261001-104634", "attempt-1-worker.json.events.jsonl");

    private readonly TempFolder _repo = new();
    private readonly TempFolder _stores = new();

    public void Dispose()
    {
        _repo.Dispose();
        _stores.Dispose();
    }

    [Fact]
    public async Task An_appended_line_and_a_changed_state_are_published_within_two_seconds()
    {
        // The first lines of a real Claude worker log: a title change, the commands, init, then a Read and a Glob call.
        var lines = File.ReadLines(FixtureEvents).Take(6).ToArray();
        Assert.Contains("\"name\":\"Glob\"", lines[5], StringComparison.Ordinal);
        _repo.Write(".orchestrator/tasks.json", """{ "tasks": [ { "id": "alpha", "title": "Alpha" } ] }""");
        _repo.Write(".orchestrator/state.json", State("running"));
        _repo.Write(".orchestrator/progress.md", "2026-10-03 12:00:00  Run started: 1 tasks, max 1 in parallel\n");
        _repo.Write($".orchestrator/logs/{WorkerKey}.prompt.md", "Build alpha.\n");
        var events = _repo.Write($".orchestrator/logs/{WorkerKey}.events.jsonl", string.Concat(lines[..5].Select(line => line + "\r\n")));

        using var store = AppRunner.CreateStore(_repo.Path, claudeDir: _stores.Folder("claude"), copilotDir: _stores.Folder("copilot"));
        store.Start();
        Assert.Equal(TaskState.Running, Alpha(store.Current).Status);
        var session = Assert.Single(store.Current.Sessions);
        Assert.Equal(WorkerKey, session.Files.Key);
        Assert.Equal(Provider.Claude, session.Provider);
        Assert.Equal("Build alpha.\n", session.Prompt);
        Assert.Equal(["Read"], ToolNames(store.Current));

        File.AppendAllText(events, lines[5] + "\r\n");
        _repo.Write(".orchestrator/state.json", State("done"));
        var watch = Stopwatch.StartNew();
        while (!HasBoth(store.Current) && watch.Elapsed < TimeSpan.FromSeconds(4))
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        watch.Stop();

        Assert.Equal(["Read", "Glob"], ToolNames(store.Current));
        Assert.Equal(TaskState.Done, Alpha(store.Current).Status);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    private static string State(string status) =>
        $$"""{ "tasks": { "alpha": { "status": "{{status}}", "mode": "fresh", "attempts": 1, "costUsd": 0.5 } } }""";

    private static TaskView Alpha(RunSnapshot s) => Assert.Single(s.Tasks, t => t.Id == "alpha");

    private static string[] ToolNames(RunSnapshot s) =>
        [.. s.Sessions.SelectMany(x => x.Content.Items).OfType<ToolCall>().Select(t => t.Name)];

    private static bool HasBoth(RunSnapshot s) =>
        ToolNames(s).Contains("Glob") && s.Tasks.Any(t => t.Id == "alpha" && t.Status == TaskState.Done);
}

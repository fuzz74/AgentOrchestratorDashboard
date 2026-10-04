using OrchDash.Core.Claude;
using OrchDash.Core.Model;
using OrchDash.Core.Tests.Fixtures;
using OrchDash.Core.Transcript;
using Xunit;

namespace OrchDash.Core.Tests.Transcript;

/// <summary>The three transcripts of claude-run against the 'claude-run session' table of spec 4.3.</summary>
public sealed class ClaudeTranscriptFixtureTests
{
    private const string BootstrapId = "60e2b369-7dd9-4eeb-b389-cdadd402e942";
    private const string WorkerId = "66a6a33c-01ca-42bf-85bb-4eec9505a991";
    private const string ReviewId = "210c86fa-485b-48c1-8808-6dac62e28c71";

    private static readonly string ProjectsDir = Path.Combine(FixturePaths.ClaudeStore, "projects");

    public static TheoryData<string, string, string, int, long, long, long, int, int, int, double, int, int> Sessions => new()
    {
        { BootstrapId, @"C:\Data\AI\AnsiDemo", "bootstrap-20261001-100433/attempt-1.json.events.jsonl",
            8, 44589, 51999, 5361, 11, 15, 20, 0.5906472, 1, 1 },
        { WorkerId, @"C:\Data\AI\AnsiDemo.worktrees\audio-synth", "audio-synth/20261001-104634/attempt-1-worker.json.events.jsonl",
            8, 46798, 67408, 17093, 11, 15, 18, 0.8224428, 602, 32 },
        { ReviewId, @"C:\Data\AI\AnsiDemo.worktrees\audio-synth", "audio-synth/20261001-104634/attempt-1-review-1.json.events.jsonl",
            2, 43206, 45873, 3333, 11, 28, 7, 0.3348476, 0, 0 },
    };

    [Theory]
    [MemberData(nameof(Sessions))]
    public void Transcript_matches_the_session_table_with_its_work_dir_and_by_search(
        string sessionId, string workDir, string log,
        int calls, long firstContext, long lastContext, long outputSum,
        int blocks, int tools, int injected, double cost, int added, int removed)
    {
        var logCallIds = ParseLog(log).Calls.Select(call => call.Id).ToArray();

        foreach (var dir in new[] { workDir, null })
        {
            var data = new ClaudeTranscriptStore(ProjectsDir).Read(sessionId, dir);

            Assert.NotNull(data);
            Assert.Equal(calls, data.Calls.Length);
            Assert.Equal(firstContext, data.Calls[0].Usage.Context);
            Assert.Equal(lastContext, data.Calls[^1].Usage.Context);
            Assert.Equal(outputSum, data.Calls.Sum(call => call.Usage.Output ?? 0));
            Assert.Equal(blocks, data.SystemPrompt.Length);
            Assert.Equal(tools, data.Tools.Length);
            Assert.Equal(injected, data.Injected.Length);
            Assert.Equal(cost, data.CostUsd);
            Assert.Equal(added, data.LinesAdded);
            Assert.Equal(removed, data.LinesRemoved);
            Assert.Equal("2.1.285", data.CliVersion);
            Assert.Equal(0, data.UnparsedLines);
            Assert.Equal(logCallIds, data.Calls.Select(call => call.CallId));
        }
    }

    [Fact]
    public void Worker_figures_and_tools_have_their_details()
    {
        var data = new ClaudeTranscriptStore(ProjectsDir).Read(WorkerId, @"C:\Data\AI\AnsiDemo.worktrees\audio-synth");

        Assert.NotNull(data);
        var first = data.Calls[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 46, 40, 513, TimeSpan.Zero), first.Time);
        Assert.Equal(new TokenUsage(2, 17465, 29331, 179), first.Usage);
        Assert.Equal(24, first.ThinkingTokens);
        Assert.Equal("tool_use", first.StopReason);
        Assert.All(data.Calls, call => Assert.Null(call.NanoAiu));
        Assert.All(data.Calls, call => Assert.Null(call.Duration));

        Assert.All(data.SystemPrompt, block => Assert.NotEmpty(block));
        var bash = Assert.Single(data.Tools, tool => tool.Name == "Bash");
        Assert.NotNull(bash.Description);
        Assert.NotNull(bash.SchemaJson);
        Assert.StartsWith("{\n  \"", bash.SchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', bash.SchemaJson);
        Assert.All(data.Injected, item => Assert.Contains(item.Role, new[] { "system", "user" }));
        Assert.All(data.Injected, item => Assert.NotNull(item.Time));
    }

    [Fact]
    public void Search_finds_nothing_for_an_unknown_id()
    {
        Assert.Null(new ClaudeTranscriptStore(ProjectsDir).Read("00000000-0000-0000-0000-000000000000", null));
    }

    private static SessionContent ParseLog(string relativePath)
    {
        var parser = new ClaudeSessionParser(null);
        foreach (var line in File.ReadAllLines(Path.Combine(FixturePaths.ClaudeRunDir, "logs", relativePath)))
            parser.AddLine(line);
        return parser.Build();
    }
}

using OrchDash.Core.Claude;
using OrchDash.Core.Model;
using OrchDash.Core.Tests.Fixtures;
using Xunit;

namespace OrchDash.Core.Tests.Claude;

public sealed class ClaudeFixtureTests
{
    private const string WorkerLog = "audio-synth/20261001-104634/attempt-1-worker.json.events.jsonl";
    private const string ReviewLog = "audio-synth/20261001-104634/attempt-1-review-1.json.events.jsonl";
    private const string BootstrapLog = "bootstrap-20261001-100433/attempt-1.json.events.jsonl";

    private static string[] ReadLines(string relativePath) =>
        File.ReadAllLines(Path.Combine(FixturePaths.ClaudeRunDir, "logs", relativePath));

    private static SessionContent ParseFile(string relativePath)
    {
        var parser = new ClaudeSessionParser(null);
        foreach (var line in ReadLines(relativePath))
            parser.AddLine(line);
        return parser.Build();
    }

    [Fact]
    public void Worker_log_has_calls_tool_calls_diffs_and_a_worker_report()
    {
        var content = ParseFile(WorkerLog);

        Assert.Equal(0, content.UnparsedLines);
        Assert.Equal("66a6a33c-01ca-42bf-85bb-4eec9505a991", content.SessionId);
        Assert.Equal("claude-opus-5-5", content.Model);
        Assert.Equal(@"C:\Data\AI\AnsiDemo.worktrees\audio-synth", content.Init?.Cwd);
        Assert.Equal(8, content.Calls.Length);
        Assert.All(content.Calls, call => Assert.NotNull(call.Usage));

        var toolCalls = content.Items.OfType<ToolCall>().ToList();
        Assert.Equal(13, toolCalls.Count);
        Assert.All(toolCalls, call => Assert.NotNull(call.Result));
        // Spec 4.3 says 5 tool calls have a diff, but the 4 Write calls have an empty structuredPatch:
        // only the single Edit has one.
        var withDiff = Assert.Single(toolCalls, call => call.Result?.Diff is not null);
        Assert.Equal("Edit tests/AnsiDemo.Tests/Audio/SynthTests.cs", withDiff.Summary);
        Assert.StartsWith("@@ -215,38 +215,6 @@\n", withDiff.Result?.Diff, StringComparison.Ordinal);
        Assert.Contains(toolCalls, call => call.Summary == "Read src/AnsiDemo/Contracts/Audio.cs");
        Assert.Contains(toolCalls, call => call.Summary == "StructuredOutput reporting the result");

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 46, 40, 513, TimeSpan.Zero), content.FirstEventAt);
        var result = Assert.IsType<SessionResult>(content.Result);
        Assert.False(result.IsError);
        Assert.Equal("success", result.Subtype);
        Assert.Equal(14, result.Turns);
        Assert.Equal(0.8224428, result.CostUsd);
        Assert.Equal(1000000, result.ContextWindow);
        Assert.Equal("done", result.Worker?.Status);
        Assert.Null(result.Review);
    }

    [Fact]
    public void Review_log_has_a_review()
    {
        var content = ParseFile(ReviewLog);

        Assert.Equal(0, content.UnparsedLines);
        var review = Assert.IsType<ReviewVerdict>(content.Result?.Review);
        Assert.Equal("pass", review.SpecVerdict);
        Assert.Equal("pass", review.QualityVerdict);
        Assert.Empty(review.Issues);
    }

    [Fact]
    public void Bootstrap_log_parses_and_has_a_result()
    {
        var content = ParseFile(BootstrapLog);

        Assert.Equal(0, content.UnparsedLines);
        Assert.NotNull(content.Result);
        Assert.NotEmpty(content.Calls);
        Assert.NotEmpty(content.Items);
    }

    [Theory]
    [InlineData(WorkerLog)]
    [InlineData(ReviewLog)]
    [InlineData(BootstrapLog)]
    public void Building_after_every_line_gives_the_same_content_as_one_build(string relativePath)
    {
        var lines = ReadLines(relativePath);
        var parser = new ClaudeSessionParser(null);
        var previous = parser.Build();

        foreach (var line in lines)
        {
            parser.AddLine(line);
            var current = parser.Build();

            Assert.True(current.Calls.Length >= previous.Calls.Length);
            Assert.True(current.Items.Length >= previous.Items.Length);
            Assert.True(current.UnparsedLines >= previous.UnparsedLines);
            previous = current;
        }

        var once = ParseFile(relativePath);
        Assert.Equal(once.Items, previous.Items);
        Assert.Equal(once.Calls, previous.Calls);
        Assert.Equal(once.Result, previous.Result);
        Assert.Equal(once.SessionId, previous.SessionId);
        Assert.Equal(once.Model, previous.Model);
        Assert.Equal(once.FirstEventAt, previous.FirstEventAt);
        Assert.Equal(once.LastEventAt, previous.LastEventAt);
        Assert.Equal(once.UnparsedLines, previous.UnparsedLines);
    }
}

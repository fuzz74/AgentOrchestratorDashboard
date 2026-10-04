using OrchDash.Core.Copilot;
using OrchDash.Core.Model;
using OrchDash.Core.Tests.Fixtures;
using Xunit;

namespace OrchDash.Core.Tests.Copilot;

/// <summary>The parser on the events files of the copied Copilot run (tests/Fixtures/copilot-run).</summary>
public sealed class CopilotFixtureTests
{
    private const string CoreWorker = "core/20261003-113444/attempt-1-worker.json.events.jsonl";
    private const string CoreReview = "core/20261003-113444/attempt-1-review-1.json.events.jsonl";
    private const string Planner = "planner-20261003-110111-1.json.events.jsonl";
    private const string Bootstrap = "bootstrap-20261003-110028/attempt-1.json.events.jsonl";
    private const string Resolver = "count/20261003-115046/attempt-1-resolver.json.events.jsonl";

    /// <summary>The work dir the core sessions ran in; only used to make paths relative.</summary>
    private const string CoreWorkDir = @"C:\Data\AI\TextKit.worktrees\core";

    [Theory]
    [InlineData(CoreWorker)]
    [InlineData(CoreReview)]
    [InlineData(Planner)]
    [InlineData(Bootstrap)]
    [InlineData(Resolver)]
    public void Building_after_every_line_gives_the_same_content_as_one_build(string file)
    {
        var lines = ReadLines(file);
        var incremental = new CopilotSessionParser(CoreWorkDir);
        var previous = incremental.Build();

        foreach (var line in lines)
        {
            incremental.AddLine(line);
            var current = incremental.Build();
            Assert.True(current.Calls.Length >= previous.Calls.Length);
            Assert.True(current.Items.Length >= previous.Items.Length);
            Assert.True(current.UnparsedLines >= previous.UnparsedLines);
            Assert.True(CountResults(current) >= CountResults(previous));
            previous = current;
        }

        var all = Parse(file, CoreWorkDir);
        Assert.Equal(all.Calls, previous.Calls);
        Assert.Equal(all.Items, previous.Items);
        Assert.Equal(all.Result?.Text, previous.Result?.Text);
        Assert.Equal(all.UnparsedLines, previous.UnparsedLines);
        Assert.Equal(all.LastEventAt, previous.LastEventAt);
    }

    [Theory]
    [InlineData(CoreWorker)]
    [InlineData(CoreReview)]
    [InlineData(Planner)]
    [InlineData(Bootstrap)]
    [InlineData(Resolver)]
    public void Every_session_parses_and_has_a_result(string file)
    {
        var content = Parse(file, null);

        Assert.Equal(0, content.UnparsedLines);
        Assert.NotNull(content.Result);
        Assert.False(content.Result.IsError);
        Assert.Equal("success", content.Result.Subtype);
        Assert.NotNull(content.SessionId);
        Assert.NotNull(content.Model);
        Assert.NotNull(content.FirstEventAt);
        Assert.Equal(content.Calls.Length, content.Result.Turns);
    }

    [Fact]
    public void Core_worker_has_its_calls_tool_calls_and_worker_report()
    {
        var content = Parse(CoreWorker, CoreWorkDir);

        Assert.Equal("gpt-6-sol", content.Model);
        Assert.Equal("ae783abf-0989-4988-88c1-089deac14062", content.SessionId);
        Assert.Equal(10, content.Calls.Length);
        Assert.All(content.Calls, call => Assert.Equal("gpt-6-sol", call.Model));
        var tools = content.Items.OfType<ToolCall>().ToList();
        Assert.Equal(13, tools.Count);
        Assert.All(tools, tool => Assert.NotNull(tool.Result));
        Assert.All(tools, tool => Assert.NotNull(tool.CallId));
        Assert.Contains(tools, tool => tool.Summary == "powershell git status --short; dotnet --version");
        Assert.Contains(tools, tool => tool.Summary == "view src/TextKit/Program.cs");
        Assert.Contains(tools, tool => tool.Result?.Diff is not null);
        Assert.Contains(tools, tool => tool.Result?.ExitCode == 0);

        var result = Assert.IsType<SessionResult>(content.Result);
        Assert.Equal("done", result.Worker?.Status);
        Assert.Null(result.Review);
        Assert.NotNull(result.StructuredJson);
        Assert.Equal(10, result.Turns);
        Assert.Equal(TimeSpan.FromMilliseconds(52974), result.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(41112), result.ApiDuration);
        Assert.Equal(1, result.PremiumRequests);
        Assert.Equal(445, result.LinesAdded);
        Assert.Equal(23, result.LinesRemoved);
    }

    [Fact]
    public void Core_review_has_a_verdict()
    {
        var content = Parse(CoreReview, CoreWorkDir);

        var review = Assert.IsType<ReviewVerdict>(content.Result?.Review);
        Assert.Equal("pass", review.SpecVerdict);
        Assert.Equal("pass", review.QualityVerdict);
        Assert.NotEmpty(review.Summary);
        Assert.Null(content.Result?.Worker);
    }

    [Fact]
    public void Resolver_answers_in_plain_text()
    {
        var content = Parse(Resolver, null);

        Assert.StartsWith("Resolved and staged both conflicts.", content.Result?.Text, StringComparison.Ordinal);
        Assert.Null(content.Result?.StructuredJson);
    }

    private static string[] ReadLines(string file) =>
        File.ReadAllLines(Path.Combine(FixturePaths.CopilotRunDir, "logs", file));

    private static SessionContent Parse(string file, string? workDir)
    {
        var parser = new CopilotSessionParser(workDir);
        foreach (var line in ReadLines(file))
            parser.AddLine(line);
        return parser.Build();
    }

    private static int CountResults(SessionContent content) =>
        content.Items.OfType<ToolCall>().Count(tool => tool.Result is not null);
}

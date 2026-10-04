using System.Globalization;
using System.Text.Json;
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
    private const string CountWorker = "count/20261003-114955/attempt-1-worker.json.events.jsonl";
    private const string CountReview1 = "count/20261003-114955/attempt-1-review-1.json.events.jsonl";
    private const string CountReview2 = "count/20261003-115046/attempt-1-review-1.json.events.jsonl";
    private const string Resolver2 = "count/20261003-115126/attempt-1-resolver.json.events.jsonl";
    private const string CountReview3 = "count/20261003-115126/attempt-1-review-1.json.events.jsonl";

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

    /// <summary>The checkpoint table of spec 4.3 (copilot-run sessions): the last context, nano-AIU, tool tokens and tools, segment sum.</summary>
    [Theory]
    [InlineData(Bootstrap, 13490, 6476050000, 1094, 3, 4254)]
    [InlineData(Planner, 10478, 5862990000, 203, 1, 3210)]
    [InlineData(CoreWorker, 19104, 12930990000, 1996, 8, 4321)]
    [InlineData(CoreReview, 14097, 3732100000, 680, 3, 3282)]
    [InlineData(CountWorker, 16607, 8824860000, 1996, 8, 4325)]
    [InlineData(CountReview1, 13048, 3942330000, 680, 3, 3279)]
    [InlineData(Resolver, 9925, 4100460000, 1996, 8, 4324)]
    [InlineData(CountReview2, 13120, 4239890000, 680, 3, 3284)]
    [InlineData(Resolver2, 10396, 4708110000, 1996, 8, 4320)]
    [InlineData(CountReview3, 13129, 3979320000, 680, 3, 3286)]
    public void Every_session_has_a_sent_prompt_and_its_checkpoint(string file, long promptTokens, long nanoAiu,
        long toolTokens, int tools, long segmentSum)
    {
        var content = Parse(file, null);

        Assert.NotNull(content.SentPrompt);
        Assert.StartsWith("<current_datetime>", content.SentPrompt, StringComparison.Ordinal);
        var checkpoint = Assert.IsType<ContextCheckpoint>(content.Checkpoint);
        Assert.Equal(promptTokens, checkpoint.PromptTokens);
        Assert.Equal(nanoAiu, checkpoint.NanoAiu);
        Assert.Equal(toolTokens, checkpoint.ToolTokens);
        Assert.Equal(tools, checkpoint.ToolNames.Length);
        Assert.Equal(19, checkpoint.SystemSegments.Length);
        Assert.Equal(segmentSum, checkpoint.SystemSegments.Sum(segment => segment.Tokens));
        Assert.Equal(1, checkpoint.PremiumRequests);
    }

    [Fact]
    public void Core_worker_checkpoint_has_the_tool_names_in_order()
    {
        var checkpoint = Parse(CoreWorker, null).Checkpoint;

        Assert.Equal(
            ["powershell", "read_powershell", "stop_powershell", "list_powershell", "apply_patch", "view", "rg", "glob"],
            checkpoint?.ToolNames);
        Assert.Equal(new TokenPart("customized_identity_preamble", 17), checkpoint?.SystemSegments[0]);
    }

    /// <summary>Spec 10.4: without its user.message and checkpoint lines a log gives the content it gave before 10.2 and 10.3.</summary>
    [Theory]
    [InlineData(Bootstrap)]
    [InlineData(Planner)]
    [InlineData(CoreWorker)]
    [InlineData(CoreReview)]
    [InlineData(CountWorker)]
    [InlineData(CountReview1)]
    [InlineData(Resolver)]
    [InlineData(CountReview2)]
    [InlineData(Resolver2)]
    [InlineData(CountReview3)]
    public void Without_prompt_and_checkpoint_lines_the_content_is_the_same_and_has_neither(string file)
    {
        var lines = ReadLines(file);
        var without = lines.Where(line => LineType(line) is not ("user.message" or "session.usage_checkpoint")).ToArray();
        Assert.Equal(lines.Length - 2, without.Length);

        var whole = Parse(file, CoreWorkDir);
        var parser = new CopilotSessionParser(CoreWorkDir);
        foreach (var line in without)
            parser.AddLine(line);
        var content = parser.Build();

        Assert.Equal(whole.Calls, content.Calls);
        Assert.Equal(whole.Items, content.Items);
        Assert.Equal(whole.Result, content.Result);
        Assert.Equal(whole.Model, content.Model);
        Assert.Equal(whole.SessionId, content.SessionId);
        Assert.Equal(whole.Init, content.Init);
        // The files differ from the spec here: in every log the user.message is the first line that is not
        // ephemeral, so its time was the log's FirstEventAt before this change as well, and without it the
        // first kept line after it gives FirstEventAt.
        var userMessage = lines.Single(line => LineType(line) == "user.message");
        Assert.Equal(Timestamp(userMessage), whole.FirstEventAt);
        Assert.Equal(without.Where(line => !IsEphemeral(line)).Select(Timestamp).First(time => time is not null),
            content.FirstEventAt);
        Assert.Equal(whole.LastEventAt, content.LastEventAt);
        Assert.Equal(whole.UnparsedLines, content.UnparsedLines);
        Assert.Null(content.SentPrompt);
        Assert.Null(content.Checkpoint);
        Assert.Null(content.RateLimit);
    }

    private static string? LineType(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;
    }

    private static bool IsEphemeral(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.TryGetProperty("ephemeral", out var ephemeral) && ephemeral.ValueKind == JsonValueKind.True;
    }

    private static DateTimeOffset? Timestamp(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.TryGetProperty("timestamp", out var timestamp)
            ? DateTimeOffset.Parse(timestamp.GetString()!, CultureInfo.InvariantCulture)
            : null;
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

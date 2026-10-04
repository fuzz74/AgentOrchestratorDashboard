using OrchDash.Core.Copilot;
using OrchDash.Core.Model;
using OrchDash.Core.Tests.Fixtures;
using OrchDash.Core.UsageDb;
using Xunit;

namespace OrchDash.Core.Tests.UsageDb;

/// <summary>The fixture's session-store.db against the 'copilot-run session' table of spec 4.3.</summary>
public sealed class CopilotUsageFixtureTests
{
    private const string CoreWorkerId = "ae783abf-0989-4988-88c1-089deac14062";

    private static readonly string DatabasePath = Path.Combine(FixturePaths.CopilotStore, "session-store.db");

    /// <summary>The ten session ids: the folder names under session-state.</summary>
    private static readonly string[] SessionIds = Directory
        .GetDirectories(Path.Combine(FixturePaths.CopilotStore, "session-state"))
        .Select(Path.GetFileName)
        .OfType<string>()
        .ToArray();

    public static TheoryData<string, string, int, long, long, long, long> Sessions => new()
    {
        { "1ebef052-3d86-4b0f-abd5-111868a6de34", "bootstrap-20261003-110028/attempt-1.json.events.jsonl",
            7, 11200, 13490, 1619, 6476050000 },
        { "d92e413e-38cc-401c-8118-611e46e77210", "planner-20261003-110111-1.json.events.jsonl",
            4, 9432, 10478, 2666, 5862990000 },
        { CoreWorkerId, "core/20261003-113444/attempt-1-worker.json.events.jsonl",
            10, 12069, 19104, 5267, 12930990000 },
        { "93a2aa4b-22ca-410a-bef8-d28ca63c86ae", "core/20261003-113444/attempt-1-review-1.json.events.jsonl",
            1, 14097, 14097, 208, 3732100000 },
        { "f7dfd185-96f7-4770-99c9-5d660aba6c5c", "count/20261003-114955/attempt-1-worker.json.events.jsonl",
            9, 12164, 16607, 2366, 8824860000 },
        { "632962e6-b77b-473f-8159-fa68fc99acba", "count/20261003-114955/attempt-1-review-1.json.events.jsonl",
            2, 11097, 13048, 458, 3942330000 },
        { "483087e5-0b46-4aa4-ad51-a9cb81de2f9d", "count/20261003-115046/attempt-1-resolver.json.events.jsonl",
            5, 6423, 9925, 975, 4100460000 },
        { "c61fb851-7815-4f6a-9f37-9e82a52e3ece", "count/20261003-115046/attempt-1-review-1.json.events.jsonl",
            3, 11114, 13120, 503, 4239890000 },
        { "905a692e-5150-4700-8ea7-ac94558036db", "count/20261003-115126/attempt-1-resolver.json.events.jsonl",
            7, 6419, 10396, 1105, 4708110000 },
        { "9db7bfa4-3063-4400-93f0-97f8a68e0d91", "count/20261003-115126/attempt-1-review-1.json.events.jsonl",
            2, 11134, 13129, 474, 3979320000 },
    };

    [Fact]
    public void Database_has_the_schema_version_and_50_rows_of_the_ten_sessions()
    {
        var rows = new CopilotUsageReader(DatabasePath).Read(SessionIds);

        Assert.Equal(10, SessionIds.Length);
        Assert.Equal(TestedVersions.CopilotSchema, rows.SchemaVersion);
        Assert.Null(rows.Problem);
        Assert.Equal(SessionIds.Order(StringComparer.Ordinal), rows.BySession.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(50, rows.BySession.Values.Sum(calls => calls.Length));
    }

    [Theory]
    [MemberData(nameof(Sessions))]
    public void Session_rows_match_the_session_table_and_the_model_calls_of_its_log(
        string sessionId, string log, int count, long firstContext, long lastContext, long outputSum, long nanoAiu)
    {
        var rows = new CopilotUsageReader(DatabasePath).Read(SessionIds);
        var content = ParseLog(log);

        Assert.Equal(sessionId, content.SessionId);
        var calls = rows.BySession[sessionId];
        Assert.Equal(count, calls.Length);
        Assert.Equal(firstContext, calls[0].Usage.Context);
        Assert.Equal(lastContext, calls[^1].Usage.Context);
        Assert.Equal(outputSum, calls.Sum(call => call.Usage.Output ?? 0));
        Assert.Equal(nanoAiu, calls.Sum(call => call.NanoAiu ?? 0));
        Assert.Equal(content.Calls.Length, calls.Length);
        Assert.All(calls, call => Assert.Null(call.CallId));
    }

    [Fact]
    public void Every_log_of_copilot_run_belongs_to_one_of_the_ten_sessions()
    {
        var logIds = Directory
            .GetFiles(Path.Combine(FixturePaths.CopilotRunDir, "logs"), "*.events.jsonl", SearchOption.AllDirectories)
            .Select(path => ParseLog(Path.GetRelativePath(Path.Combine(FixturePaths.CopilotRunDir, "logs"), path)).SessionId)
            .Order(StringComparer.Ordinal);

        Assert.Equal(SessionIds.Order(StringComparer.Ordinal), logIds);
    }

    [Fact]
    public void First_core_worker_row_has_the_cached_part_split_off_the_input()
    {
        var first = new CopilotUsageReader(DatabasePath).Read([CoreWorkerId]).BySession[CoreWorkerId][0];

        Assert.Equal(new TokenUsage(3, 0, 12066, 191), first.Usage);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 9, 34, 53, 34, TimeSpan.Zero), first.Time);
        Assert.NotNull(first.ThinkingTokens);
        Assert.NotNull(first.Duration);
        Assert.NotNull(first.StopReason);
    }

    [Fact]
    public void Reading_the_fixture_changes_and_creates_no_file()
    {
        var before = ListFiles(FixturePaths.CopilotStore);

        var rows = new CopilotUsageReader(DatabasePath).Read(SessionIds);

        Assert.Null(rows.Problem);
        Assert.Equal(before, ListFiles(FixturePaths.CopilotStore));
    }

    private static string[] ListFiles(string root) => Directory
        .GetFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => new FileInfo(path))
        .Select(info => $"{Path.GetRelativePath(root, info.FullName)} {info.Length} {info.LastWriteTimeUtc:O}")
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static SessionContent ParseLog(string relativePath)
    {
        var parser = new CopilotSessionParser(null);
        foreach (var line in File.ReadAllLines(Path.Combine(FixturePaths.CopilotRunDir, "logs", relativePath)))
            parser.AddLine(line);
        return parser.Build();
    }
}

using OrchDash.Core.Model;
using OrchDash.Core.UsageDb;
using Xunit;

namespace OrchDash.Core.Tests.UsageDb;

/// <summary>The usage reader on databases the test builds (spec 13.1-13.6).</summary>
public sealed class CopilotUsageReaderTests
{
    private const string SessionA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string SessionB = "bbbbbbbb-0000-0000-0000-000000000002";

    [Fact]
    public void Rows_are_mapped_with_nulls_and_both_time_formats()
    {
        using var database = new TempDatabase();
        database.Insert(SessionA, "2026-10-03T09:34:53.034Z", 12069, 0, 12066, 191,
            reasoning: 22, nanoAiu: 2944850000, durationMs: 2932, finishReason: "tool_calls");
        database.Insert(SessionA, "2026-10-03 09:34:53", null, null, null, null);
        database.Insert(SessionA, null, 10, 8, 5, 0);
        database.Insert(SessionA, "2026-10-03 09:35:00", null, 5, null, 7);

        var rows = new CopilotUsageReader(database.DatabasePath).Read([SessionA]);

        Assert.Null(rows.Problem);
        Assert.Equal(8, rows.SchemaVersion);
        Assert.Equal(
            [
                new CallFigures(null, new DateTimeOffset(2026, 10, 3, 9, 34, 53, 34, TimeSpan.Zero),
                    new TokenUsage(3, 0, 12066, 191), 22, 2944850000, TimeSpan.FromMilliseconds(2932), "tool_calls"),
                new CallFigures(null, new DateTimeOffset(2026, 10, 3, 9, 34, 53, TimeSpan.Zero),
                    new TokenUsage(0, 0, 0, null), null, null, null, null),
                new CallFigures(null, null, new TokenUsage(0, 8, 5, 0), null, null, null, null),
                new CallFigures(null, new DateTimeOffset(2026, 10, 3, 9, 35, 0, TimeSpan.Zero),
                    new TokenUsage(0, 5, 0, 7), null, null, null, null),
            ],
            rows.BySession[SessionA]);
        Assert.All(rows.BySession[SessionA].Select(call => call.Time).OfType<DateTimeOffset>(),
            time => Assert.Equal(TimeSpan.Zero, time.Offset));
    }

    [Fact]
    public void Rows_are_grouped_by_session_in_query_order_and_ids_without_rows_are_absent()
    {
        using var database = new TempDatabase();
        database.Insert(SessionA, "2026-10-03T10:00:00.000Z", 100, 0, 0, 1);
        database.Insert(SessionB, "2026-10-03T10:00:01.000Z", 200, 0, 0, 2);
        database.Insert(SessionA, "2026-10-03T10:00:02.000Z", 300, 0, 0, 3);
        database.Insert("cccccccc-0000-0000-0000-000000000003", "2026-10-03T10:00:03.000Z", 400, 0, 0, 4);

        var rows = new CopilotUsageReader(database.DatabasePath).Read([SessionA, "not-in-the-database"]);

        Assert.Same(StringComparer.Ordinal, rows.BySession.KeyComparer);
        Assert.Equal([SessionA], rows.BySession.Keys);
        Assert.Equal([100L, 300L], rows.BySession[SessionA].Select(call => call.Usage.Context));
    }

    [Fact]
    public void Rows_only_in_the_wal_file_are_read_and_a_later_commit_is_in_the_next_read()
    {
        using var database = new TempDatabase(wal: true);
        database.Insert(SessionA, "2026-10-03T10:00:00.000Z", 100, 0, 0, 1);
        Assert.True(new FileInfo(database.WalPath).Length > 0);
        var reader = new CopilotUsageReader(database.DatabasePath);

        var first = reader.Read([SessionA]);
        database.Insert(SessionA, "2026-10-03T10:00:01.000Z", 200, 0, 0, 2);
        var second = reader.Read([SessionA]);

        Assert.Null(first.Problem);
        Assert.Equal(8, first.SchemaVersion);
        Assert.Equal([100L], first.BySession[SessionA].Select(call => call.Usage.Context));
        Assert.Null(second.Problem);
        Assert.Equal([100L, 200L], second.BySession[SessionA].Select(call => call.Usage.Context));
    }

    [Fact]
    public void Unchanged_files_and_ids_give_the_same_instance_without_opening_the_database()
    {
        using var database = new TempDatabase();
        database.Insert(SessionA, "2026-10-03T10:00:00.000Z", 100, 0, 0, 1);
        database.CloseWriter();
        var reader = new CopilotUsageReader(database.DatabasePath);

        var first = reader.Read([SessionA, SessionB]);
        UsageRows second;
        using (new FileStream(database.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            second = reader.Read([SessionB, SessionA, SessionA]);

        Assert.Same(first, second);
        Assert.Null(second.Problem);
    }

    [Fact]
    public void A_commit_or_another_id_set_gives_a_new_read()
    {
        using var database = new TempDatabase();
        database.Insert(SessionA, "2026-10-03T10:00:00.000Z", 100, 0, 0, 1);
        var reader = new CopilotUsageReader(database.DatabasePath);

        var first = reader.Read([SessionA]);
        var same = reader.Read([SessionA]);
        database.Insert(SessionA, "2026-10-03T10:00:01.000Z", 200, 0, 0, 2);
        var afterCommit = reader.Read([SessionA]);
        var otherIds = reader.Read([SessionA, SessionB]);

        Assert.Same(first, same);
        Assert.NotSame(first, afterCommit);
        Assert.Equal(2, afterCommit.BySession[SessionA].Length);
        Assert.NotSame(afterCommit, otherIds);
        Assert.Equal(afterCommit.BySession[SessionA], otherIds.BySession[SessionA]);
    }

    [Fact]
    public void No_ids_give_the_empty_rows_without_touching_the_file_system()
    {
        using var database = new TempDatabase();

        Assert.Same(UsageRows.Empty, new CopilotUsageReader(database.DatabasePath).Read([]));
        Assert.Same(UsageRows.Empty, new CopilotUsageReader(database.MissingPath).Read([]));
        Assert.False(File.Exists(database.MissingPath));
    }

    [Fact]
    public void A_missing_database_gives_no_rows_and_a_problem()
    {
        using var database = new TempDatabase();

        var rows = new CopilotUsageReader(database.MissingPath).Read([SessionA]);

        Assert.Empty(rows.BySession);
        Assert.Null(rows.SchemaVersion);
        Assert.Equal($"Copilot database not found: {database.MissingPath}", rows.Problem);
        Assert.False(File.Exists(database.MissingPath));
    }

    [Fact]
    public void A_file_that_is_not_a_database_gives_a_problem_before_any_good_read()
    {
        using var database = new TempDatabase();
        database.CloseWriter();
        File.WriteAllText(database.DatabasePath, NotADatabase);

        var rows = new CopilotUsageReader(database.DatabasePath).Read([SessionA]);

        Assert.Empty(rows.BySession);
        Assert.Null(rows.SchemaVersion);
        Assert.NotNull(rows.Problem);
        Assert.StartsWith("Copilot database: ", rows.Problem, StringComparison.Ordinal);
        Assert.True(rows.Problem.Length > "Copilot database: ".Length);
    }

    [Fact]
    public void A_file_that_is_not_a_database_gives_the_last_good_rows_with_a_problem()
    {
        using var database = new TempDatabase();
        database.Insert(SessionA, "2026-10-03T10:00:00.000Z", 100, 0, 0, 1);
        database.CloseWriter();
        var reader = new CopilotUsageReader(database.DatabasePath);
        var good = reader.Read([SessionA]);

        File.WriteAllText(database.DatabasePath, NotADatabase);
        var rows = reader.Read([SessionA]);

        Assert.Null(good.Problem);
        Assert.Same(good.BySession, rows.BySession);
        Assert.Equal(8, rows.SchemaVersion);
        Assert.NotNull(rows.Problem);
        Assert.StartsWith("Copilot database: ", rows.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_database_without_schema_version_gives_the_rows_without_a_version_or_problem()
    {
        using var database = new TempDatabase(schemaVersion: false);
        database.Insert(SessionA, "2026-10-03T10:00:00.000Z", 100, 0, 0, 1);

        var rows = new CopilotUsageReader(database.DatabasePath).Read([SessionA]);

        Assert.Null(rows.SchemaVersion);
        Assert.Null(rows.Problem);
        Assert.Single(rows.BySession[SessionA]);
    }

    private static string NotADatabase { get; } = string.Concat(Enumerable.Repeat("This is not a database.\n", 100));
}

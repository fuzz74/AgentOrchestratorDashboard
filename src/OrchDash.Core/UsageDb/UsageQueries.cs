using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using OrchDash.Core.Model;

namespace OrchDash.Core.UsageDb;

/// <summary>The two SELECT statements of the usage row table in spec 4.3 and the mapping of their rows.</summary>
internal static class UsageQueries
{
    private const string SchemaVersionSql = "SELECT MAX(version) FROM schema_version;";

    private const string RowsSql =
        "SELECT session_id, created_at, input_tokens, cache_read_tokens, cache_write_tokens, output_tokens, " +
        "reasoning_tokens, total_nano_aiu, duration_ms, finish_reason, agent_id, parent_tool_call_id " +
        "FROM assistant_usage_events WHERE session_id IN ({0}) ORDER BY id;";

    // 13.6: a failing schema version query gives null.
    public static int? TrySchemaVersion(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = SchemaVersionSql;
            return command.ExecuteScalar() is long version and >= int.MinValue and <= int.MaxValue
                ? (int)version
                : null;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    /// <summary>The rows of the ids, grouped by session in query order; ids without rows are absent.</summary>
    public static ImmutableDictionary<string, ImmutableArray<CallFigures>> Rows(
        SqliteConnection connection, ImmutableArray<string> ids)
    {
        using var command = connection.CreateCommand();
        var names = new StringBuilder();
        for (int i = 0; i < ids.Length; i++)
        {
            var name = "@id" + i.ToString(CultureInfo.InvariantCulture);
            names.Append(i == 0 ? name : ", " + name);
            command.Parameters.AddWithValue(name, ids[i]);
        }
        command.CommandText = string.Format(CultureInfo.InvariantCulture, RowsSql, names);

        var bySession = new Dictionary<string, ImmutableArray<CallFigures>.Builder>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var sessionId = reader.GetString(0);
                if (!bySession.TryGetValue(sessionId, out var calls))
                    bySession[sessionId] = calls = ImmutableArray.CreateBuilder<CallFigures>();
                calls.Add(Map(reader));
            }
        }

        return bySession.ToImmutableDictionary(
            pair => pair.Key, pair => pair.Value.ToImmutable(), StringComparer.Ordinal);
    }

    // Copilot's input_tokens is the whole prompt including the cached part; NULL counts as 0 except for the output.
    // agent_id and parent_tool_call_id tell a sub-agent's rows apart (36.2).
    private static CallFigures Map(SqliteDataReader reader)
    {
        long input = Int64(reader, 2) ?? 0;
        long cacheRead = Int64(reader, 3) ?? 0;
        long cacheWrite = Int64(reader, 4) ?? 0;
        var usage = new TokenUsage(Math.Max(0, input - cacheRead - cacheWrite), cacheRead, cacheWrite, Int64(reader, 5));
        var duration = Int64(reader, 8);

        return new CallFigures(
            CallId: null,
            Time: reader.IsDBNull(1) ? null : ParseTime(reader.GetString(1)),
            Usage: usage,
            ThinkingTokens: Int64(reader, 6),
            NanoAiu: Int64(reader, 7),
            Duration: duration is null ? null : TimeSpan.FromMilliseconds(duration.Value),
            StopReason: Text(reader, 9))
        {
            AgentId = Text(reader, 10),
            ParentToolCallId = Text(reader, 11),
        };
    }

    private static long? Int64(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    // created_at is UTC, as "2026-10-03T09:34:53.034Z" or as SQLite's "2026-10-03 09:34:53".
    private static DateTimeOffset? ParseTime(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            ? time
            : null;
}

using Microsoft.Data.Sqlite;

namespace OrchDash.Tests.App;

/// <summary>
/// A Copilot <c>session-store.db</c> in WAL mode with the fixture's <c>schema_version</c> (row 8) and
/// <c>assistant_usage_events</c> tables, written through a writer connection that stays open until dispose, so that
/// committed rows stay in the <c>-wal</c> file as while Copilot runs.
/// </summary>
internal sealed class UsageDatabase : IDisposable
{
    // The fixture's table, without the reference to its sessions table.
    private const string UsageEventsTable = """
        CREATE TABLE assistant_usage_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id TEXT NOT NULL,
            turn_index INTEGER,
            agent_id TEXT,
            parent_tool_call_id TEXT,
            model TEXT NOT NULL,
            copilot_usage_model TEXT,
            input_tokens INTEGER,
            output_tokens INTEGER,
            cache_read_tokens INTEGER,
            cache_write_tokens INTEGER,
            reasoning_tokens INTEGER,
            total_nano_aiu INTEGER,
            request_multiplier REAL,
            duration_ms INTEGER,
            time_to_first_token_ms INTEGER,
            output_ttft_ms REAL,
            inter_token_latency_ms INTEGER,
            initiator TEXT,
            api_endpoint TEXT,
            reasoning_effort TEXT,
            finish_reason TEXT,
            content_filter_triggered INTEGER,
            token_details_json TEXT,
            created_at TEXT DEFAULT (datetime('now'))
        )
        """;

    private readonly SqliteConnection _writer;

    /// <summary>Creates the database at <paramref name="path"/>, whose folder must exist.</summary>
    public UsageDatabase(string path)
    {
        _writer = new SqliteConnection($"Data Source={path};Pooling=False");
        _writer.Open();
        Execute("PRAGMA journal_mode=WAL");
        Execute("CREATE TABLE schema_version (version INTEGER NOT NULL)");
        Execute("INSERT INTO schema_version (version) VALUES (8)");
        Execute(UsageEventsTable);
    }

    /// <summary>Commits one usage row of <paramref name="sessionId"/>; <paramref name="input"/> includes the cached part, as Copilot writes it.</summary>
    public void Commit(string sessionId, string createdAt, long input, long cacheRead, long cacheWrite, long output)
    {
        using var command = _writer.CreateCommand();
        command.CommandText = """
            INSERT INTO assistant_usage_events (session_id, model, created_at, input_tokens, cache_read_tokens,
                cache_write_tokens, output_tokens, reasoning_tokens, total_nano_aiu, duration_ms, finish_reason)
            VALUES (@session, 'gpt-6-sol', @created, @input, @cacheRead, @cacheWrite, @output, 0, 1000000000, 1500, 'tool_calls')
            """;
        command.Parameters.AddWithValue("@session", sessionId);
        command.Parameters.AddWithValue("@created", createdAt);
        command.Parameters.AddWithValue("@input", input);
        command.Parameters.AddWithValue("@cacheRead", cacheRead);
        command.Parameters.AddWithValue("@cacheWrite", cacheWrite);
        command.Parameters.AddWithValue("@output", output);
        command.ExecuteNonQuery();
    }

    /// <summary>Closes the writer and clears the pools, so that the folder of the database can be deleted.</summary>
    public void Dispose()
    {
        _writer.Dispose();
        SqliteConnection.ClearAllPools();
    }

    private void Execute(string sql)
    {
        using var command = _writer.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

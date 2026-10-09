using Microsoft.Data.Sqlite;

namespace OrchDash.Core.Tests.UsageDb;

/// <summary>
/// A <c>session-store.db</c> under the temp folder with the fixture's <c>schema_version</c> and
/// <c>assistant_usage_events</c> tables, written through a writer connection that stays open until
/// <see cref="CloseWriter"/>. The folder is deleted on dispose.
/// </summary>
internal sealed class TempDatabase : IDisposable
{
    private const string SchemaVersionTable =
        "CREATE TABLE schema_version (version INTEGER NOT NULL)";

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

    private readonly string _root = Path.Combine(Path.GetTempPath(), "OrchDash.Tests", Guid.NewGuid().ToString("N"));
    private SqliteConnection? _writer;

    /// <param name="wal">Switch the database to WAL mode first, so that the writer's commits stay in the -wal file.</param>
    /// <param name="schemaVersion">Create <c>schema_version</c> with the row 8.</param>
    public TempDatabase(bool wal = false, bool schemaVersion = true)
    {
        Directory.CreateDirectory(_root);
        DatabasePath = Path.Combine(_root, "session-store.db");

        _writer = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        _writer.Open();
        if (wal)
            Execute("PRAGMA journal_mode=WAL");
        if (schemaVersion)
        {
            Execute(SchemaVersionTable);
            Execute("INSERT INTO schema_version (version) VALUES (8)");
        }
        Execute(UsageEventsTable);
    }

    public string DatabasePath { get; }

    public string WalPath => DatabasePath + "-wal";

    /// <summary>A path in the same folder that does not exist.</summary>
    public string MissingPath => Path.Combine(_root, "missing.db");

    /// <summary>Commits one row of <c>assistant_usage_events</c>; a null value is stored as NULL.</summary>
    public void Insert(string sessionId, string? createdAt, long? input, long? cacheRead, long? cacheWrite, long? output,
        long? reasoning = null, long? nanoAiu = null, long? durationMs = null, string? finishReason = null,
        string? agentId = null, string? parentToolCallId = null)
    {
        using var command = Writer().CreateCommand();
        command.CommandText = """
            INSERT INTO assistant_usage_events (session_id, model, created_at, input_tokens, cache_read_tokens,
                cache_write_tokens, output_tokens, reasoning_tokens, total_nano_aiu, duration_ms, finish_reason,
                agent_id, parent_tool_call_id)
            VALUES (@session, 'gpt-6-sol', @created, @input, @cacheRead, @cacheWrite, @output, @reasoning, @aiu,
                @duration, @finish, @agent, @parent)
            """;
        command.Parameters.AddWithValue("@session", sessionId);
        command.Parameters.AddWithValue("@created", (object?)createdAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@input", (object?)input ?? DBNull.Value);
        command.Parameters.AddWithValue("@cacheRead", (object?)cacheRead ?? DBNull.Value);
        command.Parameters.AddWithValue("@cacheWrite", (object?)cacheWrite ?? DBNull.Value);
        command.Parameters.AddWithValue("@output", (object?)output ?? DBNull.Value);
        command.Parameters.AddWithValue("@reasoning", (object?)reasoning ?? DBNull.Value);
        command.Parameters.AddWithValue("@aiu", (object?)nanoAiu ?? DBNull.Value);
        command.Parameters.AddWithValue("@duration", (object?)durationMs ?? DBNull.Value);
        command.Parameters.AddWithValue("@finish", (object?)finishReason ?? DBNull.Value);
        command.Parameters.AddWithValue("@agent", (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@parent", (object?)parentToolCallId ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>Closes the writer connection, so that the test can replace or lock the database file.</summary>
    public void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
        SqliteConnection.ClearAllPools();
    }

    public void Dispose()
    {
        CloseWriter();
        Directory.Delete(_root, recursive: true);
    }

    private void Execute(string sql)
    {
        using var command = Writer().CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Writer() =>
        _writer ?? throw new InvalidOperationException("The writer connection is closed.");
}

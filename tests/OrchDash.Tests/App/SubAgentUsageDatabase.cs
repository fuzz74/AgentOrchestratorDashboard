using Microsoft.Data.Sqlite;
using OrchDash.Core.Model;

namespace OrchDash.Tests.App;

/// <summary>
/// A Copilot <c>session-store.db</c> as <see cref="UsageDatabase"/> creates it (WAL, schema 8, the fixture's
/// <c>assistant_usage_events</c> table), with rows that set <c>agent_id</c> and <c>parent_tool_call_id</c>, which
/// <see cref="UsageDatabase.Commit"/> leaves NULL (36.2). The rows are written through a second writer connection that
/// stays open until dispose.
/// </summary>
internal sealed class SubAgentUsageDatabase : IDisposable
{
    private readonly UsageDatabase _database;
    private readonly SqliteConnection _writer;

    /// <summary>Creates the database at <paramref name="path"/>, whose folder must exist.</summary>
    public SubAgentUsageDatabase(string path)
    {
        _database = new UsageDatabase(path);
        _writer = new SqliteConnection($"Data Source={path};Pooling=False");
        _writer.Open();
    }

    /// <summary>
    /// Commits one usage row of <paramref name="sessionId"/>; null ids are written as NULL, as for the agent's own
    /// calls. The row's input_tokens is <paramref name="usage"/>'s input plus its cached part, as Copilot writes it.
    /// </summary>
    public void Commit(string sessionId, DateTimeOffset createdAt, string? agentId, string? parentToolCallId, TokenUsage usage, long nanoAiu)
    {
        using var command = _writer.CreateCommand();
        command.CommandText = """
            INSERT INTO assistant_usage_events (session_id, agent_id, parent_tool_call_id, model, created_at, input_tokens,
                cache_read_tokens, cache_write_tokens, output_tokens, reasoning_tokens, total_nano_aiu, duration_ms, finish_reason)
            VALUES (@session, @agent, @parent, 'gpt-6-sol', @created, @input, @cacheRead, @cacheWrite, @output, 0, @aiu, 1500, 'tool_calls')
            """;
        command.Parameters.AddWithValue("@session", sessionId);
        command.Parameters.AddWithValue("@agent", (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@parent", (object?)parentToolCallId ?? DBNull.Value);
        command.Parameters.AddWithValue("@created", SubAgentJson.Time(createdAt));
        command.Parameters.AddWithValue("@input", usage.Input + usage.CacheRead + usage.CacheWrite);
        command.Parameters.AddWithValue("@cacheRead", usage.CacheRead);
        command.Parameters.AddWithValue("@cacheWrite", usage.CacheWrite);
        command.Parameters.AddWithValue("@output", (object?)usage.Output ?? DBNull.Value);
        command.Parameters.AddWithValue("@aiu", nanoAiu);
        command.ExecuteNonQuery();
    }

    /// <summary>Closes both writers and clears the pools, so that the folder of the database can be deleted.</summary>
    public void Dispose()
    {
        _writer.Dispose();
        _database.Dispose();
        SqliteConnection.ClearAllPools();
    }
}

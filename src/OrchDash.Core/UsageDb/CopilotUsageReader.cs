using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using OrchDash.Core.Model;

namespace OrchDash.Core.UsageDb;

/// <summary>
/// Reads Copilot's token figures per model call from <c>session-store.db</c> (spec 13.1-13.7). Called on one
/// thread; never throws. It runs only the two SELECT statements of <see cref="UsageQueries"/>; SQLite itself may
/// create or update the <c>-shm</c> and <c>-wal</c> files of a WAL-mode database (N.5).
/// </summary>
public sealed class CopilotUsageReader : IUsageReader
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private LastRead? _lastRead;

    public CopilotUsageReader(string databasePath)
    {
        _databasePath = databasePath;
        _connectionString = $"Data Source={databasePath};Mode=ReadOnly;Pooling=False;Default Timeout=1";
    }

    public UsageRows Read(IReadOnlyCollection<string> sessionIds)
    {
        // 13.2: no ids, no file system access.
        if (sessionIds.Count == 0)
            return UsageRows.Empty;

        var ids = sessionIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        try
        {
            // 13.3
            if (!File.Exists(_databasePath))
                return new UsageRows(UsageRows.Empty.BySession, null, $"Copilot database not found: {_databasePath}");

            // 13.4: the files' stamps are taken before the database is opened, so a commit while it is read
            // makes the next call read it again.
            var stamp = new FilesStamp(FileStamp.Of(_databasePath), FileStamp.Of(_databasePath + "-wal"));
            if (_lastRead is not null && _lastRead.Stamp == stamp && _lastRead.Ids.SequenceEqual(ids))
                return _lastRead.Rows;

            var rows = Query(ids);
            _lastRead = new LastRead(stamp, ids, rows);
            return rows;
        }
        catch (Exception e)
        {
            // 13.5: the rows and schema version of the last read that succeeded, with the failure.
            var last = _lastRead?.Rows ?? UsageRows.Empty;
            return new UsageRows(last.BySession, last.SchemaVersion, $"Copilot database: {e.Message}");
        }
    }

    // 13.1: one connection per read, closed before the result is returned.
    private UsageRows Query(ImmutableArray<string> ids)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var schemaVersion = UsageQueries.TrySchemaVersion(connection);
        var bySession = UsageQueries.Rows(connection, ids);
        return new UsageRows(bySession, schemaVersion, null);
    }

    private readonly record struct FileStamp(long Length, DateTime WriteTime)
    {
        // A missing file counts as length 0.
        public static FileStamp Of(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : default;
        }
    }

    private readonly record struct FilesStamp(FileStamp Database, FileStamp Wal);

    // Ids are distinct and in ordinal order, so equal sets give equal arrays.
    private sealed record LastRead(FilesStamp Stamp, ImmutableArray<string> Ids, UsageRows Rows);
}

using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Transcript;

/// <summary>
/// Reads Claude Code's own transcript of a session, <c>&lt;projectsDir&gt;/&lt;folder&gt;/&lt;sessionId&gt;.jsonl</c>,
/// into one <see cref="StoreData"/> (spec 11.1-11.6), with one <see cref="StoreData.SubAgents"/> entry per sub-agent
/// transcript in <c>&lt;folder&gt;/&lt;sessionId&gt;/subagents</c> (36.1). Called on one thread; never throws.
/// </summary>
public sealed class ClaudeTranscriptStore : ISessionStore
{
    private const FileShare ShareAll = FileShare.ReadWrite | FileShare.Delete;
    private static readonly TimeSpan SearchPause = TimeSpan.FromSeconds(30);
    private static readonly IReadOnlyDictionary<string, SubAgentRead> NoSubAgentFiles =
        ImmutableDictionary<string, SubAgentRead>.Empty;

    private readonly string _projectsDir;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, DateTimeOffset> _failedSearches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LastRead> _lastReads = new(StringComparer.Ordinal);

    public ClaudeTranscriptStore(string projectsDir, TimeProvider? time = null)
    {
        _projectsDir = projectsDir;
        _time = time ?? TimeProvider.System;
    }

    public StoreData? Read(string sessionId, string? workDir)
    {
        if (string.IsNullOrEmpty(sessionId) || sessionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            sessionId is "." or "..")
            return null;

        var path = Locate(sessionId + ".jsonl", workDir) ?? SearchOnce(sessionId);
        return path is null ? null : ReadFile(sessionId, path);
    }

    /// <summary>
    /// The project folder name Claude Code uses for a working folder: every character that is not an ASCII
    /// letter or digit becomes '-'.
    /// </summary>
    public static string FolderName(string workDir) =>
        string.Create(workDir.Length, workDir, static (chars, text) =>
        {
            for (int i = 0; i < text.Length; i++)
                chars[i] = char.IsAsciiLetterOrDigit(text[i]) ? text[i] : '-';
        });

    // 11.1: the transcript in the folder of workDir, when there is one.
    private string? Locate(string fileName, string? workDir)
    {
        if (string.IsNullOrEmpty(workDir))
            return null;

        var path = Path.Combine(_projectsDir, FolderName(workDir), fileName);
        return File.Exists(path) ? path : null;
    }

    // 11.2: after a search found nothing, the id is not searched for again until 30 seconds have passed.
    private string? SearchOnce(string sessionId)
    {
        var now = _time.GetUtcNow();
        if (_failedSearches.TryGetValue(sessionId, out var failedAt) && now - failedAt < SearchPause)
            return null;

        var path = Search(sessionId + ".jsonl");
        if (path is null)
            _failedSearches[sessionId] = now;
        else
            _failedSearches.Remove(sessionId);
        return path;
    }

    // 11.1: the first <projectsDir>/*/<fileName> in ordinal order of the whole path.
    private string? Search(string fileName)
    {
        try
        {
            if (!Directory.Exists(_projectsDir))
                return null;

            string? first = null;
            foreach (var folder in Directory.EnumerateDirectories(_projectsDir))
            {
                var path = Path.Combine(folder, fileName);
                if ((first is null || string.CompareOrdinal(path, first) < 0) && File.Exists(path))
                    first = path;
            }
            return first;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // 11.5, 11.6 and 36.1: when neither the transcript nor its sub-agent entries changed, the last instance; when the
    // transcript cannot be read, the last result.
    private StoreData? ReadFile(string sessionId, string path)
    {
        _lastReads.TryGetValue(sessionId, out var last);
        long length;
        DateTime writeTime;
        StoreData main;
        try
        {
            var info = new FileInfo(path);
            length = info.Length;
            writeTime = info.LastWriteTimeUtc;
            main = last is not null && last.Path == path && last.Length == length && last.WriteTime == writeTime
                ? last.Main
                : TranscriptMapper.Map(ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return last?.Data;
        }

        var subAgents = ReadSubAgents(Path.Combine(Path.ChangeExtension(path, null), "subagents"), last, out var files);
        StoreData data;
        if (last is not null && ReferenceEquals(main, last.Main) && ReferenceEquals(subAgents, last.Data.SubAgents))
            data = last.Data;
        else
            data = subAgents.Count == 0 ? main : main with { SubAgents = subAgents };
        _lastReads[sessionId] = new LastRead(path, length, writeTime, main, files, data);
        return data;
    }

    // 36.1: maps every <directory>/agent-<x>.jsonl whose meta file names its tool use T into the entry T; when two
    // name the same T, the first in ordinal path order. Gives the last entries instance when no entry changed, and
    // after a listing error.
    private static ImmutableDictionary<string, StoreData> ReadSubAgents(
        string directory, LastRead? last, out IReadOnlyDictionary<string, SubAgentRead> files)
    {
        var lastFiles = last?.SubAgentFiles ?? NoSubAgentFiles;
        var lastEntries = last?.Data.SubAgents ?? StoreData.NoSubAgents;
        string[] paths;
        try
        {
            paths = Directory.Exists(directory) ? Directory.GetFiles(directory, "agent-*.jsonl") : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            files = lastFiles;
            return lastEntries;
        }

        Array.Sort(paths, StringComparer.Ordinal);
        var reads = new Dictionary<string, SubAgentRead>(StringComparer.Ordinal);
        var entries = StoreData.NoSubAgents.ToBuilder();
        foreach (var path in paths)
        {
            if (ReadSubAgent(path, lastFiles.GetValueOrDefault(path)) is not { } read)
                continue;

            reads.Add(path, read);
            if (read is { ToolUseId: { } id, Data: { } data })
                entries.TryAdd(id, data);
        }

        files = reads;
        if (entries.Count == 0)
            return StoreData.NoSubAgents;
        return entries.Count == lastEntries.Count &&
            entries.All(entry => lastEntries.TryGetValue(entry.Key, out var data) && ReferenceEquals(data, entry.Value))
            ? lastEntries
            : entries.ToImmutable();
    }

    // A sub-agent transcript is read again, with its meta file, only when its length or last write time changed. A
    // missing, unreadable or invalid meta file gives a read without data; an unreadable transcript its last read.
    private static SubAgentRead? ReadSubAgent(string path, SubAgentRead? last)
    {
        try
        {
            var info = new FileInfo(path);
            long length = info.Length;
            var writeTime = info.LastWriteTimeUtc;
            if (last is not null && last.Length == length && last.WriteTime == writeTime)
                return last;

            var toolUseId = ReadToolUseId(Path.ChangeExtension(path, ".meta.json"));
            var data = toolUseId is null ? null : TranscriptMapper.Map(ReadAllBytes(path), toolUseId);
            return new SubAgentRead(length, writeTime, toolUseId, data);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return last;
        }
    }

    private static string? ReadToolUseId(string metaPath)
    {
        try
        {
            return TranscriptMapper.ToolUseId(ReadAllBytes(metaPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[] ReadAllBytes(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, ShareAll);
        var bytes = new byte[(int)Math.Min(RandomAccess.GetLength(handle), Array.MaxLength)];
        int count = 0;
        int read;
        while (count < bytes.Length && (read = RandomAccess.Read(handle, bytes.AsSpan(count), count)) > 0)
            count += read;
        return count == bytes.Length ? bytes : bytes[..count];
    }

    // The length and last write time are taken before the file is opened, so a change while it is read
    // makes the next call read it again. Main is the transcript's own data; Data adds the sub-agent entries to it.
    private sealed record LastRead(string Path, long Length, DateTime WriteTime, StoreData Main,
        IReadOnlyDictionary<string, SubAgentRead> SubAgentFiles, StoreData Data);

    // One sub-agent transcript as last read; ToolUseId and Data are null when its meta file named no tool use.
    private sealed record SubAgentRead(long Length, DateTime WriteTime, string? ToolUseId, StoreData? Data);
}

using OrchDash.Core.Model;

namespace OrchDash.Core.Transcript;

/// <summary>
/// Reads Claude Code's own transcript of a session, <c>&lt;projectsDir&gt;/&lt;folder&gt;/&lt;sessionId&gt;.jsonl</c>,
/// into one <see cref="StoreData"/> (spec 11.1-11.6). Called on one thread; never throws.
/// </summary>
public sealed class ClaudeTranscriptStore : ISessionStore
{
    private const FileShare ShareAll = FileShare.ReadWrite | FileShare.Delete;
    private static readonly TimeSpan SearchPause = TimeSpan.FromSeconds(30);

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

    // 11.5 and 11.6: an unchanged file gives the last instance; an unreadable one the last result.
    private StoreData? ReadFile(string sessionId, string path)
    {
        _lastReads.TryGetValue(sessionId, out var last);
        try
        {
            var info = new FileInfo(path);
            long length = info.Length;
            var writeTime = info.LastWriteTimeUtc;
            if (last is not null && last.Path == path && last.Length == length && last.WriteTime == writeTime)
                return last.Data;

            var data = TranscriptMapper.Map(ReadAllBytes(path));
            _lastReads[sessionId] = new LastRead(path, length, writeTime, data);
            return data;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return last?.Data;
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
    // makes the next call read it again.
    private sealed record LastRead(string Path, long Length, DateTime WriteTime, StoreData Data);
}

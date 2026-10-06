using System.Collections.Immutable;
using System.Security;
using OrchDash.Core.Model;
using OrchDash.Core.SessionFolder;

namespace OrchDash.Core.CommandLogs;

/// <summary>
/// Reads the setup, acceptance and integration command logs under <c>&lt;runDir&gt;/logs</c> (spec 22.1-22.4).
/// A file is read again only when its length or last write time changed. Called on one thread; never throws.
/// </summary>
public sealed class CommandLogReader : ICommandLogReader
{
    private const string LogsFolder = "logs";
    private const string StderrSuffix = ".stderr";

    private Dictionary<string, (LogText Log, LogText Stderr)> _texts = new(StringComparer.Ordinal);

    public CommandLogData Read(string runDir)
    {
        var problems = new List<string>();
        var seen = new Dictionary<string, (LogText Log, LogText Stderr)>(StringComparer.Ordinal);
        var logs = new List<CommandLog>();

        var logsDir = Path.Combine(runDir, LogsFolder);
        foreach (var file in ListFiles(logsDir, problems))
        {
            var key = Path.GetRelativePath(logsDir, file).Replace('\\', '/');
            if (LogKey.Parse(key) is not { } name)
                continue;

            var path = Path.Combine(runDir, LogsFolder, key.Replace('/', '\\'));
            var stderrPath = path + StderrSuffix;
            if (FileStamp.Of(path) is not { } stamp)
                continue;   // gone since the listing
            var stderrStamp = FileStamp.Of(stderrPath);

            var last = _texts.GetValueOrDefault(key, (Log: LogText.None, Stderr: LogText.None));
            var texts = (Log: last.Log.Update(path, stamp, key, problems),
                         Stderr: last.Stderr.Update(stderrPath, stderrStamp, key, problems));
            seen[key] = texts;

            var writtenUtc = stderrStamp is { } s && s.LastWriteUtc > stamp.LastWriteUtc ? s.LastWriteUtc : stamp.LastWriteUtc;
            logs.Add(new CommandLog(name.Kind, name.TaskId, name.StartFolder, name.Attempt, key, path, stderrPath,
                new DateTimeOffset(writtenUtc, TimeSpan.Zero).ToLocalTime(), stamp.Length,
                texts.Log.Text, texts.Stderr.Text, null, CommandOutcome.Unknown, null));
        }

        _texts = seen;   // forgets the logs that are gone
        logs.Sort(NewestFirst);
        return new CommandLogData([.. logs], [.. problems]);
    }

    // WrittenAt descending, then Key (ordinal).
    private static int NewestFirst(CommandLog a, CommandLog b)
    {
        var byTime = b.WrittenAt.CompareTo(a.WrittenAt);
        return byTime != 0 ? byTime : string.CompareOrdinal(a.Key, b.Key);
    }

    // Every file under the logs folder; none when the folder is missing, and none with a problem line when it
    // cannot be listed.
    private static List<string> ListFiles(string logsDir, List<string> problems)
    {
        try
        {
            return Directory.Exists(logsDir) ? Directory.EnumerateFiles(logsDir, "*", SearchOption.AllDirectories).ToList() : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                      or SecurityException)
        {
            problems.Add($"{LogsFolder}: {e.Message}");
            return [];
        }
    }
}

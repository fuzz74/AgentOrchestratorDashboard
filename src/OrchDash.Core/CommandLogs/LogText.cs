using System.Security;
using System.Text;
using OrchDash.Core.SessionFolder;

namespace OrchDash.Core.CommandLogs;

// The text of a log or stderr file as last read, with the stamp the file had then (null before the first read
// and for a missing file).
internal sealed record LogText(FileStamp? Stamp, string Text)
{
    public const int MaxLength = 2_000_000;
    public const string CutNotice = "... cut to the last 2,000,000 characters\n";

    private const FileShare ShareAll = FileShare.ReadWrite | FileShare.Delete;

    public static LogText None { get; } = new(null, "");

    // Spec 22.2-22.4: reads the file only when its stamp differs from the last read; a missing file gives "".
    // When the file cannot be opened, the last text comes back and "<key>: <message>" is added to problems.
    public LogText Update(string path, FileStamp? stamp, string key, List<string> problems)
    {
        if (stamp is null)
            return None;
        if (Stamp == stamp)
            return this;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, ShareAll);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return new LogText(stamp, Cut(reader.ReadToEnd()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            problems.Add($"{key}: {e.Message}");
            return this;
        }
    }

    private static string Cut(string text) =>
        text.Length > MaxLength ? string.Concat(CutNotice, text.AsSpan(text.Length - MaxLength)) : text;
}

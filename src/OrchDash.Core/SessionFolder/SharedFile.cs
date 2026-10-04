using System.Text;

namespace OrchDash.Core.SessionFolder;

// Reads whole files while the CLI may still write, rename or delete them (N.5).
internal static class SharedFile
{
    private const FileShare ShareAll = FileShare.ReadWrite | FileShare.Delete;

    // Null when the file cannot be opened or read.
    public static byte[]? TryReadAllBytes(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, ShareAll);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Null when the file cannot be opened or read; a byte order mark is removed.
    public static string? TryReadAllText(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, ShareAll);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

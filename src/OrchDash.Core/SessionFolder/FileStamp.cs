namespace OrchDash.Core.SessionFolder;

// Length and last write time of a file: while both are unchanged, the file is not read again.
internal readonly record struct FileStamp(long Length, DateTime LastWriteUtc)
{
    // Null when the file does not exist or its path is not valid.
    public static FileStamp? Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

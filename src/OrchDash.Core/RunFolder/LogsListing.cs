namespace OrchDash.Core.RunFolder;

/// <summary>
/// The files the session scan saw that the task detail of a running task needs: the files in the log root
/// and, per task, the files of its latest start folder. Filled by <see cref="SessionFileScanner"/> so that
/// <c>logs/</c> is listed only once per read.
/// </summary>
internal sealed class LogsListing
{
    private readonly List<FileInfo> _rootFiles = [];
    private readonly Dictionary<string, (string Name, FileInfo[] Files)> _latestStartFolders = new(StringComparer.Ordinal);

    public IReadOnlyList<FileInfo> RootFiles => _rootFiles;

    public void AddRootFile(FileInfo file) => _rootFiles.Add(file);

    // The latest start folder of a task is the one with the greatest name (ordinal).
    public void AddStartFolder(string taskId, string name, IEnumerable<FileInfo> files)
    {
        if (_latestStartFolders.TryGetValue(taskId, out var latest) && string.CompareOrdinal(latest.Name, name) >= 0)
            return;
        _latestStartFolders[taskId] = (name, [.. files]);
    }

    public bool TryGetLatestStartFolder(string taskId, out IReadOnlyList<FileInfo> files)
    {
        if (_latestStartFolders.TryGetValue(taskId, out var latest))
        {
            files = latest.Files;
            return true;
        }
        files = [];
        return false;
    }
}

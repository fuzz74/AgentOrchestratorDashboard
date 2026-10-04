namespace OrchDash.App;

/// <summary>Finds the repo whose run OrchDash shows: the nearest folder that contains <c>.orchestrator</c> (spec 1.1).</summary>
public static class RepoLocator
{
    /// <summary>The name of the orchestrator's folder inside a repo.</summary>
    public const string RunFolderName = ".orchestrator";

    /// <summary>
    /// Returns the full path of the nearest folder at or above <paramref name="startPath"/> that contains a folder
    /// <c>.orchestrator</c>, or null when there is none. A relative path is resolved against the current directory;
    /// a file starts the search at its folder; a path that does not exist or is not valid gives null.
    /// </summary>
    public static string? Find(string startPath)
    {
        ArgumentNullException.ThrowIfNull(startPath);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(startPath);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var folder = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath)
            : File.Exists(fullPath) ? new FileInfo(fullPath).Directory
            : null;
        for (; folder is not null; folder = folder.Parent)
        {
            if (Directory.Exists(Path.Combine(folder.FullName, RunFolderName)))
            {
                return Path.TrimEndingDirectorySeparator(folder.FullName);
            }
        }
        return null;
    }
}

using System.Security;

namespace OrchDash.Core.Model;

// Paths of a run (31.1). Pure; results are full paths without a trailing separator. A path that Path.GetFullPath
// rejects (empty or invalid) is used as given.
public static class RunPaths
{
    private const string RunsSuffix = ".runs";
    private const string WorktreesSuffix = ".worktrees";

    // <repo>.runs\<stamp> -> <repo>; otherwise the full path of repoPath.
    public static string RepoRoot(string repoPath)
    {
        if (Full(repoPath) is not { } full)
            return repoPath;
        return RunsFolderSibling(full) ?? full;
    }

    // "<stamp>" for <repo>.runs\<stamp>; else null.
    public static string? Stamp(string repoPath) =>
        Full(repoPath) is { } full && RunsFolderSibling(full) is not null ? Path.GetFileName(full) : null;

    // <RepoRoot>.worktrees\<taskId>; RepoRoot for a null task id.
    public static string WorkDir(string repoPath, string? taskId)
    {
        var root = RepoRoot(repoPath);
        return taskId is null ? root : Path.Combine(root + WorktreesSuffix, taskId);
    }

    // Full paths without trailing separators equal, ignoring case; the inputs as given when one is not a valid path.
    public static bool Same(string a, string b) =>
        Full(a) is { } fullA && Full(b) is { } fullB
            ? string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase)
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string? Full(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return null;
        }
    }

    // <repo>.runs\<stamp> -> <repo>; null when the parent folder's name does not end with .runs.
    // As RunsFolderSibling in Git/GitReadPass.cs.
    private static string? RunsFolderSibling(string full)
    {
        var parent = Path.GetDirectoryName(full);
        var name = Path.GetFileName(parent);
        if (parent is null || name is null || name.Length <= RunsSuffix.Length
            || !name.EndsWith(RunsSuffix, StringComparison.OrdinalIgnoreCase)
            || Path.GetDirectoryName(parent) is not { } grandparent)
            return null;
        return Path.Combine(grandparent, name[..^RunsSuffix.Length]);
    }
}

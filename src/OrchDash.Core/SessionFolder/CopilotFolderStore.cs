using OrchDash.Core.Model;

namespace OrchDash.Core.SessionFolder;

// Copilot's session folders under %USERPROFILE%\.copilot\session-state (spec 12.1-12.4): the system prompt and
// CLI version from <id>/events.jsonl, and the id of a running session from the workspace.yaml files.
// Called on one thread; never throws.
public sealed class CopilotFolderStore(string sessionStateDir) : ISessionStore, ISessionIdFinder
{
    private static readonly TimeSpan MaxDistance = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, CachedFile<StoreData>> _events = new(StringComparer.Ordinal);
    private Dictionary<string, CachedFile<Workspace?>> _workspaces = new(StringComparer.Ordinal);

    // workDir is not used: the folder is found by the session id alone.
    public StoreData? Read(string sessionId, string? workDir)
    {
        string path;
        try
        {
            path = Path.Combine(sessionStateDir, sessionId, "events.jsonl");
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (FileStamp.Of(path) is not { } stamp)
        {
            _events.Remove(sessionId);
            return null;
        }

        _events.TryGetValue(sessionId, out var cached);
        if (cached is not null && cached.Stamp == stamp)
            return cached.Value;

        // The stamp is taken before the read, so a line written in between is read again next time.
        if (SharedFile.TryReadAllBytes(path) is not { } bytes)
            return cached?.Value;

        var data = EventsFile.Map(bytes);
        _events[sessionId] = new CachedFile<StoreData>(stamp, data);
        return data;
    }

    public string? Find(string name, string workDir, DateTimeOffset startedAt, IReadOnlySet<string> knownIds)
    {
        var cwd = NormalizeDir(workDir);
        var seen = new Dictionary<string, CachedFile<Workspace?>>(StringComparer.Ordinal);
        string? best = null;
        var bestDistance = TimeSpan.MaxValue;

        foreach (var folder in SubFolders())
        {
            var workspace = ReadWorkspace(Path.Combine(folder, "workspace.yaml"), seen);
            if (workspace is null
                || !string.Equals(workspace.Name, name, StringComparison.Ordinal)
                || !string.Equals(NormalizeDir(workspace.Cwd), cwd, StringComparison.OrdinalIgnoreCase)
                || knownIds.Contains(workspace.Id))
                continue;

            var distance = (workspace.CreatedAt - startedAt).Duration();
            if (distance <= MaxDistance && distance < bestDistance)
            {
                best = workspace.Id;
                bestDistance = distance;
            }
        }

        _workspaces = seen;   // forgets the files of folders that are gone
        return best;
    }

    // The direct subfolders in ordinal order, so that a tie always goes to the same folder; none when the
    // folder is missing or cannot be listed.
    private List<string> SubFolders()
    {
        try
        {
            var folders = Directory.EnumerateDirectories(sessionStateDir).ToList();
            folders.Sort(StringComparer.Ordinal);
            return folders;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    // Reads the file only when its stamp changed; null when it is missing, unreadable or not usable.
    private Workspace? ReadWorkspace(string path, Dictionary<string, CachedFile<Workspace?>> seen)
    {
        if (FileStamp.Of(path) is not { } stamp)
            return null;

        if (!_workspaces.TryGetValue(path, out var cached) || cached.Stamp != stamp)
        {
            if (SharedFile.TryReadAllText(path) is not { } text)
                return null;
            cached = new CachedFile<Workspace?>(stamp, Workspace.Parse(text));
        }

        seen[path] = cached;
        return cached.Value;
    }

    // Spec 12.3: '/' replaced by '\' and a trailing '\' removed; compared ignoring case.
    private static string NormalizeDir(string dir)
    {
        var normalized = dir.Replace('/', '\\');
        return normalized.EndsWith('\\') ? normalized[..^1] : normalized;
    }
}

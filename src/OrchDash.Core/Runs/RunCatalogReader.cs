using System.Collections.Immutable;
using System.Security;
using System.Text;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;

namespace OrchDash.Core.Runs;

/// <summary>
/// Lists the run of a repo and its archived runs under <c>&lt;repo&gt;.runs</c> (spec 31.3-31.5). Opens files
/// read-only with full sharing (N.5) and writes nothing; never throws.
/// </summary>
public static class RunCatalogReader
{
    private const string RunFolder = ".orchestrator";
    private const string RunsSuffix = ".runs";
    private const string ProgressFile = "progress.md";
    private const string PlanningPrefix = "Planning from ";
    private const string SkeletonPrefix = "Creating the project skeleton in ";

    private static readonly RunCatalog Empty = new([], null);

    public static RunCatalog List(string repoPath)
    {
        if (!IsValidPath(repoPath))
            return Empty;

        var root = RunPaths.RepoRoot(repoPath);
        var runs = ImmutableArray.CreateBuilder<RunEntry>();

        var currentDir = Path.Combine(root, RunFolder);
        if (new[] { PlanJson.FileName, StateJson.FileName, ProgressFile }.Any(name => File.Exists(Path.Combine(currentDir, name))))
            runs.Add(Read(root, stamp: null));

        var runsDir = root + RunsSuffix;
        string? problem = null;
        try
        {
            var archived = Directory.Exists(runsDir)
                ? Directory.EnumerateDirectories(runsDir).Where(dir => Directory.Exists(Path.Combine(dir, RunFolder))).ToList()
                : [];
            foreach (var dir in archived.OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
                runs.Add(Read(dir, Path.GetFileName(dir)));
        }
        catch (Exception e) when (IsAccessError(e))
        {
            problem = $"{runsDir}: {e.Message}";
        }

        return new RunCatalog(runs.ToImmutable(), problem);
    }

    // One entry by the catalog table in section 4.3; a file that cannot be read or parsed leaves its fields empty
    // and adds a problem line.
    private static RunEntry Read(string repoPath, string? stamp)
    {
        var runDir = Path.Combine(repoPath, RunFolder);
        var problems = new List<string>();

        var progress = ReadText(runDir, ProgressFile, problems) is { } progressText ? ProgressParser.Parse(progressText) : [];
        var run = RunInfoRules.Derive(progress, stopRequested: false, lockHeld: false);
        var plan = ReadText(runDir, PlanJson.FileName, problems) is { } planText ? PlanJson.Parse(planText, problems) : null;
        var states = ReadText(runDir, StateJson.FileName, problems) is { } stateText ? StateJson.Parse(stateText, problems) : null;

        return new RunEntry(
            repoPath,
            stamp,
            plan?.Plan.Spec is { } spec ? Path.GetFileName(spec) : null,
            progress.IsEmpty ? null : progress[0].Time,
            run.FinishedAt,
            plan?.Tasks.Length ?? 0,
            states?.Values.Count(s => s.Status == TaskState.Done) ?? 0,
            states?.Values.Count(s => s.Status == TaskState.Failed) ?? 0,
            run.Provider,
            ModelOf(progress),
            problems.Count == 0 ? null : string.Join('\n', problems));
    }

    // The last word of the latest planning or skeleton entry.
    private static string? ModelOf(ImmutableArray<ProgressEntry> progress)
    {
        var entry = progress.LastOrDefault(e => e.Message.StartsWith(PlanningPrefix, StringComparison.Ordinal)
                                                || e.Message.StartsWith(SkeletonPrefix, StringComparison.Ordinal));
        var words = entry?.Message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words is { Length: > 0 } ? words[^1] : null;
    }

    // The whole text of a file of the run folder, as RunFolderReader reads it. Null when the file is missing, and
    // also, with one problem line that names the file, when it cannot be read.
    private static string? ReadText(string runDir, string fileName, ICollection<string> problems)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(runDir, fileName), FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception e) when (IsAccessError(e))
        {
            problems.Add($"{fileName}: {e.Message}");
            return null;
        }
    }

    // An empty or invalid path gives an empty catalog rather than a listing of the current directory.
    private static bool IsValidPath(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            return false;
        try
        {
            Path.GetFullPath(repoPath);
            return true;
        }
        catch (Exception e) when (IsAccessError(e))
        {
            return false;
        }
    }

    private static bool IsAccessError(Exception e) =>
        e is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}

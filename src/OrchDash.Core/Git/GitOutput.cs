using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using OrchDash.Core.Model;

namespace OrchDash.Core.Git;

/// <summary>Pure parsers for the output of the git command table and the git mapping rules (spec 20.6, 20.8, 4.3).</summary>
/// <remarks>Every parser accepts "\r\n" line endings and a trailing newline.</remarks>
public static partial class GitOutput
{
    public const int DiffLimit = 1_000_000;
    public const string DiffCutSuffix = "\n... cut at 1,000,000 characters";

    private const string RecordSeparator = "\x1e";
    private const char FieldSeparator = '\x1f';
    private const string HeadsPrefix = "refs/heads/";
    private const string TaskBranchPrefix = "orch/task/";

    /// <summary>Row 1: the output of <c>rev-parse --show-toplevel</c>, trimmed, with '/' as '\'.</summary>
    public static string ParseToplevel(string stdout) => WindowsPath(stdout.Trim());

    /// <summary>Row 2: the blocks of <c>worktree list --porcelain</c>, one worktree per block with a <c>worktree</c> line.</summary>
    public static ImmutableArray<GitWorktree> ParseWorktrees(string stdout)
    {
        var worktrees = ImmutableArray.CreateBuilder<GitWorktree>();
        string? path = null, branch = null, head = null;

        void EndBlock()
        {
            if (path is not null)
                worktrees.Add(new GitWorktree(path, branch, head));
            path = branch = head = null;
        }

        foreach (var line in Lines(stdout))
        {
            if (line.Length == 0)
                EndBlock();
            else if (line.StartsWith("worktree ", StringComparison.Ordinal))
                path = WindowsPath(line["worktree ".Length..]);
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
                head = line["HEAD ".Length..];
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
                branch = WithoutPrefix(line["branch ".Length..], HeadsPrefix);
            else if (line == "detached")
                branch = null;
        }

        EndBlock();
        return worktrees.ToImmutable();
    }

    /// <summary>Row 3: one branch per line <c>name&lt;TAB&gt;tip&lt;TAB&gt;iso-strict date</c>.</summary>
    public static ImmutableArray<GitBranch> ParseBranches(string stdout, string integrationBranch, string? baseBranch)
    {
        var branches = ImmutableArray.CreateBuilder<GitBranch>();
        foreach (var line in Lines(stdout))
        {
            var fields = line.Split('\t', 3);
            if (fields.Length < 2 || fields[0].Length == 0)
                continue;

            var name = fields[0];
            var committedAt = fields.Length == 3 ? ParseTime(fields[2]) : null;
            branches.Add(new GitBranch(name, fields[1], committedAt, BranchKind(name, integrationBranch, baseBranch)));
        }

        return branches.ToImmutable();
    }

    /// <summary>
    /// Row 4: records split at '\x1e', fields at '\x1f' (sha, short sha, author date, subject, body). Only Attempt,
    /// Merge and Sync subjects are kept, in git's order.
    /// </summary>
    public static ImmutableArray<GitCommit> ParseCommits(string stdout)
    {
        var commits = ImmutableArray.CreateBuilder<GitCommit>();
        foreach (var record in stdout.Replace("\r\n", "\n").Split(RecordSeparator))
        {
            // git prints a newline after each record, so each record after the first starts with one.
            var fields = record.TrimStart('\n').Split(FieldSeparator, 5);
            if (fields.Length < 5 || ParseTime(fields[2]) is not { } time)
                continue;

            var subject = fields[3];
            var body = fields[4].TrimEnd('\n');
            if (CommitKind(subject, body) is not (var kind, var taskId, var attempt))
                continue;

            commits.Add(new GitCommit(fields[0], fields[1], time, kind, subject, body, attempt, taskId));
        }

        return commits.ToImmutable();
    }

    /// <summary>Row 5 and other line lists: the non-empty lines as printed, without a trailing '\r'.</summary>
    public static ImmutableArray<string> ParseLines(string stdout) =>
        [.. Lines(stdout).Where(line => line.Length > 0)];

    /// <summary>Rows 6, 8 and 10: one file per line <c>added&lt;TAB&gt;removed&lt;TAB&gt;path</c>; '-' (binary) gives null.</summary>
    public static DiffStat ParseNumstat(string stdout)
    {
        var files = ImmutableArray.CreateBuilder<DiffFile>();
        foreach (var line in Lines(stdout))
        {
            var fields = line.Split('\t', 3);
            if (fields.Length == 3 && TryParseCount(fields[0], out var added) && TryParseCount(fields[1], out var removed))
                files.Add(new DiffFile(fields[2], added, removed));
        }

        return new DiffStat(files.ToImmutable());
    }

    /// <summary>20.8: a diff longer than 1,000,000 characters is cut there and marked; a shorter one is returned as is.</summary>
    public static string CutDiff(string diff) =>
        diff.Length > DiffLimit ? string.Concat(diff.AsSpan(0, DiffLimit), DiffCutSuffix) : diff;

    /// <summary>The worktree of a task: <c>&lt;parent of repoRoot&gt;\&lt;name of repoRoot&gt;.worktrees\&lt;taskId&gt;</c>.</summary>
    public static string WorktreePathOf(string repoRoot, string taskId)
    {
        var root = WindowsPath(repoRoot).TrimEnd('\\');
        var parent = Path.GetDirectoryName(root) ?? "";
        return $@"{parent.TrimEnd('\\')}\{Path.GetFileName(root)}.worktrees\{taskId}";
    }

    /// <summary>Two paths are the same when they are equal ignoring case, with '/' as '\' and without trailing separators.</summary>
    public static bool SamePath(string a, string b) =>
        string.Equals(WindowsPath(a).TrimEnd('\\'), WindowsPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>The commits ordered by <c>Time</c>, then <c>Sha</c> (ordinal).</summary>
    public static ImmutableArray<GitCommit> OrderCommits(IEnumerable<GitCommit> commits) =>
        [.. commits.OrderBy(commit => commit.Time).ThenBy(commit => commit.Sha, StringComparer.Ordinal)];

    private static IEnumerable<string> Lines(string stdout) =>
        stdout.Split('\n').Select(line => line.TrimEnd('\r'));

    private static string WindowsPath(string path) => path.Replace('/', '\\');

    private static string WithoutPrefix(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;

    private static DateTimeOffset? ParseTime(string text) =>
        DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;

    private static bool TryParseCount(string text, out int? count)
    {
        count = null;
        if (text == "-")
            return true;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return false;
        count = value;
        return true;
    }

    private static GitBranchKind BranchKind(string name, string integrationBranch, string? baseBranch)
    {
        if (name.Length > TaskBranchPrefix.Length && name.StartsWith(TaskBranchPrefix, StringComparison.Ordinal))
            return GitBranchKind.Task;
        if (ArchiveBranch().IsMatch(name))
            return GitBranchKind.Archive;
        if (name == integrationBranch)
            return GitBranchKind.Integration;
        if (name == baseBranch)
            return GitBranchKind.Base;
        return GitBranchKind.Other;
    }

    // The kind, task id and attempt of a subject, or null for a subject that is not an orchestrator commit.
    private static (GitCommitKind Kind, string TaskId, int? Attempt)? CommitKind(string subject, string body)
    {
        if (AttemptSubject().Match(subject) is { Success: true } attempt)
        {
            var number = AttemptNumber().Match(body);
            int? n = number.Success
                && int.TryParse(number.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : null;
            return (GitCommitKind.Attempt, attempt.Groups["id"].Value, n);
        }

        if (MergeSubject().Match(subject) is { Success: true } merge)
            return (GitCommitKind.Merge, merge.Groups["id"].Value, null);

        if (SyncSubject().Match(subject) is { Success: true } sync)
            return (GitCommitKind.Sync, sync.Groups["id"].Value, null);

        return null;
    }

    [GeneratedRegex(@"\Aorch/archive/.+/[^/]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveBranch();

    [GeneratedRegex(@"^orch\((?<id>.+?)\): ", RegexOptions.CultureInvariant)]
    private static partial Regex AttemptSubject();

    [GeneratedRegex(@"Attempt ([0-9]+)\.", RegexOptions.CultureInvariant)]
    private static partial Regex AttemptNumber();

    [GeneratedRegex(@"^Merge task (?<id>[^:]+): ", RegexOptions.CultureInvariant)]
    private static partial Regex MergeSubject();

    [GeneratedRegex(@"^Merge branch '.+' into orch/task/(?<id>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex SyncSubject();
}

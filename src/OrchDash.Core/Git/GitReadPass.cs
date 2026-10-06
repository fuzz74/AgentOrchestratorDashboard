using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Git;

/// <summary>
/// One read: runs the rows of the git command table in their order and maps their output by the git mapping rules
/// (20.3-20.9). A pass is used once; <see cref="Diffs"/> holds the diff values to reuse in the next read.
/// </summary>
internal sealed class GitReadPass(GitRunner runner, IReadOnlyDictionary<string, TaskDiffs> previousDiffs)
{
    private const string DefaultIntegrationBranch = "orch/integration";
    private const string RunsSuffix = ".runs";

    private static readonly string[] ToplevelArgs = ["rev-parse", "--show-toplevel"];
    private static readonly string[] WorktreeArgs = ["worktree", "list", "--porcelain"];
    private static readonly string[] BranchArgs =
        ["for-each-ref", "--format=%(refname:short)%09%(objectname:short)%09%(committerdate:iso-strict)", "refs/heads/"];
    private static readonly string[] LogArgs =
    [
        "log", "--all", "--format=%H%x1f%h%x1f%aI%x1f%s%x1f%b%x1e", "-E",
        @"--grep=^orch\(.+\): ", "--grep=^Merge task ", "--grep=^Merge branch '.+' into orch/task/",
    ];
    private static readonly string[] StatusArgs = ["status", "--porcelain"];

    private readonly List<string> _problems = [];

    /// <summary>The diff values per task id of this read, for the tasks whose diff commands all succeeded or were reused.</summary>
    public Dictionary<string, TaskDiffs> Diffs { get; } = new(StringComparer.Ordinal);

    /// <summary>Set when git itself could not be started (20.5).</summary>
    public bool GitUnstartable { get; private set; }

    public GitInfo Run(string repoPath, PlanInfo? plan, ImmutableArray<TaskView> tasks, DateTimeOffset now)
    {
        if (FindRoot(repoPath) is not { } root)
            return GitInfo.Empty with { ReadAt = now, Problem = Problem() };

        var integration = plan?.IntegrationBranch is { Length: > 0 } name ? name : DefaultIntegrationBranch;
        var baseBranch = plan?.BaseBranch;

        // Rows 2-4.
        var worktrees = Output(root, WorktreeArgs) is { } worktreeList ? GitOutput.ParseWorktrees(worktreeList) : [];
        var branches = Output(root, BranchArgs) is { } refs ? GitOutput.ParseBranches(refs, integration, baseBranch) : [];
        var commits = Output(root, LogArgs) is { } log ? GitOutput.ParseCommits(log) : [];

        var tips = new Dictionary<string, GitBranch>(StringComparer.Ordinal);
        foreach (var branch in branches)
            tips.TryAdd(branch.Name, branch);
        var integrationExists = tips.ContainsKey(integration);

        var reads = tasks.Select(task => new TaskRead(task, root, worktrees, tips)).ToArray();

        // Row 5.
        foreach (var read in reads.Where(read => read.WorktreeExists))
        {
            read.Status = Output(read.WorktreePath, StatusArgs);
            read.Failed |= read.Status is null;
        }

        // 20.7: reuse the last read's diffs when the key is unchanged.
        foreach (var read in reads)
        {
            read.Key = new DiffKey(root, read.Branch?.Tip, read.Task.MergedSha, read.Status);
            if (previousDiffs.TryGetValue(read.Task.Id, out var previous) && previous.Key == read.Key)
                read.Reused = previous;
        }

        var changed = reads.Where(read => read.Reused is null).ToArray();

        // Rows 6 and 7.
        foreach (var read in changed.Where(read => read.WorktreeExists))
        {
            read.Uncommitted = Numstat(read, read.WorktreePath, "diff", "--numstat", "HEAD");
            read.UncommittedDiff = Diff(read, read.WorktreePath, "diff", "HEAD");
        }

        // Rows 8 and 9.
        foreach (var read in changed.Where(read => read.Task.MergedSha is null && read.Branch is not null && integrationExists))
        {
            var range = $"{integration}...{read.Branch!.Name}";
            read.Committed = Numstat(read, root, "diff", "--numstat", range);
            read.CommittedDiff = Diff(read, root, "diff", range);
        }

        // Rows 10 and 11.
        foreach (var read in changed.Where(read => read.Task.MergedSha is not null))
        {
            var sha = read.Task.MergedSha!;
            read.Committed = Numstat(read, root, "diff", "--numstat", $"{sha}^1", sha);
            read.CommittedDiff = Diff(read, root, "diff", $"{sha}^1", sha);
        }

        foreach (var read in reads)
        {
            if (read.Reused is { } reused)
                Diffs[read.Task.Id] = reused;
            else if (!read.Failed)
                Diffs[read.Task.Id] = new TaskDiffs(read.Key, read.Uncommitted, read.Committed,
                    read.UncommittedDiff, read.CommittedDiff);
        }

        return new GitInfo(now, root,
            integration, tips.GetValueOrDefault(integration)?.Tip,
            baseBranch, baseBranch is null ? null : tips.GetValueOrDefault(baseBranch)?.Tip,
            worktrees, branches,
            [.. reads.Select(read => read.ToGitTask(commits, branches))],
            Problem());
    }

    // Row 1, with the folder beside a `.runs` folder as the fallback (20.9). The problem line kept is the last one.
    private string? FindRoot(string repoPath)
    {
        if (Output(repoPath, ToplevelArgs) is { } toplevel)
            return GitOutput.ParseToplevel(toplevel);
        if (GitUnstartable || RunsFolderSibling(repoPath) is not { } beside)
            return null;

        _problems.Clear();
        return Output(beside, ToplevelArgs) is { } besideToplevel ? GitOutput.ParseToplevel(besideToplevel) : null;
    }

    // <repo>.runs\<timestamp> -> <repo>; null when the parent folder's name does not end with .runs.
    private static string? RunsFolderSibling(string repoPath)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoPath)));
        var name = Path.GetFileName(parent);
        if (parent is null || name is null || name.Length <= RunsSuffix.Length
            || !name.EndsWith(RunsSuffix, StringComparison.OrdinalIgnoreCase)
            || Path.GetDirectoryName(parent) is not { } grandparent)
            return null;
        return Path.Combine(grandparent, name[..^RunsSuffix.Length]);
    }

    private DiffStat? Numstat(TaskRead read, string dir, params string[] args)
    {
        var output = Output(dir, args);
        read.Failed |= output is null;
        return output is null ? null : GitOutput.ParseNumstat(output);
    }

    private string? Diff(TaskRead read, string dir, params string[] args)
    {
        var output = Output(dir, args);
        read.Failed |= output is null;
        return output is null ? null : GitOutput.CutDiff(output);
    }

    // The stdout of a command, or null after adding its problem line (20.4).
    private string? Output(string dir, string[] args)
    {
        var result = runner.Run(dir, args);
        if (result.Problem is null)
            return result.Stdout;

        GitUnstartable |= result.CouldNotStart;
        _problems.Add($"git {string.Join(' ', args)}: {result.Problem}");
        return null;
    }

    private string? Problem() => _problems.Count == 0 ? null : string.Join('\n', _problems);

    // The state of one task while the rows run.
    private sealed class TaskRead
    {
        private const string TaskBranchPrefix = "orch/task/";
        private const string ArchiveBranchPrefix = "orch/archive/";

        public TaskRead(TaskView task, string root, ImmutableArray<GitWorktree> worktrees, Dictionary<string, GitBranch> tips)
        {
            Task = task;
            WorktreePath = GitOutput.WorktreePathOf(root, task.Id);
            WorktreeExists = worktrees.Any(worktree => GitOutput.SamePath(worktree.Path, WorktreePath));
            Branch = tips.GetValueOrDefault(TaskBranchPrefix + task.Id) is { Kind: GitBranchKind.Task } branch ? branch : null;
        }

        public TaskView Task { get; }
        public string WorktreePath { get; }
        public bool WorktreeExists { get; }
        public GitBranch? Branch { get; }
        public string? Status { get; set; }
        public DiffKey Key { get; set; }
        public TaskDiffs? Reused { get; set; }
        public bool Failed { get; set; }
        public DiffStat? Uncommitted { get; set; }
        public DiffStat? Committed { get; set; }
        public string? UncommittedDiff { get; set; }
        public string? CommittedDiff { get; set; }

        public GitTask ToGitTask(ImmutableArray<GitCommit> commits, ImmutableArray<GitBranch> branches)
        {
            var id = Task.Id;
            var own = commits.Where(commit => commit.TaskId == id).ToArray();
            var archivePrefix = $"{ArchiveBranchPrefix}{id}/";

            return new GitTask(id, Branch?.Name, Branch?.Tip, WorktreePath, WorktreeExists,
                Status is null ? [] : GitOutput.ParseLines(Status),
                Reused is null ? Uncommitted : Reused.Uncommitted,
                Reused is null ? Committed : Reused.Committed,
                GitOutput.OrderCommits(own.Where(commit => commit.Kind is GitCommitKind.Attempt or GitCommitKind.Sync)),
                own.Where(commit => commit.Kind == GitCommitKind.Merge).MaxBy(commit => commit.Time),
                [.. branches
                    .Where(branch => branch.Kind == GitBranchKind.Archive && branch.Name.StartsWith(archivePrefix, StringComparison.Ordinal))
                    .Select(branch => branch.Name)
                    .Order(StringComparer.Ordinal)],
                Reused is null ? CommittedDiff : Reused.CommittedDiff,
                Reused is null ? UncommittedDiff : Reused.UncommittedDiff);
        }
    }
}

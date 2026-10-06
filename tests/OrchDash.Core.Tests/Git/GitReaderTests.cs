using System.Collections.Immutable;
using System.Diagnostics;
using OrchDash.Core.Git;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Core.Tests.Git;

public sealed class GitReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 30, 0, TimeSpan.FromHours(2));
    private static readonly PlanInfo Plan = new(null, "main", "orch/integration", ImmutableSortedDictionary<string, string>.Empty);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public void ReadNow_reads_the_repo_and_each_task()
    {
        using var repo = new TempGitRepo();
        using var reader = new GitReader();
        var mergeSha = repo.Sha("orch/integration");

        var info = reader.ReadNow(repo.Root, Plan, Tasks(mergeSha), Now);

        Assert.Null(info.Problem);
        Assert.Equal(Now, info.ReadAt);
        Assert.Equal(repo.Root, info.RepoRoot, ignoreCase: true);
        Assert.Equal("orch/integration", info.IntegrationBranch);
        Assert.Equal(repo.ShortSha("orch/integration"), info.IntegrationTip);
        Assert.Equal("main", info.BaseBranch);
        Assert.Equal(repo.ShortSha("main"), info.BaseTip);

        Assert.Equal(4, info.Worktrees.Length);
        AssertWorktree(info, repo.Root, "main", repo.Sha("main"));
        AssertWorktree(info, repo.IntegrationWorktree, "orch/integration", mergeSha);
        AssertWorktree(info, repo.WorktreeOf("x"), "orch/task/x", repo.Sha("orch/task/x"));
        AssertWorktree(info, repo.WorktreeOf("y"), "orch/task/y", repo.Sha("orch/task/y"));

        Assert.Equal(
            [
                ("main", GitBranchKind.Base),
                ("orch/archive/x/20261006-120000000", GitBranchKind.Archive),
                ("orch/integration", GitBranchKind.Integration),
                ("orch/task/x", GitBranchKind.Task),
                ("orch/task/y", GitBranchKind.Task),
            ],
            info.Branches.Select(branch => (branch.Name, branch.Kind)));
        Assert.All(info.Branches, branch =>
        {
            Assert.Equal(repo.ShortSha(branch.Name), branch.Tip);
            Assert.NotNull(branch.CommittedAt);
        });

        Assert.Equal(["x", "y", "z"], info.Tasks.Select(task => task.TaskId));
        var (x, y, z) = (info.Tasks[0], info.Tasks[1], info.Tasks[2]);

        Assert.Equal("orch/task/x", x.Branch);
        Assert.Equal(repo.ShortSha("orch/task/x"), x.Tip);
        Assert.Equal(repo.WorktreeOf("x"), x.WorktreePath, ignoreCase: true);
        Assert.True(x.WorktreeExists);
        Assert.Empty(x.UncommittedFiles);
        Assert.Empty(x.Uncommitted!.Files);
        Assert.Equal("", x.UncommittedDiff);
        Assert.Equal([(GitCommitKind.Attempt, 1), (GitCommitKind.Sync, (int?)null)],
            x.Commits.Select(commit => (commit.Kind, commit.Attempt)));
        Assert.Equal(repo.Sha("orch/task/x^1"), x.Commits[0].Sha);
        Assert.Equal("orch(x): Task x", x.Commits[0].Subject);
        Assert.Equal("Attempt 1.", x.Commits[0].Body);
        Assert.Equal("Merge branch 'orch/integration' into orch/task/x", x.Commits[1].Subject);
        Assert.Equal(repo.Sha("orch/task/x"), x.Commits[1].Sha);
        Assert.Equal(mergeSha, x.MergeCommit!.Sha);
        Assert.Equal("Merge task x: Task x", x.MergeCommit.Subject);
        Assert.Equal(["orch/archive/x/20261006-120000000"], x.ArchiveBranches);
        Assert.Equal([new DiffFile("x.txt", 1, 0)], x.Committed!.Files);
        Assert.Contains("diff --git a/x.txt b/x.txt", x.CommittedDiff);
        Assert.DoesNotContain("integration.txt", x.CommittedDiff);

        Assert.Equal("orch/task/y", y.Branch);
        Assert.Equal(repo.ShortSha("orch/task/y"), y.Tip);
        Assert.True(y.WorktreeExists);
        Assert.Equal([" M y.txt", "?? new.txt"], y.UncommittedFiles);
        Assert.Equal([new DiffFile("y.txt", 1, 0)], y.Uncommitted!.Files);
        Assert.Contains("diff --git a/y.txt b/y.txt", y.UncommittedDiff);
        Assert.Contains("+y2", y.UncommittedDiff);
        Assert.Equal([new DiffFile("y.txt", 1, 0)], y.Committed!.Files);
        Assert.Contains("+y1", y.CommittedDiff);
        Assert.DoesNotContain("+y2", y.CommittedDiff);
        Assert.Equal([(GitCommitKind.Attempt, 1)], y.Commits.Select(commit => (commit.Kind, commit.Attempt)));
        Assert.Null(y.MergeCommit);
        Assert.Empty(y.ArchiveBranches);

        Assert.Null(z.Branch);
        Assert.Null(z.Tip);
        Assert.Equal(repo.WorktreeOf("z"), z.WorktreePath, ignoreCase: true);
        Assert.False(z.WorktreeExists);
        Assert.Empty(z.UncommittedFiles);
        Assert.Null(z.Uncommitted);
        Assert.Null(z.Committed);
        Assert.Empty(z.Commits);
        Assert.Null(z.MergeCommit);
        Assert.Empty(z.ArchiveBranches);
        Assert.Null(z.CommittedDiff);
        Assert.Null(z.UncommittedDiff);
    }

    [Fact]
    public void ReadNow_reuses_the_diffs_of_an_unchanged_task_and_reads_them_again_after_a_commit()
    {
        using var repo = new TempGitRepo();
        using var reader = new GitReader();
        var tasks = Tasks(repo.Sha("orch/integration"));

        var first = reader.ReadNow(repo.Root, Plan, tasks, Now);
        var second = reader.ReadNow(repo.Root, Plan, tasks, Now.AddSeconds(5));

        Assert.Same(first.Tasks[1].CommittedDiff, second.Tasks[1].CommittedDiff);
        Assert.Same(first.Tasks[1].UncommittedDiff, second.Tasks[1].UncommittedDiff);
        Assert.Same(first.Tasks[1].Committed, second.Tasks[1].Committed);
        Assert.Same(first.Tasks[0].CommittedDiff, second.Tasks[0].CommittedDiff);

        repo.CommitFile(repo.WorktreeOf("y"), "y.txt", "y1\ny2\n", "orch(y): Task y", "Attempt 2.");
        var third = reader.ReadNow(repo.Root, Plan, tasks, Now.AddSeconds(10));

        Assert.NotSame(second.Tasks[1].CommittedDiff, third.Tasks[1].CommittedDiff);
        Assert.Contains("+y2", third.Tasks[1].CommittedDiff);
        Assert.Equal([new DiffFile("y.txt", 2, 0)], third.Tasks[1].Committed!.Files);
        Assert.Equal(["?? new.txt"], third.Tasks[1].UncommittedFiles);
        Assert.Empty(third.Tasks[1].Uncommitted!.Files);
        Assert.Same(second.Tasks[0].CommittedDiff, third.Tasks[0].CommittedDiff);
    }

    [Fact]
    public void Read_returns_at_once_and_starts_one_background_read_per_five_seconds()
    {
        using var repo = new TempGitRepo();
        using var reader = new GitReader();
        var tasks = Tasks(repo.Sha("orch/integration"));

        Assert.Same(GitInfo.Empty, reader.Read(repo.Root, Plan, tasks, Now));
        reader.Read(repo.Root, Plan, tasks, Now.AddSeconds(1));
        var read = WaitFor(() => reader.Read(repo.Root, Plan, tasks, Now.AddSeconds(1)), info => info != GitInfo.Empty);

        Assert.Equal(Now, read.ReadAt);
        Assert.Null(read.Problem);
        Assert.Equal(3, read.Tasks.Length);
        AssertNoReadStarts(() => reader.Read(repo.Root, Plan, tasks, Now.AddSeconds(4)), read);

        reader.Read(repo.Root, Plan, tasks, Now.AddSeconds(5));
        var next = WaitFor(() => reader.Read(repo.Root, Plan, tasks, Now.AddSeconds(5)), info => info != read);
        Assert.Equal(Now.AddSeconds(5), next.ReadAt);
    }

    [Fact]
    public void ReadNow_cuts_a_diff_longer_than_a_million_characters()
    {
        using var repo = new TempGitRepo();
        using var reader = new GitReader();
        var big = string.Concat(Enumerable.Repeat(new string('b', 99) + "\n", 15_000));
        repo.CommitFile(repo.WorktreeOf("y"), "big.txt", big, "orch(y): Task y", "Attempt 2.");

        var info = reader.ReadNow(repo.Root, Plan, Tasks(repo.Sha("orch/integration")), Now);

        var y = info.Tasks[1];
        Assert.Null(info.Problem);
        Assert.Equal(GitOutput.DiffLimit + GitOutput.DiffCutSuffix.Length, y.CommittedDiff!.Length);
        Assert.EndsWith(GitOutput.DiffCutSuffix, y.CommittedDiff);
        Assert.Contains(new DiffFile("big.txt", 15_000, 0), y.Committed!.Files);
    }

    [Fact]
    public void ReadNow_uses_the_repo_beside_a_runs_folder()
    {
        using var repo = new TempGitRepo();
        using var reader = new GitReader();
        var runDir = Path.Combine(repo.Temp, "repo.runs", "20261006-120000");
        Directory.CreateDirectory(runDir);

        var info = reader.ReadNow(runDir, Plan, Tasks(repo.Sha("orch/integration")), Now);

        Assert.Null(info.Problem);
        Assert.Equal(repo.Root, info.RepoRoot, ignoreCase: true);
        Assert.Equal(4, info.Worktrees.Length);
        Assert.True(info.Tasks[1].WorktreeExists);
    }

    [Theory]
    [InlineData("other.runs")]
    [InlineData("plain")]
    public void ReadNow_outside_a_work_tree_gives_only_the_row_1_problem(string parent)
    {
        using var repo = new TempGitRepo();
        using var reader = new GitReader();
        var runDir = Path.Combine(repo.Temp, parent, "20261006-120000");
        Directory.CreateDirectory(runDir);

        var info = reader.ReadNow(runDir, Plan, Tasks(repo.Sha("orch/integration")), Now);

        Assert.Equal(GitInfo.Empty with { ReadAt = Now, Problem = info.Problem }, info);
        Assert.Single(info.Problem!.Split('\n'));
        Assert.StartsWith("git rev-parse --show-toplevel: fatal: ", info.Problem);
    }

    [Fact]
    public void A_git_that_cannot_start_gives_empty_data_and_no_read_for_60_seconds()
    {
        var folder = Path.GetTempPath();
        using var reader = new GitReader(gitPath: "no-such-git");
        ImmutableArray<TaskView> tasks = [Task("x")];

        var now = reader.ReadNow(folder, Plan, tasks, Now);
        Assert.Equal(GitInfo.Empty with { ReadAt = Now, Problem = now.Problem }, now);
        Assert.StartsWith("git rev-parse --show-toplevel: ", now.Problem);
        Assert.Single(now.Problem!.Split('\n'));

        using var background = new GitReader(gitPath: "no-such-git");
        Assert.Same(GitInfo.Empty, background.Read(folder, Plan, tasks, Now));
        var first = WaitFor(() => background.Read(folder, Plan, tasks, Now), info => info != GitInfo.Empty);
        Assert.Equal(now, first);

        AssertNoReadStarts(() => background.Read(folder, Plan, tasks, Now.AddSeconds(10)), first);
        AssertNoReadStarts(() => background.Read(folder, Plan, tasks, Now.AddSeconds(59)), first);

        background.Read(folder, Plan, tasks, Now.AddSeconds(60));
        var later = WaitFor(() => background.Read(folder, Plan, tasks, Now.AddSeconds(60)), info => info != first);
        Assert.Equal(Now.AddSeconds(60), later.ReadAt);
    }

    [Fact]
    public void A_failing_command_adds_its_problem_line_and_the_rest_is_read()
    {
        using var repo = new TempGitRepo();
        using var reader = new GitReader();
        const string missing = "0123456789abcdef0123456789abcdef01234567";

        var info = reader.ReadNow(repo.Root, Plan, [Task("x", missing), Task("y")], Now);

        var lines = info.Problem!.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith($"git diff --numstat {missing}^1 {missing}: fatal: ", lines[0]);
        Assert.StartsWith($"git diff {missing}^1 {missing}: fatal: ", lines[1]);

        var x = info.Tasks[0];
        Assert.Null(x.Committed);
        Assert.Null(x.CommittedDiff);
        Assert.Equal("orch/task/x", x.Branch);
        Assert.Equal(2, x.Commits.Length);
        Assert.NotNull(x.Uncommitted);
        Assert.Equal(5, info.Branches.Length);
        Assert.Equal([new DiffFile("y.txt", 1, 0)], info.Tasks[1].Committed!.Files);

        // The failed diffs are not reused: the next read runs them again and reports them again.
        var again = reader.ReadNow(repo.Root, Plan, [Task("x", missing), Task("y")], Now.AddSeconds(5));
        Assert.Equal(info.Problem, again.Problem);
    }

    [Fact]
    public void Dispose_returns_at_once_and_starts_no_further_read()
    {
        using var repo = new TempGitRepo();
        var reader = new GitReader();
        var tasks = Tasks(repo.Sha("orch/integration"));

        reader.Read(repo.Root, Plan, tasks, Now);
        var clock = Stopwatch.StartNew();
        reader.Dispose();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"Dispose took {clock.Elapsed}");

        // The read started before Dispose may still complete; none starts after it.
        var last = WaitFor(() => reader.Read(repo.Root, Plan, tasks, Now.AddSeconds(10)), info => info != GitInfo.Empty);
        Assert.Equal(Now, last.ReadAt);
        AssertNoReadStarts(() => reader.Read(repo.Root, Plan, tasks, Now.AddSeconds(20)), last);
    }

    private static ImmutableArray<TaskView> Tasks(string mergedShaOfX) => [Task("x", mergedShaOfX), Task("y"), Task("z")];

    private static TaskView Task(string id, string? mergedSha = null) =>
        new(id, $"Task {id}", "", [], [], null, null, 1, 0, mergedSha is null ? TaskState.Running : TaskState.Done,
            "normal", 1, 0, 0, 0, null, null, null, null, null, null, null, mergedSha, "");

    private static void AssertWorktree(GitInfo info, string path, string branch, string head)
    {
        var worktree = Assert.Single(info.Worktrees, worktree => GitOutput.SamePath(worktree.Path, path));
        Assert.Equal(branch, worktree.Branch);
        Assert.Equal(head, worktree.Head);
    }

    // Polls read until done says yes, for up to 10 seconds.
    private static GitInfo WaitFor(Func<GitInfo> read, Func<GitInfo, bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var info = read();
            if (done(info))
                return info;
            Assert.True(clock.Elapsed < WaitLimit, "no read completed within 10 seconds");
            Thread.Sleep(20);
        }
    }

    // read keeps returning the same result for a while, so it started no read.
    private static void AssertNoReadStarts(Func<GitInfo> read, GitInfo expected)
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.Same(expected, read());
            Thread.Sleep(50);
        }
    }
}

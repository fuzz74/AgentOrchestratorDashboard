using System.Text;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class SampleRunInsightTests
{
    private const string Worktrees = @"C:\Work\SampleRepo.worktrees\";

    private readonly RunSnapshot _run = SampleRun.CreateInsight();

    private GitTask GitTask(string id) => _run.Git.Tasks.Single(t => t.TaskId == id);

    [Fact]
    public void Only_git_processes_and_commands_differ_from_CreateEnriched()
    {
        Assert.Equivalent(
            SampleRun.CreateEnriched(),
            _run with { Git = GitInfo.Empty, Processes = ProcessInfo.Empty, Commands = [] },
            strict: true);
        Assert.Equal(1, _run.Version);
        Assert.Equal(SampleRun.At(12, 30, 0), _run.ReadAt);
    }

    [Fact]
    public void Every_call_returns_a_new_snapshot_with_equal_content()
    {
        var other = SampleRun.CreateInsight();

        Assert.NotSame(_run, other);
        Assert.NotSame(_run.Git, other.Git);
        Assert.NotSame(_run.Processes, other.Processes);
        Assert.NotSame(_run.Commands[0], other.Commands[0]);
        Assert.Equivalent(_run, other, strict: true);
    }

    [Fact]
    public void Git_header_names_the_repo_and_the_integration_and_base_branches()
    {
        var git = _run.Git;

        Assert.Equal(SampleRun.At(12, 30, 0), git.ReadAt);
        Assert.Equal(@"C:\Work\SampleRepo", git.RepoRoot);
        Assert.Equal(("orch/integration", "3f9c2e1"), (git.IntegrationBranch, git.IntegrationTip));
        Assert.Equal(("main", "a1b2c3d"), (git.BaseBranch, git.BaseTip));
        Assert.Null(git.Problem);
    }

    [Fact]
    public void Git_has_four_worktrees_and_six_branches()
    {
        Assert.Equal(
        [
            new GitWorktree(Worktrees + "_integration", "orch/integration", "3f9c2e1"),
            new GitWorktree(Worktrees + "alpha", "orch/task/alpha", "5d6e7f8"),
            new GitWorktree(Worktrees + "beta", "orch/task/beta", "7a8b9c0"),
            new GitWorktree(Worktrees + "gamma", "orch/task/gamma", "b1c2d3e"),
        ],
        _run.Git.Worktrees);
        Assert.Equal(
        [
            ("main", "a1b2c3d", GitBranchKind.Base),
            ("orch/integration", "3f9c2e1", GitBranchKind.Integration),
            ("orch/task/alpha", "5d6e7f8", GitBranchKind.Task),
            ("orch/task/beta", "7a8b9c0", GitBranchKind.Task),
            ("orch/task/gamma", "b1c2d3e", GitBranchKind.Task),
            ("orch/archive/gamma/20261003-121500000", "0a1b2c3", GitBranchKind.Archive),
        ],
        _run.Git.Branches.Select(b => (b.Name, b.Tip, b.Kind)));
        Assert.All(_run.Git.Branches, b => Assert.InRange(b.CommittedAt!.Value, SampleRun.At(11, 55, 0), SampleRun.At(12, 30, 0)));
    }

    [Fact]
    public void Git_tasks_follow_the_task_order_with_their_worktree_paths()
    {
        Assert.Equal(_run.Tasks.Select(t => t.Id), _run.Git.Tasks.Select(t => t.TaskId));
        Assert.Equal(["alpha", "gamma", "beta", "delta", "epsilon"], _run.Git.Tasks.Select(t => t.TaskId));
        Assert.All(_run.Git.Tasks, t => Assert.Equal(Worktrees + t.TaskId, t.WorktreePath));
    }

    [Fact]
    public void Commits_have_full_shas_and_short_shas_of_seven()
    {
        var commits = _run.Git.Tasks.SelectMany(t => t.Commits).Append(GitTask("alpha").MergeCommit!);

        Assert.All(commits, c =>
        {
            Assert.Matches("^[0-9a-f]{40}$", c.Sha);
            Assert.Equal(c.Sha[..7], c.ShortSha);
        });
    }

    [Fact]
    public void Alpha_has_one_attempt_commit_a_merge_and_two_committed_files()
    {
        var alpha = GitTask("alpha");

        Assert.Equal(("orch/task/alpha", "5d6e7f8"), (alpha.Branch, alpha.Tip));
        Assert.True(alpha.WorktreeExists);
        Assert.Empty(alpha.UncommittedFiles);
        Assert.Empty(alpha.Uncommitted!.Files);
        Assert.Equal("", alpha.UncommittedDiff);

        var commit = Assert.Single(alpha.Commits);
        Assert.Equal(
            (GitCommitKind.Attempt, "orch(alpha): Alpha parser", "Attempt 1.", (int?)1, "alpha", SampleRun.At(12, 8, 40)),
            (commit.Kind, commit.Subject, commit.Body, commit.Attempt, commit.TaskId, commit.Time));
        Assert.StartsWith("5d6e7f8", commit.Sha, StringComparison.Ordinal);

        var merge = alpha.MergeCommit!;
        Assert.Equal("3f9c2e1", merge.ShortSha);
        Assert.Equal(_run.Tasks.Single(t => t.Id == "alpha").MergedSha, merge.ShortSha);
        Assert.Equal(
            (GitCommitKind.Merge, "Merge task alpha: Alpha parser", (int?)null, "alpha", SampleRun.At(12, 9, 25)),
            (merge.Kind, merge.Subject, merge.Attempt, merge.TaskId, merge.Time));

        Assert.Equal(
            [new DiffFile("src/Alpha/Parser.cs", 38, 0), new DiffFile("tests/Alpha/ParserTests.cs", 22, 3)],
            alpha.Committed!.Files);
        Assert.Equal((60, 3), (alpha.Committed.Added, alpha.Committed.Removed));
        AssertDiffSections(alpha.CommittedDiff, alpha.Committed);
        Assert.Empty(alpha.ArchiveBranches);
    }

    [Fact]
    public void Gamma_has_three_attempts_one_sync_and_an_archive_branch()
    {
        var gamma = GitTask("gamma");

        Assert.Equal(("orch/task/gamma", "b1c2d3e"), (gamma.Branch, gamma.Tip));
        Assert.True(gamma.WorktreeExists);
        Assert.Empty(gamma.UncommittedFiles);
        Assert.Empty(gamma.Uncommitted!.Files);
        Assert.Equal("", gamma.UncommittedDiff);
        Assert.Equal(
        [
            (SampleRun.At(12, 3, 0), GitCommitKind.Attempt, "orch(gamma): Gamma formatter", (int?)1),
            (SampleRun.At(12, 10, 0), GitCommitKind.Sync, "Merge branch 'orch/integration' into orch/task/gamma", null),
            (SampleRun.At(12, 11, 0), GitCommitKind.Attempt, "orch(gamma): Gamma formatter", 2),
            (SampleRun.At(12, 18, 0), GitCommitKind.Attempt, "orch(gamma): Gamma formatter", 3),
        ],
        gamma.Commits.Select(c => (c.Time, c.Kind, c.Subject, c.Attempt)));
        Assert.All(gamma.Commits, c => Assert.Equal("gamma", c.TaskId));
        Assert.Equal("b1c2d3e", gamma.Commits[^1].ShortSha);
        Assert.Null(gamma.MergeCommit);
        Assert.Equal([new DiffFile("src/Gamma/Formatter.cs", 80, 0)], gamma.Committed!.Files);
        AssertDiffSections(gamma.CommittedDiff, gamma.Committed);
        Assert.Equal(["orch/archive/gamma/20261003-121500000"], gamma.ArchiveBranches);
    }

    [Fact]
    public void Beta_has_two_uncommitted_lines_and_nothing_committed()
    {
        var beta = GitTask("beta");

        Assert.Equal(("orch/task/beta", "7a8b9c0"), (beta.Branch, beta.Tip));
        Assert.True(beta.WorktreeExists);
        Assert.Equal([" M src/Beta/Parser.cs", "?? src/Beta/Lexer.cs"], beta.UncommittedFiles);
        Assert.Equal([new DiffFile("src/Beta/Parser.cs", 12, 3)], beta.Uncommitted!.Files);
        AssertDiffSections(beta.UncommittedDiff, beta.Uncommitted);
        Assert.Empty(beta.Commits);
        Assert.Empty(beta.Committed!.Files);
        Assert.Equal("", beta.CommittedDiff);
        Assert.Null(beta.MergeCommit);
        Assert.Empty(beta.ArchiveBranches);
    }

    [Theory]
    [InlineData("delta")]
    [InlineData("epsilon")]
    public void Tasks_without_a_branch_have_nulls_and_empty_arrays(string id)
    {
        var task = GitTask(id);

        Assert.Null(task.Branch);
        Assert.Null(task.Tip);
        Assert.False(task.WorktreeExists);
        Assert.Empty(task.UncommittedFiles);
        Assert.Null(task.Uncommitted);
        Assert.Null(task.Committed);
        Assert.Empty(task.Commits);
        Assert.Null(task.MergeCommit);
        Assert.Empty(task.ArchiveBranches);
        Assert.Null(task.CommittedDiff);
        Assert.Null(task.UncommittedDiff);
    }

    [Fact]
    public void One_process_is_the_running_beta_worker()
    {
        var processes = _run.Processes;
        var betaWorker = _run.Sessions.Single(s => s.Files.Key == SampleRun.BetaWorkerKey);

        Assert.Equal(SampleRun.At(12, 30, 0), processes.SampledAt);
        Assert.Null(processes.Problem);
        var process = Assert.Single(processes.Processes);
        Assert.Equal(
            new AgentProcess(4242, "claude.exe", "claude -p --output-format stream-json --verbose --name orch:beta",
                SampleRun.At(12, 10, 0), 367_001_600, 0.12, "beta", AgentRole.Worker, "9a3c5e71-8b2d-4f60-a4c9-1d7e0b2f6a58"),
            process);
        Assert.Equal(SampleRun.BetaWorkerPid, process.Pid);
        Assert.Equal(SessionState.Running, betaWorker.State);
        Assert.Equal(betaWorker.Content.SessionId, process.SessionId);
    }

    [Fact]
    public void The_beta_process_line_reads_as_the_spec_shows()
    {
        var process = _run.Processes.Processes[0];

        var line = $"pid {process.Pid} · up {Look.Span(_run.ReadAt - process.StartedAt!.Value)} · " +
            $"cpu {Look.Percent(process.CpuShare!.Value)} · mem {Look.Bytes(process.WorkingSetBytes)}";

        Assert.Equal("pid 4242 · up 20m00s · cpu 12 % · mem 367.0 MB", line);
    }

    [Fact]
    public void Commands_are_the_eleven_logs_newest_first()
    {
        const string setup = "dotnet restore Sample.slnx";
        const string check = "dotnet build Sample.slnx -warnaserror && dotnet test Sample.slnx --no-build";
        const string start = "20261003-120005";
        const string bootstrap = "bootstrap-20261003-115500";

        Assert.Equal(
        [
            ($"gamma/{start}/attempt-3-acceptance.log", CommandKind.Acceptance, "gamma", start, (int?)3, SampleRun.At(12, 19, 50), "dotnet test tests/Gamma", CommandOutcome.Failed, (int?)1),
            ($"gamma/{start}/attempt-2-acceptance.log", CommandKind.Acceptance, "gamma", start, 2, SampleRun.At(12, 12, 30), "dotnet test tests/Gamma", CommandOutcome.Failed, 1),
            ("beta/20261003-121000/setup.log", CommandKind.Setup, "beta", "20261003-121000", null, SampleRun.At(12, 10, 5), setup, CommandOutcome.Passed, null),
            ("alpha-integration-check.log", CommandKind.IntegrationCheck, "alpha", null, null, SampleRun.At(12, 9, 28), check, CommandOutcome.Passed, null),
            ("alpha-integration-setup.log", CommandKind.IntegrationSetup, "alpha", null, null, SampleRun.At(12, 9, 26), setup, CommandOutcome.Passed, null),
            ($"alpha/{start}/attempt-1-acceptance.log", CommandKind.Acceptance, "alpha", start, 1, SampleRun.At(12, 8, 50), "dotnet test tests/Alpha", CommandOutcome.Passed, null),
            ($"gamma/{start}/attempt-1-acceptance.log", CommandKind.Acceptance, "gamma", start, 1, SampleRun.At(12, 4, 30), "dotnet test tests/Gamma", CommandOutcome.Failed, 1),
            ($"gamma/{start}/setup.log", CommandKind.Setup, "gamma", start, null, SampleRun.At(12, 0, 10), setup, CommandOutcome.Passed, null),
            ($"alpha/{start}/setup.log", CommandKind.Setup, "alpha", start, null, SampleRun.At(12, 0, 8), setup, CommandOutcome.Passed, null),
            ($"{bootstrap}/attempt-1-integration-check.log", CommandKind.BootstrapCheck, null, bootstrap, 1, SampleRun.At(11, 56, 0), check, CommandOutcome.Passed, null),
            ($"{bootstrap}/attempt-1-setup.log", CommandKind.BootstrapSetup, null, bootstrap, 1, SampleRun.At(11, 55, 30), setup, CommandOutcome.Passed, null),
        ],
        _run.Commands.Select(c => (c.Key, c.Kind, c.TaskId, c.StartFolder, c.Attempt, c.WrittenAt, c.Command, c.Outcome, c.ExitCode)));
        Assert.Equal(_run.Commands.OrderByDescending(c => c.WrittenAt), _run.Commands);
    }

    [Fact]
    public void Acceptance_commands_are_those_of_the_tasks()
    {
        Assert.All(_run.Commands.Where(c => c.Kind == CommandKind.Acceptance), c =>
            Assert.Equal(_run.Tasks.Single(t => t.Id == c.TaskId).Acceptance, c.Command));
    }

    [Fact]
    public void Command_logs_have_paths_texts_and_lengths()
    {
        Assert.All(_run.Commands, c =>
        {
            Assert.Equal(SampleRun.LogsDir + c.Key.Replace('/', '\\'), c.Path);
            Assert.Equal(c.Path + ".stderr", c.StderrPath);
            Assert.InRange(c.Text.Split('\n').Length, 3, 6);
            Assert.Equal(Encoding.UTF8.GetByteCount(c.Text), c.Length);
        });
        Assert.Equal("error: 1 test failed", _run.Commands[0].StderrText);
        Assert.All(_run.Commands.Skip(1), c => Assert.Equal("", c.StderrText));
    }

    [Fact]
    public void Bootstrap_logs_start_with_their_command()
    {
        Assert.All(_run.Commands.Where(c => c.TaskId is null), c =>
            Assert.Equal("> " + c.Command, c.Text.Split('\n')[0]));
    }

    // One "diff --git" section per file of the stat, in its order, each with ---, +++ and one @@ hunk.
    private static void AssertDiffSections(string? diff, DiffStat stat)
    {
        Assert.NotNull(diff);
        var sections = diff.Split("diff --git ")[1..];
        Assert.Equal(stat.Files.Length, sections.Length);
        Assert.All(sections.Zip(stat.Files), pair =>
        {
            var (section, file) = pair;
            var lines = section.Split('\n');
            Assert.Equal($"a/{file.Path} b/{file.Path}", lines[0]);
            Assert.Contains($"--- a/{file.Path}", lines);
            Assert.Contains($"+++ b/{file.Path}", lines);
            Assert.Single(lines, l => l.StartsWith("@@ ", StringComparison.Ordinal));
        });
        Assert.StartsWith("diff --git ", diff, StringComparison.Ordinal);
    }
}

using OrchDash.Core.Git;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Core.Tests.Git;

public sealed class GitOutputTests
{
    private const char Us = '\x1f';
    private const char Rs = '\x1e';

    [Theory]
    [InlineData("C:/Work/Repo\n", @"C:\Work\Repo")]
    [InlineData("C:/Work/Repo\r\n", @"C:\Work\Repo")]
    [InlineData(@"  C:\Work\Repo  ", @"C:\Work\Repo")]
    public void ParseToplevel_trims_and_uses_backslashes(string stdout, string expected) =>
        Assert.Equal(expected, GitOutput.ParseToplevel(stdout));

    [Fact]
    public void ParseWorktrees_reads_each_block()
    {
        const string stdout =
            "worktree C:/Work/Repo\n" +
            "HEAD 1111111111111111111111111111111111111111\n" +
            "branch refs/heads/main\n" +
            "\n" +
            "worktree C:/Work/Repo.worktrees/x\n" +
            "HEAD 2222222222222222222222222222222222222222\n" +
            "detached\n" +
            "\n" +
            "worktree C:/Work/Repo.worktrees/y\n" +
            "HEAD 3333333333333333333333333333333333333333\n" +
            "branch refs/heads/orch/task/y\n" +
            "locked\n" +
            "\n";

        var worktrees = GitOutput.ParseWorktrees(stdout);

        Assert.Equal(
            [
                new GitWorktree(@"C:\Work\Repo", "main", "1111111111111111111111111111111111111111"),
                new GitWorktree(@"C:\Work\Repo.worktrees\x", null, "2222222222222222222222222222222222222222"),
                new GitWorktree(@"C:\Work\Repo.worktrees\y", "orch/task/y", "3333333333333333333333333333333333333333"),
            ],
            worktrees);
    }

    [Fact]
    public void ParseWorktrees_accepts_crlf_and_a_last_block_without_an_empty_line()
    {
        const string stdout =
            "worktree C:/Work/Repo\r\nHEAD abc\r\nbranch refs/heads/main\r\n\r\n" +
            "worktree C:/Work/Repo.worktrees/x\r\nHEAD def\r\nbranch refs/heads/orch/task/x\r\nprunable gitdir file points to non-existent location";

        var worktrees = GitOutput.ParseWorktrees(stdout);

        Assert.Equal(
            [
                new GitWorktree(@"C:\Work\Repo", "main", "abc"),
                new GitWorktree(@"C:\Work\Repo.worktrees\x", "orch/task/x", "def"),
            ],
            worktrees);
    }

    [Fact]
    public void ParseWorktrees_of_empty_output_is_empty() => Assert.Empty(GitOutput.ParseWorktrees(""));

    [Fact]
    public void ParseBranches_gives_all_five_kinds()
    {
        const string stdout =
            "main\ta1b2c3d\t2026-10-03T11:55:00+02:00\r\n" +
            "orch/archive/gamma/20261003-121500000\t0a1b2c3\t2026-10-03T12:15:00+02:00\r\n" +
            "orch/task/alpha\t5d6e7f8\t2026-10-03T12:08:40+02:00\r\n" +
            "release/next\t9f9f9f9\tnot a date\r\n" +
            "trunk\t3f9c2e1\t2026-10-03T12:09:25Z\r\n";

        var branches = GitOutput.ParseBranches(stdout, "trunk", null);

        Assert.Equal(
            [
                new GitBranch("main", "a1b2c3d", new DateTimeOffset(2026, 10, 3, 11, 55, 0, TimeSpan.FromHours(2)), GitBranchKind.Other),
                new GitBranch("orch/archive/gamma/20261003-121500000", "0a1b2c3",
                    new DateTimeOffset(2026, 10, 3, 12, 15, 0, TimeSpan.FromHours(2)), GitBranchKind.Archive),
                new GitBranch("orch/task/alpha", "5d6e7f8", new DateTimeOffset(2026, 10, 3, 12, 8, 40, TimeSpan.FromHours(2)), GitBranchKind.Task),
                new GitBranch("release/next", "9f9f9f9", null, GitBranchKind.Other),
                new GitBranch("trunk", "3f9c2e1", new DateTimeOffset(2026, 10, 3, 12, 9, 25, TimeSpan.Zero), GitBranchKind.Integration),
            ],
            branches);

        Assert.Equal(GitBranchKind.Base, GitOutput.ParseBranches(stdout, "trunk", "main")[0].Kind);
    }

    [Theory]
    [InlineData("orch/task/x", GitBranchKind.Task)]
    [InlineData("orch/archive/x/20261006-120000000", GitBranchKind.Archive)]
    [InlineData("orch/integration", GitBranchKind.Integration)]
    [InlineData("main", GitBranchKind.Base)]
    [InlineData("feature", GitBranchKind.Other)]
    [InlineData("orch/task/", GitBranchKind.Other)]
    [InlineData("orch/archive/x", GitBranchKind.Other)]
    public void ParseBranches_kind(string name, GitBranchKind kind) =>
        Assert.Equal(kind, Assert.Single(GitOutput.ParseBranches($"{name}\tabc1234\t2026-10-06T12:00:00+02:00\n", "orch/integration", "main")).Kind);

    [Fact]
    public void ParseBranches_tests_task_before_integration()
    {
        var branch = Assert.Single(GitOutput.ParseBranches("orch/task/x\tabc\t\n", "orch/task/x", "orch/task/x"));

        Assert.Equal(GitBranchKind.Task, branch.Kind);
        Assert.Null(branch.CommittedAt);
    }

    [Fact]
    public void ParseCommits_maps_subjects_and_keeps_git_order()
    {
        var stdout =
            Record("c3", "c3s", "2026-10-03T12:18:00+02:00", "orch(gamma): Gamma formatter",
                "Attempt 3. Fixed the tests.\n\nSecond paragraph.\nAttempt 4.\n\n") + "\n" +
            Record("c2", "c2s", "2026-10-03T12:10:00+02:00", "Merge branch 'orch/integration' into orch/task/gamma", "") + "\n" +
            Record("c1", "c1s", "2026-10-03T12:09:25+02:00", "Merge task alpha: Alpha parser", "") + "\n" +
            Record("c0", "c0s", "2026-10-03T12:08:00+02:00", "Some other commit", "Attempt 1.") + "\n" +
            Record("cb", "cbs", "2026-10-03T12:05:00+02:00", "orch(alpha:review): Alpha parser", "No attempt line here.\n") + "\n";

        var commits = GitOutput.ParseCommits(stdout);

        Assert.Equal(
            [
                new GitCommit("c3", "c3s", new DateTimeOffset(2026, 10, 3, 12, 18, 0, TimeSpan.FromHours(2)), GitCommitKind.Attempt,
                    "orch(gamma): Gamma formatter", "Attempt 3. Fixed the tests.\n\nSecond paragraph.\nAttempt 4.", 3, "gamma"),
                new GitCommit("c2", "c2s", new DateTimeOffset(2026, 10, 3, 12, 10, 0, TimeSpan.FromHours(2)), GitCommitKind.Sync,
                    "Merge branch 'orch/integration' into orch/task/gamma", "", null, "gamma"),
                new GitCommit("c1", "c1s", new DateTimeOffset(2026, 10, 3, 12, 9, 25, TimeSpan.FromHours(2)), GitCommitKind.Merge,
                    "Merge task alpha: Alpha parser", "", null, "alpha"),
                new GitCommit("cb", "cbs", new DateTimeOffset(2026, 10, 3, 12, 5, 0, TimeSpan.FromHours(2)), GitCommitKind.Attempt,
                    "orch(alpha:review): Alpha parser", "No attempt line here.", null, "alpha:review"),
            ],
            commits);
    }

    [Fact]
    public void ParseCommits_accepts_crlf_and_drops_an_empty_last_record()
    {
        var stdout =
            Record("s1", "s1s", "2026-10-06T12:00:00Z", "orch(x): Task x", "Attempt 12.\r\nDetails\r\n") + "\r\n" +
            Record("s2", "s2s", "2026-10-06T12:01:00Z", "Merge task x: Task x", "") + "\r\n";

        var commits = GitOutput.ParseCommits(stdout);

        Assert.Equal(2, commits.Length);
        Assert.Equal("s1", commits[0].Sha);
        Assert.Equal("Attempt 12.\nDetails", commits[0].Body);
        Assert.Equal(12, commits[0].Attempt);
        Assert.Equal("x", commits[0].TaskId);
        Assert.Equal("s2", commits[1].Sha);
        Assert.Equal(GitCommitKind.Merge, commits[1].Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("  \r\n")]
    public void ParseCommits_of_empty_output_is_empty(string stdout) => Assert.Empty(GitOutput.ParseCommits(stdout));

    [Fact]
    public void ParseCommits_drops_a_record_with_an_unparsable_date()
    {
        var stdout = Record("s1", "s1s", "yesterday", "orch(x): Task x", "Attempt 1.") + "\n";

        Assert.Empty(GitOutput.ParseCommits(stdout));
    }

    [Fact]
    public void ParseLines_keeps_non_empty_lines_as_printed()
    {
        var lines = GitOutput.ParseLines(" M src/Beta/Parser.cs\r\n?? src/Beta/Lexer.cs\r\n\r\nR  a.txt -> b.txt\n");

        Assert.Equal([" M src/Beta/Parser.cs", "?? src/Beta/Lexer.cs", "R  a.txt -> b.txt"], lines);
        Assert.Empty(GitOutput.ParseLines(""));
    }

    [Fact]
    public void ParseNumstat_reads_counts_binary_files_and_renames()
    {
        const string stdout =
            "38\t0\tsrc/Alpha/Parser.cs\r\n" +
            "-\t-\tassets/logo.png\r\n" +
            "0\t0\tsrc/{Old => New}/File.cs\r\n" +
            "22\t3\ttests/Alpha/ParserTests.cs\r\n";

        var stat = GitOutput.ParseNumstat(stdout);

        Assert.Equal(
            [
                new DiffFile("src/Alpha/Parser.cs", 38, 0),
                new DiffFile("assets/logo.png", null, null),
                new DiffFile("src/{Old => New}/File.cs", 0, 0),
                new DiffFile("tests/Alpha/ParserTests.cs", 22, 3),
            ],
            stat.Files);
        Assert.Equal(60, stat.Added);
        Assert.Equal(3, stat.Removed);
    }

    [Fact]
    public void ParseNumstat_of_empty_output_is_an_empty_stat()
    {
        var stat = GitOutput.ParseNumstat("");

        Assert.False(stat.Files.IsDefault);
        Assert.Empty(stat.Files);
    }

    [Fact]
    public void CutDiff_keeps_a_diff_of_one_million_characters()
    {
        var diff = new string('a', 1_000_000);

        Assert.Same(diff, GitOutput.CutDiff(diff));
    }

    [Fact]
    public void CutDiff_cuts_a_longer_diff()
    {
        var diff = new string('a', 1_000_000) + "b";

        var cut = GitOutput.CutDiff(diff);

        const string suffix = "\n... cut at 1,000,000 characters";
        Assert.Equal(1_000_000 + suffix.Length, cut.Length);
        Assert.EndsWith(suffix, cut, StringComparison.Ordinal);
        Assert.Equal(new string('a', 1_000_000), cut[..1_000_000]);
    }

    [Fact]
    public void CutDiff_returns_a_short_diff_unchanged()
    {
        var diff = "diff --git a/x b/x\n";

        Assert.Same(diff, GitOutput.CutDiff(diff));
    }

    [Theory]
    [InlineData(@"C:\Work\Repo\", "x", @"C:\Work\Repo.worktrees\x")]
    [InlineData(@"C:\Work\Repo", "alpha", @"C:\Work\Repo.worktrees\alpha")]
    [InlineData("C:/Work/Repo/", "x", @"C:\Work\Repo.worktrees\x")]
    [InlineData(@"C:\Repo", "x", @"C:\Repo.worktrees\x")]
    public void WorktreePathOf_is_beside_the_repo(string repoRoot, string taskId, string expected) =>
        Assert.Equal(expected, GitOutput.WorktreePathOf(repoRoot, taskId));

    [Theory]
    [InlineData(@"C:\Work\Repo.worktrees\x", @"C:\Work\Repo.worktrees\x", true)]
    [InlineData(@"C:\Work\Repo.worktrees\x", "c:/work/repo.worktrees/X", true)]
    [InlineData(@"C:\Work\Repo.worktrees\x\", @"C:\Work\Repo.worktrees\x", true)]
    [InlineData(@"C:\Work\Repo.worktrees\x", @"C:\Work\Repo.worktrees\y", false)]
    [InlineData(@"C:\Work\Repo.worktrees\x", @"C:\Work\Repo.worktrees\xy", false)]
    public void SamePath_ignores_case_slashes_and_trailing_separators(string a, string b, bool same) =>
        Assert.Equal(same, GitOutput.SamePath(a, b));

    [Fact]
    public void OrderCommits_orders_by_time_then_sha()
    {
        var early = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(2));
        var sameInstant = new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
        var late = early.AddMinutes(5);
        GitCommit Commit(string sha, DateTimeOffset time) =>
            new(sha, sha, time, GitCommitKind.Attempt, "orch(x): X", "", null, "x");

        var ordered = GitOutput.OrderCommits([Commit("c", late), Commit("b", sameInstant), Commit("a", early), Commit("B", early)]);

        Assert.Equal(["B", "a", "b", "c"], ordered.Select(commit => commit.Sha));
    }

    private static string Record(string sha, string shortSha, string date, string subject, string body) =>
        $"{sha}{Us}{shortSha}{Us}{date}{Us}{subject}{Us}{body}{Rs}";
}

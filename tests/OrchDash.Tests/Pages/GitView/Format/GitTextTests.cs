using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.GitView.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.GitView.Format;

public sealed class GitTextTests
{
    private readonly RunSnapshot _run = SampleRun.CreateInsight();

    private TaskView Task(string id) => _run.Tasks.Single(t => t.Id == id);

    private GitTask Git(string id) => _run.Git.Tasks.Single(g => g.TaskId == id);

    private IReadOnlyList<string> Cells(string id) => GitText.TaskCells(Git(id), Task(id));

    [Fact]
    public void Header_shows_the_branches_worktrees_archive_branches_and_read_time()
    {
        Assert.Equal(
            ["integration orch/integration @3f9c2e1 · base main @a1b2c3d · 4 worktrees · 1 archive branches · read 12:30:00"],
            GitText.HeaderLines(_run.Git));
    }

    [Fact]
    public void Header_of_empty_git_info_shows_dashes()
    {
        Assert.Equal(
            ["integration - @- · base - @- · 0 worktrees · 0 archive branches · read -"],
            GitText.HeaderLines(GitInfo.Empty));
    }

    [Fact]
    public void Header_shows_the_first_problem_line_in_the_warning_colour()
    {
        var git = _run.Git with
        {
            Problem = "git status --porcelain: fatal: not a git repository\r\ngit diff HEAD: timed out",
        };

        Assert.Equal(
        [
            "integration orch/integration @3f9c2e1 · base main @a1b2c3d · 4 worktrees · 1 archive branches · read 12:30:00",
            "[warning]git unavailable: git status --porcelain: fatal: not a git repository[/]",
        ], GitText.HeaderLines(git));
    }

    [Fact]
    public void Header_escapes_branch_names()
    {
        var git = _run.Git with { IntegrationBranch = "orch/[int]" };

        Assert.StartsWith("integration orch/[[int]] @3f9c2e1 · ", GitText.HeaderLines(git)[0]);
    }

    [Fact]
    public void Cells_of_a_merged_task()
    {
        Assert.Equal(["✔", "alpha", "@5d6e7f8", "clean", "1 commit", "2 files +60 -3", "3f9c2e1"], Cells("alpha"));
    }

    [Fact]
    public void Cells_of_a_task_with_syncs_and_an_archive_branch_show_its_branch()
    {
        Assert.Equal(["✖", "gamma", "@b1c2d3e", "clean", "3 commits, 1 sync", "1 files +80 -0", "-"], Cells("gamma"));
    }

    [Fact]
    public void Cells_of_a_task_with_uncommitted_files()
    {
        Assert.Equal(["▶", "beta", "@7a8b9c0", "2 uncommitted", "0 commits", "0 files +0 -0", "-"], Cells("beta"));
    }

    [Fact]
    public void Cells_of_a_task_without_branch_and_worktree()
    {
        Assert.Equal(["⊘", "delta", "-", "-", "0 commits", "-", "-"], Cells("delta"));
        Assert.Equal(["·", "epsilon", "-", "-", "0 commits", "-", "-"], Cells("epsilon"));
    }

    [Fact]
    public void Cells_of_a_task_with_only_an_archive_branch_show_archived()
    {
        var git = Git("delta") with { ArchiveBranches = ["orch/archive/delta/20261003-121500000"] };

        Assert.Equal("archived", GitText.TaskCells(git, Task("delta"))[2]);
    }

    [Fact]
    public void Widths_fit_the_longest_cell_or_title_of_each_column()
    {
        var widths = GitText.Widths(_run.Git.Tasks.Select(g => GitText.TaskCells(g, Task(g.TaskId))));

        Assert.Equal([1, 7, 8, 13, 17, 14, 7], widths);
        Assert.Equal([1, 2, 6, 8, 7, 4, 6], GitText.Widths([]));
    }

    [Fact]
    public void Task_row_colours_only_the_icon_and_pads_the_cells()
    {
        var widths = GitText.Widths(_run.Git.Tasks.Select(g => GitText.TaskCells(g, Task(g.TaskId))));

        Assert.Equal(
            "[success]✔[/]  alpha    @5d6e7f8  clean          1 commit           2 files +60 -3  3f9c2e1",
            GitText.TaskRow(Git("alpha"), Task("alpha"), widths));
        Assert.Equal(
            "[muted]·[/]  epsilon  -         -              0 commits          -               -",
            GitText.TaskRow(Git("epsilon"), Task("epsilon"), widths));
    }

    [Fact]
    public void Task_header_is_muted_and_aligned_with_the_rows()
    {
        var widths = GitText.Widths(_run.Git.Tasks.Select(g => GitText.TaskCells(g, Task(g.TaskId))));
        var header = GitText.TaskHeader(widths);
        var row = Plain(GitText.TaskRow(Git("alpha"), Task("alpha"), widths));

        Assert.StartsWith("[muted]", header, StringComparison.Ordinal);
        Assert.EndsWith("[/]", header, StringComparison.Ordinal);
        string[] titles = ["Id", "Branch", "Worktree", "Commits", "Diff", "Merged"];
        string[] cells = ["alpha", "@5d6e7f8", "clean", "1 commit", "2 files", "3f9c2e1"];
        for (var i = 0; i < titles.Length; i++)
            Assert.Equal(row.IndexOf(cells[i], StringComparison.Ordinal), Plain(header).IndexOf(titles[i], StringComparison.Ordinal));
    }

    [Fact]
    public void Task_row_escapes_the_id_and_pads_by_its_plain_length()
    {
        var task = Task("delta") with { Id = "d[1]" };
        var git = Git("delta") with { TaskId = "d[1]" };
        var widths = GitText.Widths([GitText.TaskCells(git, task)]);

        Assert.Equal("[warning]⊘[/]  d[[1]]  -       -         0 commits  -     -", GitText.TaskRow(git, task, widths));
    }

    [Fact]
    public void Commit_rows_of_a_task_with_attempts_and_a_sync()
    {
        Assert.Equal(
        [
            "12:03:00 c4d5e6f attempt 1 orch(gamma): Gamma formatter",
            "12:10:00 d2e3f40 sync Merge branch 'orch/integration' into orch/task/gamma",
            "12:11:00 e8f90a1 attempt 2 orch(gamma): Gamma formatter",
            "12:18:00 b1c2d3e attempt 3 orch(gamma): Gamma formatter",
        ], Git("gamma").Commits.Select(GitText.CommitRow));
    }

    [Fact]
    public void Commit_row_without_attempt_number_and_of_a_merge()
    {
        var attempt = Git("alpha").Commits[0] with { Attempt = null, Subject = "orch(alpha): fix [x]" };

        Assert.Equal("12:08:40 5d6e7f8 attempt orch(alpha): fix [[x]]", GitText.CommitRow(attempt));
        Assert.Equal("12:09:25 3f9c2e1 merge Merge task alpha: Alpha parser", GitText.CommitRow(Git("alpha").MergeCommit!));
    }

    [Fact]
    public void File_rows_of_committed_files()
    {
        Assert.Equal(
        [
            new GitFileRow("+38 -0 src/Alpha/Parser.cs", "src/Alpha/Parser.cs", true),
            new GitFileRow("+22 -3 tests/Alpha/ParserTests.cs", "tests/Alpha/ParserTests.cs", true),
        ], GitText.FileRows(Git("alpha")));
    }

    [Fact]
    public void File_rows_of_uncommitted_files_are_the_porcelain_lines()
    {
        Assert.Equal(
        [
            new GitFileRow(" M src/Beta/Parser.cs", "src/Beta/Parser.cs", false),
            new GitFileRow("?? src/Beta/Lexer.cs", "src/Beta/Lexer.cs", false),
        ], GitText.FileRows(Git("beta")));
    }

    [Fact]
    public void File_rows_list_committed_files_before_uncommitted_ones()
    {
        var git = Git("beta") with { Committed = new DiffStat([new DiffFile("docs/[a].png", null, null)]) };

        Assert.Equal(
        [
            new GitFileRow("+- -- docs/[[a]].png", "docs/[a].png", true),
            new GitFileRow(" M src/Beta/Parser.cs", "src/Beta/Parser.cs", false),
            new GitFileRow("?? src/Beta/Lexer.cs", "src/Beta/Lexer.cs", false),
        ], GitText.FileRows(git));
    }

    [Fact]
    public void File_rows_of_a_task_without_changes_are_empty()
    {
        Assert.Empty(GitText.FileRows(Git("delta")));
        Assert.Equal("no changes", GitText.NoChanges);
        Assert.Equal("no commits", GitText.NoCommits);
        Assert.Equal("No plan yet", GitText.NoPlan);
    }

    [Theory]
    [InlineData(" M src/Beta/Parser.cs", "src/Beta/Parser.cs")]
    [InlineData("?? src/Beta/Lexer.cs", "src/Beta/Lexer.cs")]
    [InlineData("R  old.cs -> new.cs", "new.cs")]
    [InlineData("??", "")]
    public void Uncommitted_path_is_the_line_from_the_fourth_character_after_a_rename_arrow(string line, string path)
    {
        Assert.Equal(path, GitText.UncommittedPath(line));
    }

    [Fact]
    public void File_diff_returns_each_section_of_a_diff()
    {
        var diff = Git("alpha").CommittedDiff!;
        var second = diff.IndexOf("diff --git a/tests/", StringComparison.Ordinal);

        var parser = GitText.FileDiff(diff, "src/Alpha/Parser.cs");
        var tests = GitText.FileDiff(diff, "tests/Alpha/ParserTests.cs");

        Assert.Equal(diff[..(second - 1)], parser);
        Assert.StartsWith("diff --git a/src/Alpha/Parser.cs b/src/Alpha/Parser.cs\n", parser, StringComparison.Ordinal);
        Assert.EndsWith("\n+}", parser, StringComparison.Ordinal);
        Assert.Equal(diff[second..^1], tests);
        Assert.StartsWith("diff --git a/tests/Alpha/ParserTests.cs b/tests/Alpha/ParserTests.cs\n", tests, StringComparison.Ordinal);
        Assert.EndsWith("\n }", tests, StringComparison.Ordinal);
    }

    [Fact]
    public void File_diff_is_null_for_an_unknown_path_or_no_diff()
    {
        var diff = Git("alpha").CommittedDiff;

        Assert.Null(GitText.FileDiff(diff, "src/Alpha/Lexer.cs"));
        Assert.Null(GitText.FileDiff(diff, "Parser.cs"));
        Assert.Null(GitText.FileDiff("", "src/Alpha/Parser.cs"));
        Assert.Null(GitText.FileDiff(null, "src/Alpha/Parser.cs"));
    }

    [Fact]
    public void File_diff_finds_a_rename_by_its_new_path()
    {
        const string rename = "diff --git a/old.cs b/new.cs\nsimilarity index 90%\nrename from old.cs\nrename to new.cs";
        var diff = Git("alpha").CommittedDiff + rename + "\n";

        Assert.Equal(rename, GitText.FileDiff(diff, "new.cs"));
    }

    [Fact]
    public void Diff_popup_of_a_task_with_a_committed_diff()
    {
        var section = Assert.Single(GitText.DiffPopup(Git("alpha")));

        Assert.Equal(new PopupSection("Committed", Git("alpha").CommittedDiff!, TextKind.Diff), section);
        Assert.Equal("Diff: alpha", GitText.DiffTitle("alpha"));
    }

    [Fact]
    public void Diff_popup_of_a_task_with_an_uncommitted_diff()
    {
        var section = Assert.Single(GitText.DiffPopup(Git("beta")));

        Assert.Equal(new PopupSection("Uncommitted", Git("beta").UncommittedDiff!, TextKind.Diff), section);
    }

    [Fact]
    public void Diff_popup_of_a_task_without_diff()
    {
        Assert.Equal([new PopupSection("Diff", "no diff")], GitText.DiffPopup(Git("delta")));
    }

    [Fact]
    public void Diff_popup_of_a_task_with_both_diffs()
    {
        var git = Git("beta") with { CommittedDiff = Git("gamma").CommittedDiff };

        Assert.Equal(
        [
            new PopupSection("Committed", Git("gamma").CommittedDiff!, TextKind.Diff),
            new PopupSection("Uncommitted", Git("beta").UncommittedDiff!, TextKind.Diff),
        ], GitText.DiffPopup(git));
    }

    [Fact]
    public void File_diff_popup_of_a_committed_file()
    {
        var git = Git("alpha");
        var row = GitText.FileRows(git)[0];

        Assert.Equal(
            [new PopupSection("Diff", GitText.FileDiff(git.CommittedDiff, "src/Alpha/Parser.cs")!, TextKind.Diff)],
            GitText.FileDiffPopup(git, row));
        Assert.Equal("Diff: alpha src/Alpha/Parser.cs", GitText.FileDiffTitle("alpha", row.Path));
    }

    [Fact]
    public void File_diff_popup_of_an_uncommitted_file()
    {
        var git = Git("beta");
        var rows = GitText.FileRows(git);

        Assert.Equal([new PopupSection("Diff", git.UncommittedDiff!.TrimEnd('\n'), TextKind.Diff)],
            GitText.FileDiffPopup(git, rows[0]));
        Assert.Equal([new PopupSection("Diff", "no diff for this file")], GitText.FileDiffPopup(git, rows[1]));
    }

    [Fact]
    public void Commit_popup_lists_every_value()
    {
        var commit = Git("alpha").Commits[0];

        Assert.Equal(
        [
            new PopupSection("Commit",
                "sha: 5d6e7f8a9b0c1d2e3f405162738495a6b7c8d9e0\n" +
                "time: 2026-10-03 12:08\n" +
                "kind: attempt\n" +
                "attempt: 1\n" +
                "subject: orch(alpha): Alpha parser\n" +
                "body: Attempt 1."),
        ], GitText.CommitPopup(commit));
        Assert.Equal("5d6e7f8 orch(alpha): Alpha parser", GitText.CommitTitle(commit));
    }

    [Fact]
    public void Commit_popup_of_a_sync_shows_dashes_and_keeps_brackets_plain()
    {
        var sync = Git("gamma").Commits[1] with { Subject = "Merge branch '[x]' into orch/task/gamma" };

        var section = Assert.Single(GitText.CommitPopup(sync));

        Assert.Equal(
            "sha: d2e3f405162738495a6b7c8d9eafb0c1d2e3f405\n" +
            "time: 2026-10-03 12:10\n" +
            "kind: sync\n" +
            "attempt: -\n" +
            "subject: Merge branch '[x]' into orch/task/gamma\n" +
            "body: -",
            section.Text);
        Assert.Equal("d2e3f40 Merge branch '[x]' into orch/task/gamma", GitText.CommitTitle(sync));
    }

    [Fact]
    public void Commit_popup_of_a_merge_commit()
    {
        var section = Assert.Single(GitText.CommitPopup(Git("alpha").MergeCommit!));

        Assert.Contains("\nkind: merge\nattempt: -\n", section.Text, StringComparison.Ordinal);
    }

    private static string Plain(string markup) =>
        markup.Replace("[muted]", "", StringComparison.Ordinal)
            .Replace("[success]", "", StringComparison.Ordinal)
            .Replace("[/]", "", StringComparison.Ordinal);
}

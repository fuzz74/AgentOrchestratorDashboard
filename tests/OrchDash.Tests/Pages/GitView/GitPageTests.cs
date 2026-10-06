using System.Text.RegularExpressions;
using OrchDash.Core.Model;
using OrchDash.Pages.GitView;
using OrchDash.Shell;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.Pages.GitView;

public sealed class GitPageTests
{
    private const string Header =
        "integration orch/integration @3f9c2e1 · base main @a1b2c3d · 4 worktrees · 1 archive branches · read 12:30:00";
    private const string AlphaCommit = "12:08:40 5d6e7f8 attempt 1 orch(alpha): Alpha parser";
    private const string AlphaParserFile = "+38 -0 src/Alpha/Parser.cs";
    private const string AlphaTestsFile = "+22 -3 tests/Alpha/ParserTests.cs";
    private const string GammaSync = "12:10:00 d2e3f40 sync Merge branch 'orch/integration' into orch/task/gamma";

    private static UiTestHost Start(RunSnapshot? snapshot = null) =>
        UiTestHost.Start([new GitPage()], snapshot ?? SampleRun.CreateInsight());

    [Fact]
    public void The_page_shows_the_header_the_task_table_and_the_commits_and_files_of_the_first_task()
    {
        using var host = Start();
        var frame = host.Frame();

        Assert.Contains(Header, frame, StringComparison.Ordinal);
        Assert.DoesNotContain("git unavailable", frame, StringComparison.Ordinal);
        Assert.Equal(["Id", "Branch", "Worktree", "Commits", "Diff", "Merged"], Cells(PaneLines(frame, "Tasks")[0]));
        var rows = TaskRows(frame);
        Assert.Equal(["alpha", "gamma", "beta", "delta", "epsilon"], rows.Select(row => row[1]));
        Assert.Equal(["✔", "alpha", "@5d6e7f8", "clean", "1 commit", "2 files +60 -3", "3f9c2e1"], rows[0]);
        Assert.Equal(["✖", "gamma", "@b1c2d3e", "clean", "3 commits, 1 sync", "1 files +80 -0", "-"], rows[1]);
        Assert.Equal(["▶", "beta", "@7a8b9c0", "2 uncommitted"], rows[2][..4]);
        Assert.Equal(["⊘", "delta", "-", "-"], rows[3][..4]);
        Assert.DoesNotContain("archived", frame, StringComparison.Ordinal);
        Assert.Equal("alpha", Cells(Selected(host, "Tasks"))[1]);

        Assert.Equal([AlphaCommit], Rows(host, "Commits"));
        Assert.Equal(AlphaCommit, Selected(host, "Commits"));
        Assert.Equal([AlphaParserFile, AlphaTestsFile], Rows(host, "Files"));
        Assert.Equal(AlphaParserFile, Selected(host, "Files"));
        Assert.Contains("[Tab] Switch list", CommandBar(host), StringComparison.Ordinal);
        Assert.Contains("[Enter] Diff", CommandBar(host), StringComparison.Ordinal);
        host.SaveSvg("git");
    }

    [Fact]
    public void Down_selects_the_next_task_and_shows_its_commits_and_files()
    {
        using var host = Start();

        host.Press(TerminalKey.Down);
        Assert.Equal("gamma", Cells(Selected(host, "Tasks"))[1]);
        Assert.Equal(
        [
            "12:03:00 c4d5e6f attempt 1 orch(gamma): Gamma formatter",
            GammaSync,
            "12:11:00 e8f90a1 attempt 2 orch(gamma): Gamma formatter",
            "12:18:00 b1c2d3e attempt 3 orch(gamma): Gamma formatter",
        ], Rows(host, "Commits"));
        Assert.Equal(["+80 -0 src/Gamma/Formatter.cs"], Rows(host, "Files"));

        host.Press(TerminalKey.Down);
        Assert.Equal("beta", Cells(Selected(host, "Tasks"))[1]);
        Assert.Equal(["no commits"], Rows(host, "Commits", marker: false));
        Assert.Equal([" M src/Beta/Parser.cs", "?? src/Beta/Lexer.cs"], Rows(host, "Files"));

        host.Press(TerminalKey.Down);
        Assert.Equal("delta", Cells(Selected(host, "Tasks"))[1]);
        Assert.Equal(["no commits"], Rows(host, "Commits", marker: false));
        Assert.Equal(["no changes"], Rows(host, "Files", marker: false));
    }

    [Fact]
    public void Enter_on_a_task_opens_its_diff_and_Escape_closes_it()
    {
        using var host = Start();

        host.Press(TerminalKey.Enter);

        Assert.Contains("┌ Diff: alpha ", PopupTitleRow(host), StringComparison.Ordinal);
        var popup = PopupLines(host);
        var committed = popup.IndexOf("Committed");
        Assert.True(committed >= 0, host.Frame());
        Assert.StartsWith("diff --git a/src/Alpha/Parser.cs", popup[committed + 1], StringComparison.Ordinal);
        Assert.DoesNotContain("Uncommitted", popup);
        host.SaveSvg("git-diff-popup");

        host.Press(TerminalKey.Escape);
        Assert.Empty(PopupLines(host));
        Assert.Equal("alpha", Cells(Selected(host, "Tasks"))[1]);
    }

    [Fact]
    public void A_click_on_another_task_selects_it_and_a_click_on_the_selected_task_opens_its_diff()
    {
        using var host = Start();

        host.ClickText("@7a8b9c0");
        Assert.Empty(PopupLines(host));
        Assert.Equal("beta", Cells(Selected(host, "Tasks"))[1]);

        host.ClickText("@7a8b9c0");
        Assert.Contains("┌ Diff: beta ", PopupTitleRow(host), StringComparison.Ordinal);
        var popup = PopupLines(host);
        var uncommitted = popup.IndexOf("Uncommitted");
        Assert.True(uncommitted >= 0, host.Frame());
        Assert.StartsWith("diff --git a/src/Beta/Parser.cs", popup[uncommitted + 1], StringComparison.Ordinal);
        Assert.DoesNotContain("Committed", popup);
    }

    [Fact]
    public void Enter_on_a_task_without_a_diff_shows_no_diff()
    {
        using var host = Start();
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);

        host.Press(TerminalKey.Enter);

        Assert.Contains("┌ Diff: delta ", PopupTitleRow(host), StringComparison.Ordinal);
        Assert.Contains("no diff", PopupLines(host));
    }

    [Fact]
    public void Tab_moves_to_the_commits_where_Enter_opens_the_commit()
    {
        using var host = Start();

        host.Press(TerminalKey.Tab);
        Assert.Equal(AlphaCommit, Selected(host, "Commits"));
        Assert.Contains("[Enter] Commit", CommandBar(host), StringComparison.Ordinal);
        host.Press(TerminalKey.Enter);

        Assert.Contains("┌ 5d6e7f8 orch(alpha): Alpha parser ", PopupTitleRow(host), StringComparison.Ordinal);
        var popup = PopupLines(host);
        Assert.Contains("sha: 5d6e7f8a9b0c1d2e3f405162738495a6b7c8d9e0", popup);
        Assert.Contains(popup, line => line.StartsWith("time: 2026-10-03 12:08", StringComparison.Ordinal));
        Assert.Contains("kind: attempt", popup);
        Assert.Contains("attempt: 1", popup);
        Assert.Contains("subject: orch(alpha): Alpha parser", popup);
        Assert.Contains("body: Attempt 1.", popup);
    }

    [Fact]
    public void A_click_on_a_commit_opens_it()
    {
        using var host = Start();
        host.Press(TerminalKey.Down);

        host.ClickText(GammaSync);

        Assert.Contains("┌ d2e3f40 Merge branch 'orch/integration' into orch/task/gamma ", PopupTitleRow(host), StringComparison.Ordinal);
        Assert.Contains("kind: sync", PopupLines(host));
        host.Press(TerminalKey.Escape);
        Assert.Equal(GammaSync, Selected(host, "Commits"));
    }

    [Fact]
    public void Tab_twice_moves_to_the_files_where_Enter_opens_the_diff_of_the_file()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Tab);
        Assert.Contains("[Enter] File diff", CommandBar(host), StringComparison.Ordinal);

        host.Press(TerminalKey.Down);
        Assert.Equal(AlphaTestsFile, Selected(host, "Files"));
        host.Press(TerminalKey.Enter);

        Assert.Contains("┌ Diff: alpha tests/Alpha/ParserTests.cs ", PopupTitleRow(host), StringComparison.Ordinal);
        var popup = PopupLines(host);
        var diff = popup.IndexOf("Diff");
        Assert.True(diff >= 0, host.Frame());
        Assert.StartsWith("diff --git a/tests/Alpha/ParserTests.cs", popup[diff + 1], StringComparison.Ordinal);
        Assert.DoesNotContain(popup, line => line.Contains("src/Alpha/Parser.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void A_click_on_a_file_opens_its_diff()
    {
        using var host = Start();

        host.ClickText(AlphaTestsFile);

        Assert.Contains("┌ Diff: alpha tests/Alpha/ParserTests.cs ", PopupTitleRow(host), StringComparison.Ordinal);
        host.Press(TerminalKey.Escape);
        Assert.Equal(AlphaTestsFile, Selected(host, "Files"));
    }

    [Fact]
    public void An_untracked_file_has_no_diff()
    {
        using var host = Start();
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);

        host.ClickText("?? src/Beta/Lexer.cs");

        Assert.Contains("┌ Diff: beta src/Beta/Lexer.cs ", PopupTitleRow(host), StringComparison.Ordinal);
        Assert.Contains("no diff for this file", PopupLines(host));
    }

    [Fact]
    public void Tab_from_the_files_moves_back_to_the_tasks()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Down);

        Assert.Equal("gamma", Cells(Selected(host, "Tasks"))[1]);
    }

    [Fact]
    public void Shift_Tab_moves_from_the_tasks_to_the_files_to_the_commits_and_back()
    {
        var snapshot = SampleRun.CreateInsight();
        var shell = new AppShell([new GitPage()], () => snapshot, new FixedTimeProvider(snapshot.ReadAt));
        using var harness = TerminalHarness.Start(shell.Root, shell.OnUpdate);
        harness.Press(TerminalKey.Down);

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        harness.Press(TerminalKey.Down);
        Assert.Equal("gamma", Cells(Selected(harness.Frame(), "Tasks")!)[1]);
        Assert.Equal("+80 -0 src/Gamma/Formatter.cs", Selected(harness.Frame(), "Files"));
        Assert.Contains("[Enter] File diff", harness.Frame().Split('\n')[^1], StringComparison.Ordinal);

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        harness.Press(TerminalKey.Down);
        Assert.Equal(GammaSync, Selected(harness.Frame(), "Commits"));

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        harness.Press(TerminalKey.Down);
        Assert.Equal("beta", Cells(Selected(harness.Frame(), "Tasks")!)[1]);
    }

    [Fact]
    public void A_new_snapshot_keeps_the_selected_task_and_commit_by_id_and_sha()
    {
        var first = SampleRun.CreateInsight();
        using var host = Start(first);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        Assert.Equal(GammaSync, Selected(host, "Commits"));

        var next = Reordered(first);
        var gamma = next.Git.Tasks.Single(g => g.TaskId == "gamma");
        var earlier = gamma.Commits[0] with
        {
            Sha = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c", ShortSha = "0f1e2d3", Time = SampleRun.At(12, 1, 0),
        };
        var longer = gamma with { Commits = gamma.Commits.Insert(0, earlier) };
        host.SetSnapshot(next with { Git = next.Git with { Tasks = next.Git.Tasks.Replace(gamma, longer) } });

        Assert.Equal("epsilon", TaskRows(host.Frame())[0][1]);
        Assert.Equal("gamma", Cells(Selected(host, "Tasks"))[1]);
        Assert.Equal(5, Rows(host, "Commits").Length);
        Assert.Equal(GammaSync, Selected(host, "Commits"));
    }

    [Fact]
    public void A_new_snapshot_keeps_the_selected_file_by_path()
    {
        var first = SampleRun.CreateInsight();
        using var host = Start(first);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        Assert.Equal(AlphaTestsFile, Selected(host, "Files"));

        var next = Reordered(first);
        var alpha = next.Git.Tasks.Single(g => g.TaskId == "alpha");
        var more = alpha with { Committed = new DiffStat(alpha.Committed!.Files.Insert(0, new DiffFile("src/Alpha/Lexer.cs", 5, 0))) };
        host.SetSnapshot(next with { Git = next.Git with { Tasks = next.Git.Tasks.Replace(alpha, more) } });

        Assert.Equal("alpha", Cells(Selected(host, "Tasks"))[1]);
        Assert.Equal(["+5 -0 src/Alpha/Lexer.cs", AlphaParserFile, AlphaTestsFile], Rows(host, "Files"));
        Assert.Equal(AlphaTestsFile, Selected(host, "Files"));
    }

    [Fact]
    public void A_task_that_is_gone_from_a_new_snapshot_selects_the_first_task()
    {
        var first = SampleRun.CreateInsight();
        using var host = Start(first);
        host.Press(TerminalKey.Down);

        host.SetSnapshot(first with
        {
            Version = first.Version + 1,
            Tasks = first.Tasks.RemoveAll(t => t.Id == "gamma"),
            Git = first.Git with { Tasks = first.Git.Tasks.RemoveAll(g => g.TaskId == "gamma") },
        });

        Assert.Equal("alpha", Cells(Selected(host, "Tasks"))[1]);
        Assert.Equal([AlphaCommit], Rows(host, "Commits"));
    }

    [Fact]
    public void Before_the_first_git_read_the_tasks_are_listed_without_git_state()
    {
        using var host = Start(SampleRun.CreateInsight() with { Git = GitInfo.Empty });

        Assert.Contains("integration - @- · base - @- · 0 worktrees · 0 archive branches · read -", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(["✔", "alpha", "-", "-", "0 commits", "-", "-"], TaskRows(host.Frame())[0]);
        Assert.Equal(["no commits"], Rows(host, "Commits", marker: false));
        Assert.Equal(["no changes"], Rows(host, "Files", marker: false));

        host.Press(TerminalKey.Enter);
        Assert.Contains("┌ Diff: alpha ", PopupTitleRow(host), StringComparison.Ordinal);
        Assert.Contains("no diff", PopupLines(host));
    }

    [Fact]
    public void Without_a_plan_the_task_table_says_No_plan_yet()
    {
        using var host = Start(SampleRun.CreateInsight() with { Plan = null, Tasks = [], Git = GitInfo.Empty });

        Assert.Equal(["No plan yet"], PaneLines(host.Frame(), "Tasks").Where(line => line.Trim().Length > 0).Select(line => line.Trim()));
        Assert.Equal(["no commits"], Rows(host, "Commits", marker: false));
        Assert.Equal(["no changes"], Rows(host, "Files", marker: false));
    }

    [Fact]
    public void A_git_problem_shows_its_first_line_below_the_header()
    {
        var snapshot = SampleRun.CreateInsight();
        using var host = Start(snapshot with
        {
            Git = snapshot.Git with { Problem = "git rev-parse --show-toplevel: fatal: not a git repository\nmore" },
        });

        var lines = host.Frame().Split('\n');
        var header = Array.FindIndex(lines, line => line.Contains(Header, StringComparison.Ordinal));
        Assert.True(header >= 0, host.Frame());
        Assert.Equal("git unavailable: git rev-parse --show-toplevel: fatal: not a git repository", lines[header + 1].Trim());
        Assert.DoesNotContain("more", host.Frame(), StringComparison.Ordinal);
    }

    /// <summary>The snapshot with its tasks and their git state in the reverse order and a new version.</summary>
    private static RunSnapshot Reordered(RunSnapshot snapshot) => snapshot with
    {
        Version = snapshot.Version + 1,
        Tasks = [.. snapshot.Tasks.Reverse()],
        Git = snapshot.Git with { Tasks = [.. snapshot.Git.Tasks.Reverse()] },
    };

    private static string CommandBar(UiTestHost host) => host.Frame().Split('\n')[^1];

    /// <summary>The cells of a task row or of the column titles: the texts between runs of two or more spaces.</summary>
    private static string[] Cells(string row) => Regex.Split(row.Trim(), " {2,}");

    /// <summary>The cells of the task rows, below the column titles.</summary>
    private static string[][] TaskRows(string frame) =>
        [.. PaneLines(frame, "Tasks").Skip(1).Where(line => line.Trim().Length > 0).Select(line => Cells(line[2..]))];

    /// <summary>The text rows inside the pane whose title starts with <paramref name="title"/>, without borders.</summary>
    private static string[] PaneLines(string frame, string title)
    {
        var lines = frame.Split('\n');
        var top = Array.FindIndex(lines, line => line.Contains($"┌ {title}", StringComparison.Ordinal));
        Assert.True(top >= 0, frame);
        var left = lines[top].IndexOf($"┌ {title}", StringComparison.Ordinal);
        var right = lines[top].IndexOf('┐', left);
        var rows = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > left && lines[row][left] == '│'; row++)
        {
            var line = lines[row];
            rows.Add(line.Length <= left + 1 ? "" : line[(left + 1)..Math.Min(right - 1, line.Length)].TrimEnd());
        }
        return [.. rows];
    }

    /// <summary>The non-empty rows of a list pane, without the marker column unless <paramref name="marker"/> is false.</summary>
    private static string[] Rows(UiTestHost host, string title, bool marker = true) =>
        [.. PaneLines(host.Frame(), title).Where(line => line.Trim().Length > 0).Select(line => marker ? line[2..] : line.Trim())];

    /// <summary>The selected row of a list pane: the row with the arrow, without it; null when no row has it.</summary>
    private static string? Selected(string frame, string title) =>
        PaneLines(frame, title).FirstOrDefault(line => line.StartsWith('→'))?[2..];

    private static string Selected(UiTestHost host, string title)
    {
        var frame = host.Frame();
        Assert.DoesNotContain("[X]", frame, StringComparison.Ordinal);
        var row = Selected(frame, title);
        Assert.True(row is not null, frame);
        return row;
    }

    /// <summary>The text lines inside the pop-up, without its borders and scroll bar; empty when no pop-up is open.</summary>
    private static List<string> PopupLines(UiTestHost host)
    {
        var lines = host.Frame().Split('\n');
        var top = Array.FindIndex(lines, line => line.Contains("┌ ", StringComparison.Ordinal) && line.Contains("[X] ┐", StringComparison.Ordinal));
        if (top < 0)
        {
            return [];
        }
        var left = lines[top].IndexOf('┌', StringComparison.Ordinal);
        var right = lines[top].LastIndexOf('┐');
        var text = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > right && lines[row][left] == '│'; row++)
        {
            text.Add(lines[row][(left + 1)..(right - 1)].TrimEnd());
        }
        return text;
    }

    private static string PopupTitleRow(UiTestHost host) =>
        host.Frame().Split('\n').FirstOrDefault(line => line.Contains("[X] ┐", StringComparison.Ordinal)) ?? "";
}

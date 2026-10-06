using System.Collections.Immutable;
using System.Globalization;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Pages.GitView.Format;

// The text of the Git page (spec 25.1-25.6, git row table in 4.3): markup lines and pop-up sections built from the
// model. Every piece of model text in a markup line goes through Look.Tag; PopupSection.Text and titles are plain text.
public static class GitText
{
    public const string Missing = "-";
    public const string NoPlan = "No plan yet";
    public const string NoCommits = "no commits";
    public const string NoChanges = "no changes";
    public const string NoDiff = "no diff";
    public const string NoFileDiff = "no diff for this file";

    private const string ColumnGap = "  ";
    private const string Separator = " · ";
    private const string DiffHeader = "diff --git ";
    private const string RenameArrow = " -> ";

    private static readonly ImmutableArray<string> Titles = [" ", "Id", "Branch", "Worktree", "Commits", "Diff", "Merged"];

    // 25.1: the summary line and, while git reported a problem, its first line in the warning colour.
    public static ImmutableArray<string> HeaderLines(GitInfo git)
    {
        var archives = git.Branches.Count(b => b.Kind == GitBranchKind.Archive);
        var summary = string.Join(Separator,
            $"integration {Or(git.IntegrationBranch)} @{Or(git.IntegrationTip)}",
            $"base {Or(git.BaseBranch)} @{Or(git.BaseTip)}",
            $"{Number(git.Worktrees.Length)} worktrees",
            $"{Number(archives)} archive branches",
            "read " + (git.ReadAt is { } at ? Look.Clock(at) : Missing));
        return git.Problem is null
            ? [Look.Tag("", summary)]
            : [Look.Tag("", summary), Look.Tag("warning", "git unavailable: " + FirstLine(git.Problem))];
    }

    // 25.2: plain cells icon, id, branch, worktree, commits, diff, merged.
    public static IReadOnlyList<string> TaskCells(GitTask g, TaskView t) =>
    [
        Look.Icon(t.Status),
        t.Id,
        BranchCell(g),
        WorktreeCell(g),
        CommitsCell(g),
        g.Committed is { } stat ? $"{Number(stat.Files.Length)} files +{Number(stat.Added)} -{Number(stat.Removed)}" : Missing,
        g.MergeCommit?.ShortSha ?? Missing,
    ];

    // The width of each column: the longest cell of the column over the given rows and the header title.
    public static ImmutableArray<int> Widths(IEnumerable<IReadOnlyList<string>> rows)
    {
        var widths = Titles.Select(title => title.Length).ToArray();
        foreach (var cells in rows)
        {
            for (var i = 0; i < widths.Length; i++)
                widths[i] = Math.Max(widths[i], cells[i].Length);
        }
        return [.. widths];
    }

    // 25.2: one task row; the icon in the status colour, the other cells plain, padded to the widths.
    public static string TaskRow(GitTask g, TaskView t, IReadOnlyList<int> widths)
    {
        var cells = TaskCells(g, t);
        return Look.Tag(Look.Color(t.Status), cells[0].PadRight(widths[0])) + ColumnGap +
               Look.Tag("", Columns(cells.Skip(1), widths.Skip(1)));
    }

    // The header of the task table, aligned with TaskRow.
    public static string TaskHeader(IReadOnlyList<int> widths) => Look.Tag("muted", Columns(Titles, widths));

    // 25.3: "<clock> <short sha> <kind word> <subject>".
    public static string CommitRow(GitCommit c) =>
        Look.Tag("", $"{Look.Clock(c.Time)} {c.ShortSha} {KindWord(c)} {c.Subject}");

    // 25.3: one row per committed file, then one per uncommitted porcelain line; empty when neither exists.
    public static ImmutableArray<GitFileRow> FileRows(GitTask g)
    {
        var rows = ImmutableArray.CreateBuilder<GitFileRow>();
        if (g.Committed is { } stat)
        {
            foreach (var file in stat.Files)
                rows.Add(new GitFileRow(
                    Look.Tag("", $"+{NumberOr(file.Added)} -{NumberOr(file.Removed)} {file.Path}"), file.Path, Committed: true));
        }
        foreach (var line in g.UncommittedFiles)
            rows.Add(new GitFileRow(Look.Tag("", line), UncommittedPath(line), Committed: false));
        return rows.ToImmutable();
    }

    // 25.5: the path of a status --porcelain line: from the fourth character on, and after " -> " for a rename.
    public static string UncommittedPath(string porcelainLine)
    {
        var path = porcelainLine.Length > 3 ? porcelainLine[3..] : "";
        var arrow = path.IndexOf(RenameArrow, StringComparison.Ordinal);
        return arrow < 0 ? path : path[(arrow + RenameArrow.Length)..];
    }

    // 25.5: the section of a unified diff from the "diff --git" line of the path up to the next one; null without one.
    public static string? FileDiff(string? diff, string path)
    {
        if (string.IsNullOrEmpty(diff))
            return null;
        // "diff --git a/<path> b/<path>" ends with " b/<path>", as does the header of a rename to the path.
        var suffix = " b/" + path;
        for (var start = SectionStart(diff, 0); start >= 0; start = SectionStart(diff, start + 1))
        {
            var lineEnd = diff.IndexOf('\n', start);
            var header = (lineEnd < 0 ? diff[start..] : diff[start..lineEnd]).TrimEnd('\r');
            if (!header.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            var next = SectionStart(diff, start + 1);
            return (next < 0 ? diff[start..] : diff[start..next]).TrimEnd('\r', '\n');
        }
        return null;
    }

    // 25.4
    public static string DiffTitle(string taskId) => "Diff: " + taskId;

    // 25.4: Committed and Uncommitted when not empty, else one section with "no diff".
    public static IReadOnlyList<PopupSection> DiffPopup(GitTask g)
    {
        var sections = new List<PopupSection>();
        if (!string.IsNullOrEmpty(g.CommittedDiff))
            sections.Add(new PopupSection("Committed", g.CommittedDiff, TextKind.Diff));
        if (!string.IsNullOrEmpty(g.UncommittedDiff))
            sections.Add(new PopupSection("Uncommitted", g.UncommittedDiff, TextKind.Diff));
        if (sections.Count == 0)
            sections.Add(new PopupSection("Diff", NoDiff));
        return sections;
    }

    // 25.5
    public static string FileDiffTitle(string taskId, string path) => $"Diff: {taskId} {path}";

    // 25.5: the file's section of the committed or uncommitted diff, or "no diff for this file".
    public static IReadOnlyList<PopupSection> FileDiffPopup(GitTask g, GitFileRow row) =>
        FileDiff(row.Committed ? g.CommittedDiff : g.UncommittedDiff, row.Path) is { } section
            ? [new PopupSection("Diff", section, TextKind.Diff)]
            : [new PopupSection("Diff", NoFileDiff)];

    // 25.6
    public static string CommitTitle(GitCommit c) => $"{c.ShortSha} {c.Subject}";

    // 25.6: sha, time, kind, attempt, subject and body, one per line.
    public static IReadOnlyList<PopupSection> CommitPopup(GitCommit c) =>
    [
        new PopupSection("Commit", string.Join('\n',
            "sha: " + c.Sha,
            "time: " + Look.DateClock(c.Time),
            "kind: " + KindName(c.Kind),
            "attempt: " + NumberOr(c.Attempt),
            "subject: " + c.Subject,
            "body: " + Or(c.Body))),
    ];

    private static string BranchCell(GitTask g)
    {
        if (g.Branch is not null)
            return "@" + Or(g.Tip);
        return g.ArchiveBranches.IsEmpty ? Missing : "archived";
    }

    private static string WorktreeCell(GitTask g)
    {
        if (!g.WorktreeExists)
            return Missing;
        return g.UncommittedFiles.IsEmpty ? "clean" : $"{Number(g.UncommittedFiles.Length)} uncommitted";
    }

    private static string CommitsCell(GitTask g)
    {
        var text = Words.Count(g.Commits.Count(c => c.Kind == GitCommitKind.Attempt), "commit");
        var syncs = g.Commits.Count(c => c.Kind == GitCommitKind.Sync);
        return syncs > 0 ? $"{text}, {Words.Count(syncs, "sync")}" : text;
    }

    private static string KindWord(GitCommit c) => c.Kind == GitCommitKind.Attempt && c.Attempt is { } n
        ? "attempt " + Number(n)
        : KindName(c.Kind);

    private static string KindName(GitCommitKind kind) => kind switch
    {
        GitCommitKind.Attempt => "attempt",
        GitCommitKind.Sync => "sync",
        GitCommitKind.Merge => "merge",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string Columns(IEnumerable<string> cells, IEnumerable<int> widths) =>
        string.Join(ColumnGap, cells.Zip(widths, (cell, width) => cell.PadRight(width))).TrimEnd();

    // The index of the next line at or after from that starts with "diff --git ", or -1.
    private static int SectionStart(string diff, int from)
    {
        if (from == 0 && diff.StartsWith(DiffHeader, StringComparison.Ordinal))
            return 0;
        var newline = diff.IndexOf("\n" + DiffHeader, Math.Max(0, from - 1), StringComparison.Ordinal);
        return newline < 0 ? -1 : newline + 1;
    }

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string NumberOr(int? n) => n is { } value ? Number(value) : Missing;

    private static string Or(string? text) => string.IsNullOrEmpty(text) ? Missing : text;

    private static string FirstLine(string text)
    {
        var end = text.IndexOf('\n');
        return (end < 0 ? text : text[..end]).TrimEnd('\r');
    }
}

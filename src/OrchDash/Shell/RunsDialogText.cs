using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Shell;

/// <summary>Builds the rows of the <c>Runs</c> dialog by the dialog row table (32.8). Pure.</summary>
public static class RunsDialogText
{
    public const string Loading = "loading…";

    private const string Separator = "  ";

    private static readonly string[] Headings = [" ", "run", "spec", "started", "finished", "tasks", "provider"];

    /// <summary>
    /// <c>loading…</c> without a catalog; else the header row, one row per run and, when the catalog has one, its
    /// problem as the last row. The columns are padded to their longest value and separated by two spaces.
    /// </summary>
    /// <param name="catalog">The host's catalog; null while the first listing runs.</param>
    /// <param name="shownRepoPath">The <c>RepoPath</c> of the snapshot the dashboard shows; its row gets the marker <c>●</c>.</param>
    public static ImmutableArray<RunsRow> Rows(RunCatalog? catalog, string shownRepoPath)
    {
        ArgumentNullException.ThrowIfNull(shownRepoPath);
        if (catalog is null)
        {
            return [new RunsRow(Look.Tag("muted", Loading), null, false)];
        }
        var runs = catalog.Runs.IsDefault ? [] : catalog.Runs;
        var shown = runs.Select(entry => RunPaths.Same(entry.RepoPath, shownRepoPath)).ToArray();
        string[][] cells = [Headings, .. runs.Select((entry, i) => Cells(entry, shown[i]))];
        var widths = Enumerable.Range(0, Headings.Length).Select(column => cells.Max(row => row[column].Length)).ToArray();

        var rows = ImmutableArray.CreateBuilder<RunsRow>();
        rows.Add(new RunsRow(Look.Tag("muted", Line(cells[0], widths)), null, false));
        for (var i = 0; i < runs.Length; i++)
        {
            var entry = runs[i];
            var problem = entry.Problem is null ? "" : Look.Tag("warning", $" · {OneLine(entry.Problem)}");
            rows.Add(new RunsRow(Look.Tag("", Line(cells[i + 1], widths)) + problem, entry, shown[i]));
        }
        if (catalog.Problem is not null)
        {
            rows.Add(new RunsRow(Look.Tag("warning", OneLine(catalog.Problem)), null, false));
        }
        return rows.ToImmutable();
    }

    private static string[] Cells(RunEntry entry, bool shown) =>
    [
        shown ? "●" : " ",
        entry.Stamp ?? "current",
        entry.Spec ?? "-",
        entry.StartedAt is { } started ? Look.DateClock(started) : "-",
        entry.FinishedAt is { } finished ? Look.DateClock(finished) : "-",
        $"{Words.Number(entry.Done)} done · {Words.Number(entry.Failed)} failed · {Words.Number(entry.TaskCount)} tasks",
        entry.Provider switch
        {
            Provider.Claude => "Claude",
            Provider.Copilot => "Copilot",
            _ => entry.Model ?? "-",
        },
    ];

    /// <summary>The cells padded to the column widths and joined by two spaces; the last column is not padded.</summary>
    private static string Line(string[] cells, int[] widths) =>
        string.Join(Separator, cells.Select((cell, i) => i == cells.Length - 1 ? cell : cell.PadRight(widths[i])));

    private static string OneLine(string text) => text.ReplaceLineEndings(" · ");
}

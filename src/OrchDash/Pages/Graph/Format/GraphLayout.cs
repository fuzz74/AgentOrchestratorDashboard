using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Graph.Format;

/// <summary>
/// The graph layout rules (spec 24.1, 4.3, 40.2): one column per wave, <see cref="Width"/> cells wide, with a gutter of
/// <see cref="Gutter"/> cells between columns. Row 0 holds the column titles; below them each card has one row,
/// directly followed by the rows of its child lines, and the bus row <see cref="BusRow"/> is the last row. Columns are
/// 0-based, rows and x are cells of the graph.
/// </summary>
public sealed record GraphLayout(ImmutableArray<GraphColumn> Columns, int Width)
{
    public const int Gutter = 3;

    /// <summary>
    /// The row of the bus that edges spanning more than one gutter run along: 1 + the tallest column, counting its cards
    /// and their child lines (40.2).
    /// </summary>
    public int BusRow => 1 + (Columns.IsEmpty ? 0 : Columns.Max(c => c.Lines));

    public int Height => BusRow + 1;

    public int TotalWidth => Columns.IsEmpty ? 0 : Width * Columns.Length + Gutter * (Columns.Length - 1);

    /// <summary>
    /// One column per distinct wave, ascending, each with its tasks in snapshot order; width 3 + the longest id. 40.2:
    /// <paramref name="childLines"/> gives the number of child lines to reserve below each task's card; a task that is
    /// not in it has none.
    /// </summary>
    public static GraphLayout Build(ImmutableArray<TaskView> tasks, IReadOnlyDictionary<string, int>? childLines = null)
    {
        var columns = tasks
            .Select(t => t.Wave)
            .Distinct()
            .Order()
            .Select(wave =>
            {
                ImmutableArray<string> ids = [.. tasks.Where(t => t.Wave == wave).Select(t => t.Id)];
                return new GraphColumn(wave, ids) { ChildLines = [.. ids.Select(id => ChildLinesOf(childLines, id))] };
            });
        var width = 3 + (tasks.IsEmpty ? 0 : tasks.Max(t => t.Id.Length));
        return new GraphLayout([.. columns], width);
    }

    /// <summary>The x of the first cell of a column.</summary>
    public int ColumnStart(int column) => column * (Width + Gutter);

    /// <summary>The number of cards in a column.</summary>
    public int Rows(int column) => Columns[column].TaskIds.Length;

    /// <summary>The row of card <paramref name="index"/> of a column: 1 + the cards and child lines above it (40.2).</summary>
    public int CardRow(int column, int index)
    {
        var col = Columns[column];
        var row = 1 + index;
        for (var i = 0; i < index; i++)
            row += col.ChildLinesOf(i);
        return row;
    }

    /// <summary>The column and row of a task's card, or null when the task has none. The row is the card's own line.</summary>
    public (int Column, int Row)? Position(string taskId)
    {
        for (var c = 0; c < Columns.Length; c++)
        {
            var i = Columns[c].TaskIds.IndexOf(taskId, StringComparer.Ordinal);
            if (i >= 0)
                return (c, CardRow(c, i));
        }
        return null;
    }

    /// <summary>The task whose card is in this column and row, or null (also on a child line).</summary>
    public string? TaskAt(int column, int row) =>
        Locate(column, row) is (var card, -1) ? Columns[column].TaskIds[card] : null;

    /// <summary>The task whose card covers the cell (x, y), or null for a title, child line, gutter, bus or empty cell.</summary>
    public string? CardAt(int x, int y) => ColumnAt(x) is { } column ? TaskAt(column, y) : null;

    /// <summary>
    /// 40.3: the task and the 0-based index among its child lines of the child line that covers the cell (x, y), or null
    /// for any other cell.
    /// </summary>
    public (string TaskId, int Index)? ChildAt(int x, int y) =>
        ColumnAt(x) is { } column && Locate(column, y) is (var card, var child) && child >= 0
            ? (Columns[column].TaskIds[card], child)
            : null;

    /// <summary>
    /// The task of the card in this column whose row is nearest to the row (for Tab), the upper one of two as near, or
    /// null without cards.
    /// </summary>
    public string? NearestInColumn(int column, int row)
    {
        if (column < 0 || column >= Columns.Length)
            return null;
        string? nearest = null;
        var distance = int.MaxValue;
        var ids = Columns[column].TaskIds;
        for (var i = 0; i < ids.Length; i++)
        {
            var d = Math.Abs(CardRow(column, i) - row);
            if (d < distance)
            {
                (nearest, distance) = (ids[i], d);
            }
        }
        return nearest;
    }

    private static int ChildLinesOf(IReadOnlyDictionary<string, int>? childLines, string taskId) =>
        childLines is not null && childLines.TryGetValue(taskId, out var count) ? Math.Max(count, 0) : 0;

    // The column whose cards cover x, or null for a gutter cell or x < 0; a column past the last is not checked.
    private int? ColumnAt(int x) => x >= 0 && x % (Width + Gutter) < Width ? x / (Width + Gutter) : null;

    // The card whose block (its row and its child lines) holds the row, and the index of the row among the card's child
    // lines, -1 for the card's own row; null for a column that does not exist or a row outside every block.
    private (int Card, int Child)? Locate(int column, int row)
    {
        if (column < 0 || column >= Columns.Length)
            return null;
        var col = Columns[column];
        var top = 1;
        for (var i = 0; i < col.TaskIds.Length; i++)
        {
            var lines = col.ChildLinesOf(i);
            if (row >= top && row <= top + lines)
                return (i, row - top - 1);
            top += 1 + lines;
        }
        return null;
    }
}

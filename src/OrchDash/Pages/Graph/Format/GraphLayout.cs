using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Graph.Format;

/// <summary>
/// The graph layout rules (spec 24.1, 4.3): one column per wave, <see cref="Width"/> cells wide, with a gutter of
/// <see cref="Gutter"/> cells between columns. Row 0 holds the column titles, card i of a column is on row 1 + i and
/// the bus row <see cref="BusRow"/> is the last row. Columns are 0-based, rows and x are cells of the graph.
/// </summary>
public sealed record GraphLayout(ImmutableArray<GraphColumn> Columns, int Width)
{
    public const int Gutter = 3;

    /// <summary>The row of the bus that edges spanning more than one gutter run along: 1 + the largest card count.</summary>
    public int BusRow => 1 + (Columns.IsEmpty ? 0 : Columns.Max(c => c.TaskIds.Length));

    public int Height => BusRow + 1;

    public int TotalWidth => Columns.IsEmpty ? 0 : Width * Columns.Length + Gutter * (Columns.Length - 1);

    /// <summary>One column per distinct wave, ascending, each with its tasks in snapshot order; width 3 + the longest id.</summary>
    public static GraphLayout Build(ImmutableArray<TaskView> tasks)
    {
        var columns = tasks
            .Select(t => t.Wave)
            .Distinct()
            .Order()
            .Select(wave => new GraphColumn(wave, [.. tasks.Where(t => t.Wave == wave).Select(t => t.Id)]));
        var width = 3 + (tasks.IsEmpty ? 0 : tasks.Max(t => t.Id.Length));
        return new GraphLayout([.. columns], width);
    }

    /// <summary>The x of the first cell of a column.</summary>
    public int ColumnStart(int column) => column * (Width + Gutter);

    /// <summary>The number of cards in a column.</summary>
    public int Rows(int column) => Columns[column].TaskIds.Length;

    /// <summary>The column and row of a task's card, or null when the task has none.</summary>
    public (int Column, int Row)? Position(string taskId)
    {
        for (var c = 0; c < Columns.Length; c++)
        {
            var i = Columns[c].TaskIds.IndexOf(taskId, StringComparer.Ordinal);
            if (i >= 0)
                return (c, 1 + i);
        }
        return null;
    }

    /// <summary>The task whose card is in this column and row, or null.</summary>
    public string? TaskAt(int column, int row)
    {
        if (column < 0 || column >= Columns.Length)
            return null;
        var ids = Columns[column].TaskIds;
        var i = row - 1;
        return i >= 0 && i < ids.Length ? ids[i] : null;
    }

    /// <summary>The task whose card covers the cell (x, y), or null for a title, gutter, bus or empty cell.</summary>
    public string? CardAt(int x, int y)
    {
        if (x < 0)
            return null;
        var column = x / (Width + Gutter);
        return x % (Width + Gutter) < Width ? TaskAt(column, y) : null;
    }

    /// <summary>The task of the card in this column whose row is nearest to the row (for Tab), or null without cards.</summary>
    public string? NearestInColumn(int column, int row)
    {
        if (column < 0 || column >= Columns.Length || Rows(column) == 0)
            return null;
        return TaskAt(column, Math.Clamp(row, 1, Rows(column)));
    }
}

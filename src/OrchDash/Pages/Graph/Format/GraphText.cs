using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Pages.Graph.Format;

/// <summary>
/// The text of the Graph page (spec 24.1, 24.2, 24.4): the graph as one line per row of a <see cref="GraphLayout"/>
/// by the card table and the edge routing rules in section 4.3, and the detail lines of a task. Every graph cell is one
/// terminal cell, so x in a plain line is the cell x of <see cref="GraphLayout.CardAt"/>.
/// </summary>
public static class GraphText
{
    public const string NoPlan = "No plan yet";

    public const char Selected = '●';
    public const char DepMarker = '◂';
    public const char DependentMarker = '▸';

    private const string Missing = "-";
    private const string Separator = " · ";
    private const string Edge = "accent";

    /// <summary>
    /// One markup line per row of the layout (<see cref="GraphLayout.Height"/> lines), each cut to <paramref name="width"/>
    /// cells: the titles, the cards with the selected task and its deps and dependents marked, and the edges between the
    /// selected task and its direct deps and dependents. Runs of one colour form one <see cref="Look.Tag"/>. The layout
    /// is <c>GraphLayout.Build(tasks)</c>; a <paramref name="selectedId"/> that is null or not a task marks nothing.
    /// </summary>
    public static ImmutableArray<string> Lines(GraphLayout layout, ImmutableArray<TaskView> tasks, string? selectedId, int width)
    {
        var grid = Draw(layout, tasks, selectedId, width);
        return [.. Enumerable.Range(0, grid.Height).Select(grid.Markup)];
    }

    /// <summary>The lines of <see cref="Lines"/> without markup, for tests and for mapping a click to a cell.</summary>
    public static ImmutableArray<string> PlainLines(GraphLayout layout, ImmutableArray<TaskView> tasks, string? selectedId, int width)
    {
        var grid = Draw(layout, tasks, selectedId, width);
        return [.. Enumerable.Range(0, grid.Height).Select(grid.Plain)];
    }

    /// <summary>24.4: the plain-text lines of the detail panel of a task; the page escapes them.</summary>
    public static ImmutableArray<string> Detail(TaskView task, RunSnapshot snapshot)
    {
        var tasks = snapshot.Tasks;
        var overlaps = tasks
            .Where(t => t.Id != task.Id && TaskDetails.OwnsOverlap(task.Owns, t.Owns))
            .Select(t => t.Id);
        var sessions = snapshot.Sessions.Count(s => s.Files.TaskId == task.Id);
        return
        [
            $"{task.Id} - {task.Title}",
            "deps: " + IconList(tasks, GraphRelations.Deps(tasks, task.Id)),
            "dependents: " + IconList(tasks, GraphRelations.Dependents(tasks, task.Id)),
            "owns: " + List(task.Owns),
            "overlaps: " + List(overlaps),
            "state: " + string.Join(Separator, task.Status.ToString(), Words.Count(task.Attempts, "attempt"), Look.Usd(task.CostUsd)),
            "detail: " + task.Detail,
            "sessions: " + sessions.ToString(CultureInfo.InvariantCulture),
        ];
    }

    private static string IconList(ImmutableArray<TaskView> tasks, ImmutableArray<string> ids) =>
        List(ids.Select(id => $"{Look.Icon(tasks.First(t => t.Id == id).Status)} {id}"));

    private static string List(IEnumerable<string> items)
    {
        var text = string.Join(", ", items);
        return text.Length == 0 ? Missing : text;
    }

    private static Grid Draw(GraphLayout layout, ImmutableArray<TaskView> tasks, string? selectedId, int width)
    {
        var grid = new Grid(Math.Clamp(width, 0, layout.TotalWidth), layout.Height);
        var selected = selectedId is not null ? layout.Position(selectedId) : null;
        var deps = selected is not null ? GraphRelations.AllDeps(tasks, selectedId!).ToHashSet() : [];
        var dependents = selected is not null ? GraphRelations.AllDependents(tasks, selectedId!).ToHashSet() : [];

        for (var c = 0; c < layout.Columns.Length; c++)
        {
            var x = layout.ColumnStart(c);
            grid.Write(x, 0, layout.Columns[c].Title, "bold");
            for (var row = 1; row <= layout.Rows(c); row++)
            {
                var task = tasks.First(t => t.Id == layout.TaskAt(c, row));
                var (marker, colour) =
                    selected is not null && task.Id == selectedId ? (Selected, "bold")
                    : deps.Contains(task.Id) ? (DepMarker, "accent")
                    : dependents.Contains(task.Id) ? (DependentMarker, "accent")
                    : (' ', Look.Color(task.Status));
                grid.Write(x, row, $"{marker}{Look.Icon(task.Status)} {task.Id}", colour);
            }
        }

        if (selected is { } to)
        {
            foreach (var dep in GraphRelations.Deps(tasks, selectedId!))
                DrawEdge(grid, layout, layout.Position(dep)!.Value, to);
            foreach (var dependent in GraphRelations.Dependents(tasks, selectedId!))
                DrawEdge(grid, layout, to, layout.Position(dependent)!.Value);
        }
        return grid;
    }

    // The edge routing rules (24.2): an edge goes from a column to a later one; any other pair draws nothing.
    private static void DrawEdge(Grid grid, GraphLayout layout, (int Column, int Row) from, (int Column, int Row) to)
    {
        var (a, ya) = from;
        var (b, yb) = to;
        if (a >= b)
            return;

        int X0(int column) => layout.ColumnStart(column) + layout.Width;
        var x0 = X0(a);
        var x1 = x0 + 1;
        grid.Line(x0, ya, '─');
        if (b == a + 1)
        {
            if (ya == yb)
                grid.Line(x1, ya, '─');
            else
                Turn(grid, x1, ya, yb);
            grid.Line(x0 + 2, yb, '─');
            return;
        }

        var bus = layout.BusRow;
        var x1End = X0(b - 1) + 1;
        Turn(grid, x1, ya, bus);
        for (var x = x1 + 1; x < x1End; x++)
            grid.Line(x, bus, '─');
        Turn(grid, x1End, bus, yb);
        grid.Line(x1End + 1, yb, '─');
    }

    // The vertical part of an edge at x from row y1 (entered from the left) to row y2 (left to the right).
    private static void Turn(Grid grid, int x, int y1, int y2)
    {
        var down = y1 < y2;
        grid.Line(x, y1, down ? '┐' : '┘');
        for (var y = Math.Min(y1, y2) + 1; y < Math.Max(y1, y2); y++)
            grid.Line(x, y, '│');
        grid.Line(x, y2, down ? '└' : '┌');
    }

    // Cells of one character and one colour ("" = plain); cells outside the width are dropped.
    private sealed class Grid(int width, int height)
    {
        private readonly char[,] _glyphs = Filled(width, height);
        private readonly string[,] _colours = new string[width, height];
        private readonly bool[,] _edges = new bool[width, height];

        public int Height => height;

        public void Write(int x, int y, string text, string colour)
        {
            for (var i = 0; i < text.Length; i++)
                Set(x + i, y, text[i], colour);
        }

        // An edge cell; a cell that another edge drew with a different glyph becomes a crossing.
        public void Line(int x, int y, char glyph)
        {
            if (!Inside(x, y))
                return;
            Set(x, y, _edges[x, y] && _glyphs[x, y] != glyph ? '┼' : glyph, Edge);
            _edges[x, y] = true;
        }

        public string Plain(int y)
        {
            var line = new StringBuilder(width);
            for (var x = 0; x < width; x++)
                line.Append(_glyphs[x, y]);
            return line.ToString();
        }

        public string Markup(int y)
        {
            var line = new StringBuilder();
            var run = new StringBuilder();
            var colour = "";
            for (var x = 0; x < width; x++)
            {
                var cellColour = _colours[x, y] ?? "";
                if (cellColour != colour && run.Length > 0)
                {
                    line.Append(Look.Tag(colour, run.ToString()));
                    run.Clear();
                }
                colour = cellColour;
                run.Append(_glyphs[x, y]);
            }
            if (run.Length > 0)
                line.Append(Look.Tag(colour, run.ToString()));
            return line.ToString();
        }

        private void Set(int x, int y, char glyph, string colour)
        {
            if (!Inside(x, y))
                return;
            _glyphs[x, y] = glyph;
            _colours[x, y] = colour;
        }

        private bool Inside(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

        private static char[,] Filled(int width, int height)
        {
            var glyphs = new char[width, height];
            for (var x = 0; x < width; x++)
                for (var y = 0; y < height; y++)
                    glyphs[x, y] = ' ';
            return glyphs;
        }
    }
}

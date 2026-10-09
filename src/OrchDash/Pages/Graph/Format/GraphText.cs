using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Pages.Graph.Format;

/// <summary>
/// The text of the Graph page (spec 24.1, 24.2, 24.4, 40.1-40.4): the graph as one line per row of a
/// <see cref="GraphLayout"/> by the card table and the edge routing rules in section 4.3, with each card's child rows on
/// the lines below it, and the detail lines of a task. Every graph cell is one terminal cell, so x in a plain line is the
/// cell x of <see cref="GraphLayout.CardAt"/>.
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
    private const string ChildIndent = "  ";
    private const int ShortLength = 20;   // 40.1: a longer sub-agent name is cut to ShortLength - 1 characters and "…"

    /// <summary>
    /// One markup line per row of the layout (<see cref="GraphLayout.Height"/> lines), each cut to <paramref name="width"/>
    /// cells: the titles, the cards with the selected task and its deps and dependents marked, the child lines of each
    /// card, and the edges between the selected task and its direct deps and dependents. Runs of one colour form one
    /// <see cref="Look.Tag"/>. The layout is <c>GraphLayout.Build(tasks)</c>, or with child rows <see cref="Layout"/>;
    /// a <paramref name="selectedId"/> that is null or not a task marks nothing.
    /// </summary>
    public static ImmutableArray<string> Lines(GraphLayout layout, ImmutableArray<TaskView> tasks, string? selectedId, int width,
        IReadOnlyDictionary<string, ImmutableArray<AgentRow>>? childRows = null)
    {
        var grid = Draw(layout, tasks, selectedId, width, childRows);
        return [.. Enumerable.Range(0, grid.Height).Select(grid.Markup)];
    }

    /// <summary>The lines of <see cref="Lines"/> without markup, for tests and for mapping a click to a cell.</summary>
    public static ImmutableArray<string> PlainLines(GraphLayout layout, ImmutableArray<TaskView> tasks, string? selectedId, int width,
        IReadOnlyDictionary<string, ImmutableArray<AgentRow>>? childRows = null)
    {
        var grid = Draw(layout, tasks, selectedId, width, childRows);
        return [.. Enumerable.Range(0, grid.Height).Select(grid.Plain)];
    }

    /// <summary>
    /// 40.1: the child rows of each task that has any: <see cref="AgentTree.Rows"/> of the task's sessions in snapshot
    /// order.
    /// </summary>
    public static ImmutableDictionary<string, ImmutableArray<AgentRow>> ChildRows(RunSnapshot snapshot)
    {
        var rows = ImmutableDictionary.CreateBuilder<string, ImmutableArray<AgentRow>>(StringComparer.Ordinal);
        foreach (var id in snapshot.Tasks.Select(t => t.Id).Distinct(StringComparer.Ordinal))
        {
            if (TaskRows(snapshot, id) is { IsEmpty: false } taskRows)
                rows[id] = taskRows;
        }
        return rows.ToImmutable();
    }

    /// <summary>
    /// 40.1, 40.2: <see cref="GraphLayout.Build"/> with the lines of each task's child rows reserved below its card, its
    /// columns as wide as the longest card or child line.
    /// </summary>
    public static GraphLayout Layout(ImmutableArray<TaskView> tasks, IReadOnlyDictionary<string, ImmutableArray<AgentRow>> childRows)
    {
        var layout = GraphLayout.Build(tasks, childRows.ToDictionary(pair => pair.Key, pair => pair.Value.Length, StringComparer.Ordinal));
        var longest = childRows.Values.SelectMany(rows => rows).Select(row => ChildLine(row).Length).DefaultIfEmpty(0).Max();
        return longest > layout.Width ? layout with { Width = longest } : layout;
    }

    /// <summary>40.1: the plain text of a child line, <c>  &lt;Prefix&gt;&lt;icon&gt; &lt;short&gt;</c>.</summary>
    public static string ChildLine(AgentRow row) =>
        $"{ChildIndent}{row.Prefix}{Look.Icon(SubAgents.StateOf(row.Session, row.SubAgent))} {Short(row.SubAgent.Name)}";

    /// <summary>
    /// 24.4: the plain-text lines of the detail panel of a task; the page escapes them. 40.4: the sessions line adds the
    /// task's sub-agents when it has any.
    /// </summary>
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
            "sessions: " + sessions.ToString(CultureInfo.InvariantCulture) + SubAgentCount(TaskRows(snapshot, task.Id)),
        ];
    }

    // 40.4: " · sub-agents: M (k running)" for a task with child rows, k counting those whose StateOf is Running; else "".
    private static string SubAgentCount(ImmutableArray<AgentRow> rows)
    {
        if (rows.IsEmpty)
            return "";
        var running = rows.Count(row => SubAgents.StateOf(row.Session, row.SubAgent) == SessionState.Running);
        return string.Create(CultureInfo.InvariantCulture, $"{Separator}sub-agents: {rows.Length} ({running} running)");
    }

    // 40.1: the child rows of a task: AgentTree.Rows of its sessions in snapshot order.
    private static ImmutableArray<AgentRow> TaskRows(RunSnapshot snapshot, string taskId) =>
        AgentTree.Rows(snapshot.Sessions.Where(s => s.Files.TaskId == taskId));

    // 40.1: the name, or its first ShortLength - 1 characters and "…" when it is longer than ShortLength.
    private static string Short(string name) => name.Length > ShortLength ? name[..(ShortLength - 1)] + "…" : name;

    private static string IconList(ImmutableArray<TaskView> tasks, ImmutableArray<string> ids) =>
        List(ids.Select(id => $"{Look.Icon(tasks.First(t => t.Id == id).Status)} {id}"));

    private static string List(IEnumerable<string> items)
    {
        var text = string.Join(", ", items);
        return text.Length == 0 ? Missing : text;
    }

    private static Grid Draw(GraphLayout layout, ImmutableArray<TaskView> tasks, string? selectedId, int width,
        IReadOnlyDictionary<string, ImmutableArray<AgentRow>>? childRows)
    {
        var grid = new Grid(Math.Clamp(width, 0, layout.TotalWidth), layout.Height);
        var selected = selectedId is not null ? layout.Position(selectedId) : null;
        var deps = selected is not null ? GraphRelations.AllDeps(tasks, selectedId!).ToHashSet() : [];
        var dependents = selected is not null ? GraphRelations.AllDependents(tasks, selectedId!).ToHashSet() : [];

        for (var c = 0; c < layout.Columns.Length; c++)
        {
            var x = layout.ColumnStart(c);
            var column = layout.Columns[c];
            grid.Write(x, 0, column.Title, "bold");
            for (var i = 0; i < column.TaskIds.Length; i++)
            {
                var task = tasks.First(t => t.Id == column.TaskIds[i]);
                var row = layout.CardRow(c, i);
                var (marker, colour) =
                    selected is not null && task.Id == selectedId ? (Selected, "bold")
                    : deps.Contains(task.Id) ? (DepMarker, "accent")
                    : dependents.Contains(task.Id) ? (DependentMarker, "accent")
                    : (' ', Look.Color(task.Status));
                grid.Write(x, row, $"{marker}{Look.Icon(task.Status)} {task.Id}", colour);

                // 40.1, 40.2: the task's child rows, each in its state colour, on the lines reserved below the card.
                if (childRows is not null && childRows.TryGetValue(task.Id, out var children))
                {
                    for (var j = 0; j < Math.Min(children.Length, column.ChildLinesOf(i)); j++)
                        grid.Write(x, row + 1 + j, ChildLine(children[j]),
                            Look.Color(SubAgents.StateOf(children[j].Session, children[j].SubAgent)));
                }
            }
        }

        // 40.2: Position gives the card's own line, so edges attach to it and never to a child line.
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

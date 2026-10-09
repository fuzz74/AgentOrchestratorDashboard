using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Graph.Format;
using OrchDash.Pages.Overview.Format;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Pages.Graph;

/// <summary>
/// The visuals and the selection state of one <see cref="GraphPage"/>: the graph as markup lines in one vertically
/// scrolling view, and the detail panel of the selected task. Everything shown is a function of
/// <see cref="IAppContext.Snapshot"/> and the selected task id; the layout and the child rows are cached per snapshot.
/// </summary>
internal sealed class GraphView
{
    public const string ConversationPageId = "conversation";

    // The eight lines of GraphText.Detail, each cut at the right, and the panel's border.
    private const int DetailHeight = 10;

    private readonly IAppContext _context;
    private readonly State<string?> _selected = new(null);
    private readonly ScrollViewer _viewer;

    private RunSnapshot? _layoutFor;
    private GraphLayout _layout = GraphLayout.Build([]);
    private ImmutableDictionary<string, ImmutableArray<AgentRow>> _childRows = ImmutableDictionary<string, ImmutableArray<AgentRow>>.Empty;
    private RunSnapshot? _arrangedFor;

    public GraphView(IAppContext context)
    {
        _context = context;
        // The markup's lambda runs at once and reads the viewer, so the viewer exists before its content.
        _viewer = new ScrollViewer(focusable: true).HorizontalScrollEnabled(false);
        var graph = new Markup(GraphMarkup) { Wrap = false }.IsSelectable(false).PointerPressed((_, e) => OnPressed(e));
        _viewer.Content = new ArrangeProbe(graph, OnArranged);
        _viewer.AutoFocus(true);
        AddCommands(_viewer);

        var detail = new Markup(DetailMarkup) { Wrap = false }.IsSelectable(false);
        Root = new Grid()
            .Rows(
                new RowDefinition().Height(GridLength.Star()),
                new RowDefinition().Height(GridLength.Fixed(DetailHeight)))
            .Columns(new ColumnDefinition().Width(GridLength.Star()))
            .Cell(new Group("Graph").Content(_viewer).Stretch(), 0, 0)
            .Cell(new Group("Task").Content(detail).Stretch(), 1, 0);
    }

    public Visual Root { get; }

    /// <summary>40.1, 40.2: the layout with each card's child lines, built with the child rows once per snapshot.</summary>
    private GraphLayout Layout()
    {
        var snapshot = _context.Snapshot.Value;
        if (!ReferenceEquals(snapshot, _layoutFor))
        {
            _childRows = GraphText.ChildRows(snapshot);
            _layout = GraphText.Layout(snapshot.Tasks, _childRows);
            _layoutFor = snapshot;
        }
        return _layout;
    }

    /// <summary>The child rows of each task of the snapshot that has any, for the layout of <see cref="Layout"/>.</summary>
    private ImmutableDictionary<string, ImmutableArray<AgentRow>> ChildRows()
    {
        Layout();
        return _childRows;
    }

    /// <summary>
    /// 24.7: the selected task id while it is a task of the snapshot, else the first task; null without a plan or tasks.
    /// </summary>
    private string? SelectedId()
    {
        var snapshot = _context.Snapshot.Value;
        if (snapshot.Plan is null)
        {
            return null;
        }
        var id = _selected.Value;
        return id is not null && snapshot.Tasks.Any(t => t.Id == id) ? id : snapshot.Tasks.FirstOrDefault()?.Id;
    }

    private TaskView? SelectedTask() =>
        SelectedId() is { } id ? _context.Snapshot.Value.Tasks.First(t => t.Id == id) : null;

    /// <summary>24.1, 24.2: the graph cut to the width of the view, or <c>No plan yet</c>.</summary>
    private string GraphMarkup()
    {
        var snapshot = _context.Snapshot.Value;
        if (snapshot.Plan is null)
        {
            return Look.Tag("muted", GraphText.NoPlan);
        }
        var layout = Layout();
        var width = _viewer.ViewportWidth > 0 ? _viewer.ViewportWidth : layout.TotalWidth;
        return string.Join('\n', GraphText.Lines(layout, snapshot.Tasks, SelectedId(), width, ChildRows()));
    }

    /// <summary>24.4: the detail lines of the selected task; empty without one.</summary>
    private string DetailMarkup() =>
        SelectedTask() is { } task
            ? string.Join('\n', GraphText.Detail(task, _context.Snapshot.Value).Select(line => Look.Tag("", line)))
            : "";

    private void Select(string id)
    {
        _selected.Value = id;
        ScrollToSelection();
    }

    /// <summary>24.3: Up and Down move to the next card within the column, past its child lines (40.3), and stop at its ends.</summary>
    private void MoveInColumn(int step)
    {
        if (SelectedId() is { } id && Layout() is var layout && layout.Position(id) is (var column, _))
        {
            var ids = layout.Columns[column].TaskIds;
            var next = ids.IndexOf(id, StringComparer.Ordinal) + step;
            if (next >= 0 && next < ids.Length)
            {
                Select(ids[next]);
            }
        }
    }

    /// <summary>24.3: Tab and Shift+Tab select the nearest card of the next or previous column, wrapping around.</summary>
    private void MoveToColumn(int step)
    {
        if (SelectedId() is { } id && Layout() is var layout && layout.Position(id) is (var column, var row))
        {
            var count = layout.Columns.Length;
            if (layout.NearestInColumn((column + step + count) % count, row) is { } next)
            {
                Select(next);
            }
        }
    }

    /// <summary>
    /// 24.3, 24.5: a click selects the card under the pointer, or opens it when it is already selected. 40.3: a click on a
    /// child line opens its sub-agent.
    /// </summary>
    private void OnPressed(PointerEventArgs e)
    {
        if (e.Button != TerminalMouseButton.Left)
        {
            return;
        }
        e.Handled = true;
        _viewer.App?.Focus(_viewer);
        if (SelectedId() is not { } selected)
        {
            return;
        }
        var layout = Layout();
        if (layout.CardAt(e.LocalX, e.LocalY) is { } id)
        {
            if (id == selected)
            {
                OpenDetails();
            }
            else
            {
                Select(id);
            }
        }
        else if (layout.ChildAt(e.LocalX, e.LocalY) is (var taskId, var index)
            && ChildRows().TryGetValue(taskId, out var rows) && index < rows.Length)
        {
            ShowSubAgent(rows[index]);
        }
    }

    /// <summary>24.5: the task pop-up of the Overview page.</summary>
    private void OpenDetails()
    {
        if (SelectedTask() is { } task)
        {
            var snapshot = _context.Snapshot.Value;
            _context.ShowPopup($"{task.Id} - {task.Title}", OverviewText.TaskPopup(task, snapshot));
        }
    }

    /// <summary>24.6: the selected task's last session in snapshot order on the Conversation page.</summary>
    private void ShowConversation()
    {
        if (SelectedId() is { } id && _context.Snapshot.Value.Sessions.LastOrDefault(s => s.Files.TaskId == id) is { } session)
        {
            _context.SelectedSessionKey.Value = session.Files.Key;
            _context.ShowPage(ConversationPageId);
        }
    }

    /// <summary>40.3: the sub-agent of a child row on the Conversation page, as on the Overview page (39.1).</summary>
    private void ShowSubAgent(AgentRow row)
    {
        _context.SelectedSessionKey.Value = AgentKey.Of(row.Session, row.SubAgent.Id);
        _context.ShowPage(ConversationPageId);
    }

    /// <summary>
    /// After the layout of a new snapshot, keeps the selection (24.7) and scrolls to it. Arrange runs in a tracking
    /// context, which may not write the state it read, so both are posted to run after the frame.
    /// </summary>
    private void OnArranged()
    {
        var snapshot = _context.Snapshot.Value;
        if (!ReferenceEquals(snapshot, _arrangedFor))
        {
            _arrangedFor = snapshot;
            _viewer.App?.Post(() =>
            {
                if (_selected.Value != SelectedId())
                {
                    _selected.Value = SelectedId();
                }
                ScrollToSelection();
            });
        }
    }

    /// <summary>Scrolls as little as needed to show the selected card's row; the first row also shows the titles.</summary>
    private void ScrollToSelection()
    {
        var viewport = _viewer.ViewportHeight;
        if (viewport <= 0 || SelectedId() is not { } id || Layout().Position(id) is not (_, var row))
        {
            return;
        }
        var top = _viewer.VerticalOffset;
        if (row < top)
        {
            top = row == 1 ? 0 : row;
        }
        else if (row >= top + viewport)
        {
            top = row - viewport + 1;
        }
        if (top != _viewer.VerticalOffset)
        {
            _viewer.VerticalOffset = top;
        }
    }

    private void AddCommands(Visual viewer)
    {
        Add("up", "Up", new KeyGesture(TerminalKey.Up), () => MoveInColumn(-1));
        Add("down", "Down", new KeyGesture(TerminalKey.Down), () => MoveInColumn(1));
        Add("next-column", "Next column", new KeyGesture(TerminalKey.Tab), () => MoveToColumn(1), visible: true);
        Add("previous-column", "Previous column", new KeyGesture(TerminalKey.Tab, TerminalModifiers.Shift), () => MoveToColumn(-1));
        Add("details", "Details", new KeyGesture(TerminalKey.Enter), OpenDetails, visible: true);
        Add("conversation", "Conversation", new KeyGesture('c'), ShowConversation, visible: true);

        void Add(string name, string label, KeyGesture gesture, Action execute, bool visible = false) =>
            viewer.AddCommand(new Command
            {
                Id = $"graph.{name}",
                LabelMarkup = label,
                Gesture = gesture,
                Presentation = visible ? CommandPresentation.CommandBar : CommandPresentation.None,
                Execute = _ => execute(),
            });
    }

    /// <summary>
    /// The content of the view: the graph markup in a container that reports each of its arranges, after which the
    /// viewport is known. (ScrollViewer is sealed; Padder is the simplest container that is not.)
    /// </summary>
    private sealed class ArrangeProbe(Visual content, Action arranged) : Padder(content)
    {
        protected override void ArrangeCore(in Rectangle finalRect)
        {
            base.ArrangeCore(finalRect);
            arranged();
        }
    }
}

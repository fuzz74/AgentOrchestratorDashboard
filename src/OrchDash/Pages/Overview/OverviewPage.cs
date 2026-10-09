using System.Globalization;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Overview.Format;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Pages.Overview;

/// <summary>
/// The Overview page (spec 7.1-7.9, 39): the run panel on top, the task table and the running panel side by side, and the
/// log at the bottom. Each list scrolls inside its own panel; Tab and Shift+Tab move between the panels and Enter or a click
/// opens the details of what is selected. The task table and the running blocks show the sessions' sub-agents.
/// </summary>
public sealed class OverviewPage : IPage
{
    public const string ConversationPageId = "conversation";

    // The run panel: its border, four lines of values in two columns, and the progress bar.
    private const int RunLinesPerColumn = 4;
    private const int RunPanelHeight = RunLinesPerColumn + 3;
    private const int LogPanelHeight = 11;

    // Left of each list item: the selection marker and a space.
    private const int MarkerWidth = 2;

    public string Id => "overview";

    public string Title => "Overview";

    public Visual Build(IAppContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var run = RunPanel(context);
        var tasks = TaskTable(context);
        var running = RunningPanel(context);
        var log = LogPanel(context);
        Visual[] panels = [run, tasks.List, running.List, log.List];

        var root = new Grid()
            .Rows(
                new RowDefinition().Height(GridLength.Fixed(RunPanelHeight)),
                new RowDefinition().Height(GridLength.Star()),
                new RowDefinition().Height(GridLength.Fixed(LogPanelHeight)))
            .Columns(
                new ColumnDefinition().Width(GridLength.Star(3)),
                new ColumnDefinition().Width(GridLength.Star(2)))
            .Cell(new Group("Run").Content(run).Stretch(), 0, 0, columnSpan: 2)
            .Cell(tasks.Panel("Tasks", TaskTableContent(context, tasks.List)), 1, 0)
            .Cell(running.Panel("Running", RunningPanelContent(context, running.List)), 1, 1)
            .Cell(log.Panel("Log", log.List), 2, 0, columnSpan: 2);
        root.AddCommand(new Command
        {
            Id = "overview.next-panel",
            LabelMarkup = "Next panel",
            Gesture = new KeyGesture(TerminalKey.Tab),
            Execute = _ => MoveFocus(panels, 1),
        });
        root.AddCommand(new Command
        {
            Id = "overview.previous-panel",
            LabelMarkup = "Previous panel",
            Gesture = new KeyGesture(TerminalKey.Tab, TerminalModifiers.Shift),
            Execute = _ => MoveFocus(panels, -1),
        });
        return root;
    }

    /// <summary>7.1, 7.2: the run values in two columns and the progress bar; Enter or a click opens the run pop-up.</summary>
    private static Visual RunPanel(IAppContext context)
    {
        IReadOnlyList<string> Lines() => OverviewText.RunPanel(context.Snapshot.Value, context.Now.Value);
        void ShowRun() => context.ShowPopup("Run", OverviewText.RunPopup(context.Snapshot.Value));

        var body = new VStack(
            new HStack(
                    new Markup(() => string.Join('\n', Lines().Take(RunLinesPerColumn))).IsSelectable(false),
                    new Markup(() => string.Join('\n', Lines().Skip(RunLinesPerColumn))).IsSelectable(false))
                .Spacing(6),
            new ProgressBar().Value(() => OverviewText.Progress(context.Snapshot.Value)));
        var panel = new ScrollViewer(body, focusable: true)
            .HorizontalScrollEnabled(false)
            .PointerPressed(ShowRun);
        panel.AddCommand(new Command
        {
            Id = "overview.run.activate",
            LabelMarkup = "Details",
            Gesture = new KeyGesture(TerminalKey.Enter),
            Execute = _ => ShowRun(),
        });
        return panel;
    }

    /// <summary>
    /// 7.3, 7.4, 39.1: one row per task, followed by the child rows of its sessions. Enter or a click on a task row opens
    /// the task pop-up; on a child row it shows that sub-agent on the Conversation page.
    /// </summary>
    private static PanelList<OverviewRow> TaskTable(IAppContext context)
    {
        // The rows fit the width of the list, which the template reads once the table exists.
        PanelList<OverviewRow>? table = null;
        table = new PanelList<OverviewRow>(
            "tasks",
            TaskRows(context),
            (row, _) => row.Key,
            row => new Markup(() =>
            {
                if (row.Child is { } child)
                {
                    return OverviewText.ChildRow(child, context.Now.Value, RowWidth(table!.List));
                }
                var (id, title, detail) = TaskWidths(context, table!.List);
                return OverviewText.TaskRow(row.Task, context.Now.Value, id, title, detail);
            }).IsSelectable(false),
            row =>
            {
                if (row.Child is { } child)
                {
                    ShowConversation(context, AgentKey.Of(child.Session, child.SubAgent.Id));
                    return;
                }
                context.ShowPopup($"{row.Task.Id} - {row.Task.Title}", OverviewText.TaskPopup(row.Task, context.Snapshot.Value));
            },
            "Details");
        table.List.AutoFocus(true);
        return table;
    }

    /// <summary>
    /// The task table's rows of the current snapshot (39.1). While they equal the last rows the same list comes back, as
    /// <see cref="PanelList{T}"/> keeps its items only while they are the same instances.
    /// </summary>
    private static Func<IReadOnlyList<OverviewRow>> TaskRows(IAppContext context)
    {
        RunSnapshot? snapshot = null;
        IReadOnlyList<OverviewRow> rows = [];
        return () =>
        {
            var current = context.Snapshot.Value;
            if (!ReferenceEquals(current, snapshot))
            {
                var next = OverviewText.TaskRows(current);
                if (!next.SequenceEqual(rows))
                {
                    rows = next;
                }
                snapshot = current;
            }
            return rows;
        };
    }

    private static Visual TaskTableContent(IAppContext context, Visual list) => new VStack(
        new Markup(() =>
        {
            if (context.Snapshot.Value.Plan is null)
            {
                return Look.Tag("muted", "No plan yet");
            }
            var (id, title, detail) = TaskWidths(context, list);
            return new string(' ', MarkerWidth) + OverviewText.TaskHeader(id, title, detail);
        }).IsSelectable(false),
        list.Stretch());

    private static (int Id, int Title, int Detail) TaskWidths(IAppContext context, Visual list) =>
        OverviewText.TaskWidths(context.Snapshot.Value.Tasks, RowWidth(list));

    /// <summary>The width of a row of the list, right of the selection marker.</summary>
    private static int RowWidth(Visual list) => list.Bounds.Width - MarkerWidth;

    /// <summary>
    /// 7.5-7.7, 39.2: one block per running session, its sub-agents' lines included; Enter or a click shows the session on
    /// the Conversation page.
    /// </summary>
    private static PanelList<Session> RunningPanel(IAppContext context) => new(
        "running",
        () => OverviewText.RunningSessions(context.Snapshot.Value),
        (session, _) => session.Files.Key,
        session => new Markup(() => string.Join('\n', OverviewText.RunningBlock(session, context.Snapshot.Value, context.Now.Value))).IsSelectable(false),
        session => ShowConversation(context, session.Files.Key),
        "Conversation");

    /// <summary>Selects the session, or the sub-agent of an <see cref="AgentKey"/>, and shows the Conversation page.</summary>
    private static void ShowConversation(IAppContext context, string key)
    {
        context.SelectedSessionKey.Value = key;
        context.ShowPage(ConversationPageId);
    }

    private static Visual RunningPanelContent(IAppContext context, Visual list) => new VStack(
        new Markup(() => Look.Tag("muted", "No running sessions"))
            .IsSelectable(false)
            .IsVisible(() => OverviewText.RunningSessions(context.Snapshot.Value).Count == 0),
        list.Stretch());

    /// <summary>7.8, 7.9: every progress entry, following the newest; Enter or a click opens the whole message.</summary>
    private static PanelList<ProgressEntry> LogPanel(IAppContext context) => new(
        "log",
        () => context.Snapshot.Value.Progress,
        (_, index) => index.ToString(CultureInfo.InvariantCulture),
        entry => new Markup(OverviewText.LogLine(entry)).IsSelectable(false),
        entry => context.ShowPopup("Log entry", OverviewText.LogPopup(entry)),
        "Details",
        followTail: true);

    /// <summary>Focuses the panel <paramref name="step"/> places after (or before) the one that has the focus.</summary>
    private static void MoveFocus(Visual[] panels, int step)
    {
        var app = panels[0].App;
        if (app is null)
        {
            return;
        }
        var current = Array.FindIndex(panels, panel => IsWithin(app.FocusedElement, panel));
        var next = current < 0 ? 0 : (current + step + panels.Length) % panels.Length;
        app.Focus(panels[next]);
    }

    private static bool IsWithin(Visual? visual, Visual ancestor)
    {
        for (var current = visual; current is not null; current = current.Parent)
        {
            if (current == ancestor)
            {
                return true;
            }
        }
        return false;
    }
}

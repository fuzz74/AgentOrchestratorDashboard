using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation;
using OrchDash.Pages.Usage.Format;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Pages.Usage;

/// <summary>
/// The visuals and the selection state of one <see cref="UsagePage"/>. Everything shown is a function of
/// <see cref="IAppContext.Snapshot"/>, <see cref="IAppContext.SelectedSessionKey"/> and the group the user picked last;
/// the tables are derived once per snapshot.
/// </summary>
internal sealed class UsageView
{
    private const string ChartTitle = "Tokens per group";

    private const string EmptyText = "No sessions yet";

    // The run panel: its border, the figures in columns, the rate limit line and the versions line.
    private const int RunColumns = 3;
    private const int RunLinesPerColumn = 4;
    private const int RunPanelHeight = RunLinesPerColumn + 4;

    // Left of each list row: the selection marker of SelectableList and a space.
    private const string MarkerSpace = "  ";

    private readonly IAppContext _context;
    private readonly State<GroupPick?> _pick = new(null);
    private readonly SelectableList _groupList;
    private readonly SelectableList _sessionList;

    private RunSnapshot? _tablesFor;
    private UsageTables? _tables;

    public UsageView(IAppContext context)
    {
        _context = context;
        _groupList = new SelectableList(
            "usage.groups", () => Tables().GroupRows, GroupSelection, SelectGroup, _ => FocusSessions(),
            activateLabel: "Sessions", emptyText: EmptyText);
        _sessionList = new SelectableList(
            "usage.sessions", SessionRows, SessionSelection, SelectSession, OpenSession,
            activateLabel: "Details", activateOnClick: true, emptyText: EmptyText);
        _groupList.View.AutoFocus(true);

        Root = new Grid()
            .Rows(
                new RowDefinition().Height(GridLength.Fixed(RunPanelHeight)),
                new RowDefinition().Height(GridLength.Star(2)),
                new RowDefinition().Height(GridLength.Star(3)))
            .Columns(
                new ColumnDefinition().Width(GridLength.Star(2)),
                new ColumnDefinition().Width(GridLength.Star(1)))
            .Cell(Pane("Run", RunPanel()), 0, 0, columnSpan: 2)
            .Cell(Pane("Groups", Table(() => Tables().GroupHeader, _groupList)), 1, 0)
            .Cell(new Group().Content(new ComputedVisual(Chart)).Stretch(), 1, 1)
            .Cell(Pane("Sessions", Table(() => Tables().SessionHeader, _sessionList)), 2, 0, columnSpan: 2);
        AddCommands(Root);
    }

    public Visual Root { get; }

    private static Group Pane(string title, Visual content) =>
        new Group().TopLeftText(new Markup(Look.Tag("bold", title))).Content(content).Stretch();

    /// <summary>A header line above a list, aligned with the list's rows.</summary>
    private static Visual Table(Func<string> header, SelectableList list) => new VStack(
        new Markup(header).IsSelectable(false),
        list.View.Stretch());

    /// <summary>16.1-16.3: the run figures in columns, the rate limit line and the versions line.</summary>
    private Visual RunPanel() => new VStack(
        new HStack([.. Enumerable.Range(0, RunColumns).Select(RunColumn)]).Spacing(6),
        new Markup(() => Tables().RateLimitLine).IsSelectable(false),
        new Markup(() => Tables().VersionsLine).IsSelectable(false));

    private Visual RunColumn(int column) => new Markup(() =>
        string.Join('\n', Tables().RunLines.Skip(column * RunLinesPerColumn).Take(RunLinesPerColumn))).IsSelectable(false);

    /// <summary>16.5: one bar per group; a chart cannot follow a state, so it is rebuilt with each snapshot.</summary>
    private Visual Chart()
    {
        var bars = Tables().Bars;
        var title = new Markup(Look.Tag("bold", ChartTitle));
        if (bars.IsEmpty)
        {
            return new VStack(title, new Markup(Look.Tag("muted", EmptyText)));
        }
        return new BarChart(bars.Select(bar =>
                new BarChartItem(new TextBlock(bar.Name), bar.Tokens).ValueLabel(new TextBlock(Look.Tokens(bar.Tokens)))))
            .Title(title)
            .ShowValues(true);
    }

    /// <summary>16.9: Tab and Shift+Tab move the focus between the two tables, wherever it is on the page.</summary>
    private void AddCommands(Visual root)
    {
        root.AddCommand(new Command
        {
            Id = "usage.switch",
            LabelMarkup = "Switch table",
            Gesture = new KeyGesture(TerminalKey.Tab),
            Execute = _ => SwitchTable(),
        });
        root.AddCommand(new Command
        {
            Id = "usage.switchback",
            LabelMarkup = "Switch table",
            Gesture = new KeyGesture(TerminalKey.Tab, TerminalModifiers.Shift),
            Presentation = CommandPresentation.None,
            Execute = _ => SwitchTable(),
        });
    }

    private void SwitchTable()
    {
        if (_sessionList.View.HasFocus)
        {
            Root.App?.Focus(_groupList.View);
        }
        else
        {
            FocusSessions();
        }
    }

    private void FocusSessions() => Root.App?.Focus(_sessionList.View);

    private UsageTables Tables()
    {
        var snapshot = _context.Snapshot.Value;
        if (_tables is null || !ReferenceEquals(snapshot, _tablesFor))
        {
            _tables = UsageTables.Of(snapshot);
            _tablesFor = snapshot;
        }
        return _tables;
    }

    /// <summary>
    /// 16.7, 16.10: the group of the session with <see cref="IAppContext.SelectedSessionKey"/> while that key was set
    /// elsewhere after the user's last pick (or before any pick); else the picked group by name; else the first group;
    /// -1 without groups.
    /// </summary>
    private int SelectedGroupIndex()
    {
        var groups = Tables().Groups;
        if (groups.IsEmpty)
        {
            return -1;
        }
        var key = _context.SelectedSessionKey.Value;
        var pick = _pick.Value;
        if ((pick is null || pick.Value.Key != key) && IndexOf(groups, group => IndexOfSession(group, key) >= 0) is var keyed and >= 0)
        {
            return keyed;
        }
        if (pick is { } picked && IndexOf(groups, group => group.Name == picked.Name) is var named and >= 0)
        {
            return named;
        }
        return 0;
    }

    private UsageGroup? SelectedGroup() => SelectedGroupIndex() is var index and >= 0 ? Tables().Groups[index] : null;

    private ListSelection GroupSelection() => new(null, SelectedGroupIndex());

    private void SelectGroup(int index) => _pick.Value = new GroupPick(Tables().Groups[index].Name, _context.SelectedSessionKey.Value);

    private ImmutableArray<ImmutableArray<string>> SessionRows() =>
        SelectedGroupIndex() is var index and >= 0 ? Tables().SessionRows[index] : [];

    /// <summary>The session with <see cref="IAppContext.SelectedSessionKey"/> when it is in the selected group, else the group's first.</summary>
    private ListSelection SessionSelection()
    {
        if (SelectedGroup() is not { } group)
        {
            return new ListSelection(null, -1);
        }
        return new ListSelection(group.Name, Math.Max(0, IndexOfSession(group, _context.SelectedSessionKey.Value)));
    }

    private void SelectSession(int index)
    {
        if (SelectedGroup() is { } group)
        {
            var key = group.Sessions[index].Files.Key;
            _pick.Value = new GroupPick(group.Name, key);
            _context.SelectedSessionKey.Value = key;
        }
    }

    /// <summary>16.8: the session's usage pop-up.</summary>
    private void OpenSession(int index)
    {
        if (SelectedGroup() is { } group && index < group.Sessions.Length)
        {
            var session = group.Sessions[index];
            _context.ShowPopup(UsageText.SessionPopupTitle(session), UsageText.SessionPopup(_context.Snapshot.Value, session));
        }
    }

    private static int IndexOfSession(UsageGroup group, string? key) =>
        key is null ? -1 : IndexOf(group.Sessions, session => session.Files.Key == key);

    private static int IndexOf<T>(ImmutableArray<T> items, Func<T, bool> match)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (match(items[i]))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Everything the page shows of one snapshot, as markup lines.</summary>
    private sealed record UsageTables(
        IReadOnlyList<string> RunLines,
        string RateLimitLine,
        string VersionsLine,
        ImmutableArray<UsageGroup> Groups,
        string GroupHeader,
        ImmutableArray<ImmutableArray<string>> GroupRows,
        ImmutableArray<UsageBar> Bars,
        string SessionHeader,
        ImmutableArray<ImmutableArray<ImmutableArray<string>>> SessionRows)   // per group, per session
    {
        public static UsageTables Of(RunSnapshot snapshot)
        {
            var groups = UsageRules.Groups(snapshot);
            var nameWidth = UsageText.GroupNameWidth(groups);
            return new UsageTables(
                UsageText.RunPanel(snapshot),
                UsageText.RateLimitLine(snapshot),
                UsageText.VersionsLine(snapshot),
                groups,
                MarkerSpace + UsageText.GroupHeader(nameWidth),
                [.. groups.Select(group => ImmutableArray.Create(UsageText.GroupRow(group, nameWidth)))],
                UsageText.BarItems(groups),
                MarkerSpace + UsageText.SessionHeader(),
                [.. groups.Select(group => group.Sessions.Select(session => ImmutableArray.Create(UsageText.SessionRow(snapshot, session))).ToImmutableArray())]);
        }
    }
}

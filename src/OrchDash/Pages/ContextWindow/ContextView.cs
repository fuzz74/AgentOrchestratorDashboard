using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.ContextWindow.Format;
using OrchDash.Pages.Conversation;
using OrchDash.Pages.Conversation.Format;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Pages.ContextWindow;

/// <summary>
/// The visuals and the selection state of one <see cref="ContextPage"/>. Everything shown is a function of
/// <see cref="IAppContext.Snapshot"/>, <see cref="IAppContext.SelectedSessionKey"/> and the call and part the user
/// selected last; the make-up and the rows are cached until one of those changes.
/// </summary>
internal sealed class ContextView
{
    private const double ListShare = 0.25;
    private const int ChartRows = 8;          // height of the chart and call panes, borders included
    private const int CallPaneWidth = 80;     // a call row with "#1" and "tool_calls", the marker, borders and scroll bar
    private const int LabelCap = 40;          // widest part label before it is cut

    private readonly IAppContext _context;
    private readonly State<ListCursor> _callCursor = new(ListCursor.None);
    private readonly State<ListCursor> _partCursor = new(ListCursor.None);
    private readonly SelectableList _sessionList;
    private readonly SelectableList _callList;
    private readonly SelectableList _partList;
    private readonly ScrollViewer[] _focusOrder;

    private string? _shownKey;
    private int _showing;

    private RunSnapshot? _sessionsFor;
    private ImmutableArray<Session> _sessions = [];
    private ImmutableArray<ImmutableArray<string>> _sessionRows = [];
    private (RunSnapshot Snapshot, Session Session)? _makeupFor;
    private ContextMakeup? _makeup;
    private ImmutableArray<ImmutableArray<string>> _callRows = [];
    private (ContextMakeup Makeup, int Call)? _partsFor;
    private ImmutableArray<ContextPart> _parts = [];
    private ImmutableArray<ImmutableArray<string>> _partRows = [];

    public ContextView(IAppContext context)
    {
        _context = context;
        _sessionList = new SelectableList(
            "context.sessions", SessionRows, SessionSelection, SelectSession, _ => FocusList(1),
            activateLabel: "Calls", emptyText: "No sessions yet");
        _callList = new SelectableList(
            "context.calls", CallRows, CallSelection, SelectCall, _ => FocusList(2),
            activateLabel: "Parts", endLabel: "Follow", emptyText: "no model calls yet");
        _partList = new SelectableList(
            "context.parts", PartRows, PartSelection, SelectPart, OpenPart,
            activateLabel: "Open", activateOnClick: true, emptyText: "no parts");
        _sessionList.View.AutoFocus(true);
        _focusOrder = [_sessionList.View, _callList.View, _partList.View];

        Root = new HSplitter(Pane("Sessions", _sessionList.View), Details()).Ratio(ListShare);
        AddCommands(Root);
    }

    public Visual Root { get; }

    private static Group Pane(string title, Visual content) => Pane(new Markup(Look.Tag("bold", title)), content);

    private static Group Pane(Visual title, Visual content) =>
        new Group().TopLeftText(title).Content(content).Stretch();

    /// <summary>The right side: header, chart and calls, the make-up of the selected call, and its parts below.</summary>
    private Visual Details()
    {
        var top = new VStack(
            new Markup(() => string.Join('\n', HeaderLines())) { Wrap = true },
            new HStack(
                    Pane("Context per call", new ComputedVisual(Chart)),
                    Pane("Calls", _callList.View).MinWidth(CallPaneWidth).MaxWidth(CallPaneWidth))
                .MinHeight(ChartRows).MaxHeight(ChartRows),
            Pane(new Markup(() => Look.Tag("bold", MakeupTitle())), new ComputedVisual(Breakdown)));
        return new DockLayout().Top(top).Content(Pane("Parts", _partList.View));
    }

    /// <summary>
    /// The page's keys, on the root so that they work from each of the three lists: Tab and Shift+Tab move the focus
    /// (15.13), 's' and 't' open the system prompt and the tool definitions (15.11, 15.12).
    /// </summary>
    private void AddCommands(Visual root)
    {
        root.AddCommand(new Command
        {
            Id = "context.switch",
            LabelMarkup = "Switch list",
            Gesture = new KeyGesture(TerminalKey.Tab),
            Execute = _ => MoveFocus(1),
        });
        root.AddCommand(new Command
        {
            Id = "context.switchback",
            LabelMarkup = "Switch list",
            Gesture = new KeyGesture(TerminalKey.Tab, TerminalModifiers.Shift),
            Presentation = CommandPresentation.None,
            Execute = _ => MoveFocus(-1),
        });
        root.AddCommand(new Command
        {
            Id = "context.system",
            LabelMarkup = "System prompt",
            Gesture = new KeyGesture('s'),
            Execute = _ => OpenSystemPrompt(),
        });
        root.AddCommand(new Command
        {
            Id = "context.tools",
            LabelMarkup = "Tool definitions",
            Gesture = new KeyGesture('t'),
            Execute = _ => OpenTools(),
        });
    }

    /// <summary>Moves the focus to the next (1) or previous (-1) of session list, call list and part list.</summary>
    private void MoveFocus(int direction)
    {
        var current = Array.FindIndex(_focusOrder, view => view.HasFocus);
        var next = current >= 0 ? (current + direction + _focusOrder.Length) % _focusOrder.Length
            : direction > 0 ? 0
            : _focusOrder.Length - 1;
        FocusList(next);
    }

    private void FocusList(int index) => Root.App?.Focus(_focusOrder[index]);

    private ImmutableArray<Session> Sessions()
    {
        var snapshot = _context.Snapshot.Value;
        if (!ReferenceEquals(snapshot, _sessionsFor))
        {
            _sessions = ConversationText.OrderSessions(snapshot);
            var nameWidth = _sessions.IsEmpty ? 0 : _sessions.Max(s => ContextText.SessionName(s).Length);
            _sessionRows = [.. _sessions.Select(s => ImmutableArray.Create(ContextText.SessionRow(s, nameWidth)))];
            _sessionsFor = snapshot;
        }
        return _sessions;
    }

    private ImmutableArray<ImmutableArray<string>> SessionRows()
    {
        Sessions();
        return _sessionRows;
    }

    /// <summary>The session with the key <see cref="IAppContext.SelectedSessionKey"/>, else the first one; -1 without sessions.</summary>
    private int SelectedSessionIndex()
    {
        var sessions = Sessions();
        var key = _context.SelectedSessionKey.Value;
        if (sessions.IsEmpty)
        {
            return -1;
        }
        for (var i = 0; i < sessions.Length; i++)
        {
            if (sessions[i].Files.Key == key)
            {
                return i;
            }
        }
        return 0;
    }

    private Session? SelectedSession() => SelectedSessionIndex() is var index and >= 0 ? Sessions()[index] : null;

    private ListSelection SessionSelection() => new(null, SelectedSessionIndex());

    private void SelectSession(int index) => _context.SelectedSessionKey.Value = Sessions()[index].Files.Key;

    /// <summary>
    /// The number of the current showing of the selected session: it grows each time the page shows another session,
    /// whether this page or another one changed the key. A cursor of an earlier showing is ignored, so a newly shown
    /// session starts at its last call and its first part (15.7).
    /// </summary>
    private int Showing(Session session)
    {
        if (session.Files.Key != _shownKey)
        {
            _shownKey = session.Files.Key;
            _showing++;
        }
        return _showing;
    }

    /// <summary>The make-up of the selected session, cached per snapshot and session; null without sessions.</summary>
    private ContextMakeup? Makeup(Session? session)
    {
        if (session is null)
        {
            return null;
        }
        var snapshot = _context.Snapshot.Value;
        if (_makeupFor is not { } key || !ReferenceEquals(key.Snapshot, snapshot) || !ReferenceEquals(key.Session, session))
        {
            _makeup = ContextMakeup.Build(snapshot, session);
            _callRows = [.. Enumerable.Range(0, _makeup.Calls.Length).Select(i => ImmutableArray.Create(ContextText.CallRow(_makeup, i)))];
            _makeupFor = (snapshot, session);
        }
        return _makeup;
    }

    private ImmutableArray<string> HeaderLines() =>
        SelectedSession() is { } session && Makeup(session) is { } makeup
            ? ContextText.Header(_context.Snapshot.Value, session, makeup, SelectedCall())
            : [];

    /// <summary>15.5: the contexts of the calls with usage from 0 to the largest one, or "no context sizes".</summary>
    private Visual Chart()
    {
        var values = Makeup(SelectedSession()) is { } makeup ? ContextText.ChartValues(makeup) : [];
        if (values.IsEmpty)
        {
            return new Markup(Look.Tag("muted", "no context sizes"));
        }
        // A maximum of at least 1 keeps the scale above the minimum when every context is 0.
        return new LineChart(values).Minimum(0).Maximum(Math.Max(1, values.Max())).Stretch();
    }

    private ImmutableArray<ImmutableArray<string>> CallRows() => Makeup(SelectedSession()) is not null ? _callRows : [];

    /// <summary>
    /// The selected call: on a change of session that session's last call; the chain's last call while the user's
    /// last pick was the last call and the session runs (15.7); else the user's pick by position (15.14).
    /// </summary>
    private ListSelection CallSelection()
    {
        var session = SelectedSession();
        if (session is null || Makeup(session) is not { } makeup || makeup.Calls.IsEmpty)
        {
            return new ListSelection(null, -1);
        }
        var count = makeup.Calls.Length;
        var cursor = _callCursor.Value;
        var index = cursor.Showing != Showing(session) ? LastCallOf(session, makeup)
            : cursor.AtEnd && session.State == SessionState.Running ? count - 1
            : Math.Min(cursor.Index, count - 1);
        return new ListSelection(session.Files.Key, index);
    }

    /// <summary>The chain index of the session's own last call, or the chain's last call when it has none.</summary>
    private static int LastCallOf(Session session, ContextMakeup makeup)
    {
        for (var i = makeup.Calls.Length - 1; i >= 0; i--)
        {
            if (makeup.Calls[i].Session.Files.Key == session.Files.Key)
            {
                return i;
            }
        }
        return makeup.Calls.Length - 1;
    }

    private int SelectedCall() => CallSelection().Index;

    private void SelectCall(int index)
    {
        if (SelectedSession() is { } session)
        {
            _callCursor.Value = new ListCursor(Showing(session), index, index == CallRows().Length - 1);
        }
    }

    private string MakeupTitle() =>
        SelectedCall() is var call and >= 0 ? $"Make-up at call {Words.Number(call + 1)}" : "Make-up";

    /// <summary>
    /// 15.8: the stacked bar of the categories with tokens and one line per category; a click on a segment shows its
    /// tip, a click on a category line opens the explanation of the category (<see cref="CategoryHelp"/>).
    /// </summary>
    private Visual Breakdown()
    {
        if (Makeup(SelectedSession()) is not { } makeup)
        {
            return new Markup("");
        }
        var totals = makeup.TotalsAt(SelectedCall());
        var visuals = new List<Visual>();
        var segments = ContextText.CategorySegments(totals);
        if (!segments.IsEmpty)
        {
            visuals.Add(Bar(totals, segments));
        }
        foreach (var total in totals)
        {
            var category = total.Category;
            visuals.Add(new Markup(ContextText.CategoryLine(total))
                .PointerPressed(() => Show(CategoryHelp.Popup(category))));
        }
        return new VStack([.. visuals]);
    }

    /// <summary>
    /// The stacked bar; a click on a segment shows its tip at the pointer. Pointer moves never reach the bar: the
    /// hover tooltip of XenoAtom.Terminal.UI 3.10.0 ends the app with "The visual is already part of the UI tree."
    /// when the pointer crosses into the next segment.
    /// </summary>
    private static Visual Bar(ImmutableArray<CategoryTotal> totals, ImmutableArray<CategorySegment> segments)
    {
        var pressed = (X: 0, Y: 0);
        return new BreakdownChart(segments.Select(segment =>
                new BreakdownSegment(segment.Tokens, new Markup(Look.Tag(segment.Color, segment.Name)))
                {
                    Color = Enum.Parse<ConsoleColor>(segment.Color, ignoreCase: true),
                }))
            .ShowValues(false)
            .ShowPercentages(false)
            .PointerMoved((_, e) => e.Handled = true)
            .PointerPressed((_, e) => pressed = (e.UiX, e.UiY))
            .SegmentClicked((_, e) =>
                ShowTip(totals.First(t => t.Category == segments[e.Index].Category), pressed.X, pressed.Y));
    }

    /// <summary>
    /// Shows the tip of a category above the cell (<paramref name="x"/>, <paramref name="y"/>) until the mouse moves,
    /// a key is pressed or the user clicks elsewhere.
    /// </summary>
    private static void ShowTip(CategoryTotal total, int x, int y)
    {
        var tip = new Popup { AnchorRect = new Rectangle(x, y, 1, 1) }
            .Placement(PopupPlacement.Above)
            .MatchAnchorWidth(false);
        // The pop-up is modal, so keys only arrive while something in it has the focus.
        var text = new ScrollViewer(new Markup(ContextText.CategoryTip(total)), focusable: true)
            .KeyDown((_, e) =>
            {
                tip.Close();
                e.Handled = true;
            });
        tip.Content(new Border(text)).PointerMoved((_, _) => tip.Close());
        tip.Show();
    }

    private ImmutableArray<ContextPart> Parts() => PartsAndRows().Parts;

    private ImmutableArray<ImmutableArray<string>> PartRows() => PartsAndRows().Rows;

    /// <summary>The parts in the context at the selected call and their rows, cached per make-up and call.</summary>
    private (ImmutableArray<ContextPart> Parts, ImmutableArray<ImmutableArray<string>> Rows) PartsAndRows()
    {
        if (Makeup(SelectedSession()) is not { } makeup)
        {
            return ([], []);
        }
        var call = SelectedCall();
        if (_partsFor is not { } key || !ReferenceEquals(key.Makeup, makeup) || key.Call != call)
        {
            _parts = makeup.PartsAt(call);
            var labelWidth = _parts.IsEmpty ? 0 : Math.Min(LabelCap, _parts.Max(p => p.Label.Length));
            _partRows = [.. _parts.Select(p => ImmutableArray.Create(ContextText.PartRow(p, labelWidth)))];
            _partsFor = (makeup, call);
        }
        return (_parts, _partRows);
    }

    /// <summary>The selected part: the user's pick by position in this showing of the session (15.14), else the first.</summary>
    private ListSelection PartSelection()
    {
        var session = SelectedSession();
        var count = Parts().Length;
        if (session is null || count == 0)
        {
            return new ListSelection(null, -1);
        }
        var cursor = _partCursor.Value;
        var index = cursor.Showing != Showing(session) ? 0 : Math.Min(cursor.Index, count - 1);
        return new ListSelection((session.Files.Key, SelectedCall()), index);
    }

    private void SelectPart(int index)
    {
        if (SelectedSession() is { } session)
        {
            _partCursor.Value = new ListCursor(Showing(session), index, false);
        }
    }

    private void OpenPart(int index)
    {
        var parts = Parts();
        if (index < parts.Length)
        {
            Show(ContextText.PartPopup(parts[index]));
        }
    }

    private void OpenSystemPrompt()
    {
        if (SelectedSession() is { } session)
        {
            Show(ContextText.SystemPromptPopup(session));
        }
    }

    private void OpenTools()
    {
        if (SelectedSession() is { } session && Makeup(session) is { } makeup)
        {
            Show(ContextText.ToolsPopup(session, makeup));
        }
    }

    private void Show(ContextPopup popup) => _context.ShowPopup(popup.Title, popup.Sections);
}

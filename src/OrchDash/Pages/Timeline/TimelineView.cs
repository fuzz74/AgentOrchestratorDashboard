using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Core.Timeline;
using OrchDash.Pages.Conversation;
using OrchDash.Pages.Timeline.Format;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Pages.Timeline;

/// <summary>
/// The visuals and the selection state of one <see cref="TimelinePage"/>: the filter row and the list of the merged
/// timeline. Everything shown is a function of <see cref="IAppContext.Snapshot"/>, the filters and the event the user
/// selected last; the timeline is cached per snapshot and the rows per timeline and filters.
/// </summary>
internal sealed class TimelineView
{
    public const string ConversationPageId = "conversation";

    private readonly IAppContext _context;
    private readonly State<ImmutableHashSet<TimelineKind>> _shown = new(TimelineText.AllKinds);
    private readonly State<string?> _taskGroup = new(null);
    private readonly State<TimelineCursor> _cursor = new(TimelineCursor.Start);
    private readonly SelectableList _list;

    private RunSnapshot? _eventsFor;
    private ImmutableArray<TimelineEvent> _events = [];
    private (ImmutableArray<TimelineEvent> Events, ImmutableHashSet<TimelineKind> Shown, string? Group)? _visibleFor;
    private ImmutableArray<TimelineEvent> _visible = [];
    private ImmutableArray<ImmutableArray<string>> _rows = [];

    // While the cursor is at the end: the key of the last row the selection followed, which stays selected once the run
    // is no longer active (33.7). Written while the selection is computed; it is no state that a frame depends on.
    private string? _followedKey;

    // Whether the last select was a click on the row that was already selected; null once that input is handled.
    private bool? _clickOnSelected;

    public TimelineView(IAppContext context)
    {
        _context = context;
        _list = new SelectableList(
            "timeline.events", Rows, Selection, Select, Open,
            activateLabel: "Open", endLabel: "Follow", activateOnClick: true, emptyText: TimelineText.NoEvents);
        _list.View.AutoFocus(true);
        AddCommands(_list.View);

        Root = new DockLayout()
            .Top(FilterRow())
            .Content(new Group().TopLeftText(new Markup(Look.Tag("bold", "Events"))).Content(_list.View).Stretch());
    }

    public Visual Root { get; }

    /// <summary>33.1-33.3, 33.6: the five kind toggles, the task filter and <c>▶ replay here</c>, each clickable.</summary>
    private Visual FilterRow()
    {
        var kinds = TimelineText.Filters.Select(filter =>
            Label(() => TimelineText.FilterLabel(filter, TimelineText.IsActive(filter, _shown.Value)), () => ToggleKind(filter)));
        return new HStack(
            [
                .. kinds,
                Label(() => TimelineText.TaskLabel(_taskGroup.Value), ToggleTask),
                Label(TimelineText.ReplayLabel, ReplayHere),
            ])
            .Spacing(2);
    }

    /// <summary>
    /// A clickable label. A second click on the same cell also raises a pressed event of kind DoubleClick after its
    /// Down; only the Down counts, so that two clicks toggle twice and not three times.
    /// </summary>
    private static Visual Label(Func<string> markup, Action click) =>
        new Markup(markup) { Wrap = false }.IsSelectable(false).PointerPressed((_, e) =>
        {
            if (e.Button != TerminalMouseButton.Left)
            {
                return;
            }
            e.Handled = true;
            if (e.Kind == TerminalMouseKind.Down)
            {
                click();
            }
        });

    private ImmutableArray<TimelineEvent> Events()
    {
        var snapshot = _context.Snapshot.Value;
        if (!ReferenceEquals(snapshot, _eventsFor))
        {
            _events = TimelineBuilder.Build(snapshot);
            _eventsFor = snapshot;
        }
        return _events;
    }

    /// <summary>The events that pass the filters, and their rows, cached per timeline and filters.</summary>
    private ImmutableArray<TimelineEvent> Visible()
    {
        var events = Events();
        var shown = _shown.Value;
        var group = _taskGroup.Value;
        if (_visibleFor is not { } key || key.Events != events || !ReferenceEquals(key.Shown, shown) || key.Group != group)
        {
            _visible = TimelineText.Visible(events, shown, group);
            _rows = [.. TimelineText.Rows(_visible).Select(row => ImmutableArray.Create(row))];
            _visibleFor = (events, shown, group);
        }
        return _visible;
    }

    private ImmutableArray<ImmutableArray<string>> Rows()
    {
        Visible();
        return _rows;
    }

    private bool RunIsActive() =>
        _context.Snapshot.Value.Run.Phase is RunPhase.Planning or RunPhase.Running or RunPhase.Stopping;

    /// <summary>
    /// 33.7: while the user's last pick was the last row (or before any pick), the last row while the run is active,
    /// else the row followed last; otherwise the picked event by key; the last row when the key is gone; -1 without rows.
    /// </summary>
    private int SelectedIndex()
    {
        var visible = Visible();
        if (visible.IsEmpty)
        {
            return -1;
        }
        var last = visible.Length - 1;
        var cursor = _cursor.Value;
        if (!cursor.AtEnd)
        {
            return IndexOf(visible, cursor.Key) ?? last;
        }
        if (!RunIsActive() && IndexOf(visible, _followedKey) is { } followed)
        {
            return followed;
        }
        _followedKey = visible[last].Key;
        return last;
    }

    private static int? IndexOf(ImmutableArray<TimelineEvent> events, string? key)
    {
        for (var i = 0; i < events.Length; i++)
        {
            if (events[i].Key == key)
            {
                return i;
            }
        }
        return null;
    }

    private TimelineEvent? SelectedEvent() => SelectedIndex() is var index and >= 0 ? Visible()[index] : null;

    private ListSelection Selection() => SelectedEvent() is { } selected
        ? new ListSelection(selected.Key, SelectedIndex())
        : new ListSelection(null, -1);

    /// <summary>Up and the other moves pick a row; picking the last row (End, or a click on it) follows again (33.7).</summary>
    private void Select(int index)
    {
        var visible = Visible();
        _clickOnSelected = index == SelectedIndex();
        Root.App?.Post(() => _clickOnSelected = null);
        var atEnd = index == visible.Length - 1;
        _followedKey = atEnd ? visible[index].Key : null;
        _cursor.Value = new TimelineCursor(visible[index].Key, atEnd);
    }

    /// <summary>
    /// 33.4: Enter on a row, or a click on the row that was already selected, opens the event's pop-up. A click calls
    /// <see cref="Select"/> and then this; Enter never passes through <see cref="Select"/>, so only a click on another
    /// row (false) keeps the pop-up closed.
    /// </summary>
    private void Open(int index)
    {
        var visible = Visible();
        if (_clickOnSelected is false || index >= visible.Length)
        {
            return;
        }
        _context.ShowPopup(TimelineText.PopupTitle(visible[index]), TimelineText.Popup(visible[index]));
    }

    private void ToggleKind(KindFilter filter) => _shown.Value = TimelineText.Toggle(filter, _shown.Value);

    /// <summary>33.3: on, the selected event's group (and <c>run</c>); off, every group.</summary>
    private void ToggleTask()
    {
        if (_taskGroup.Value is not null)
        {
            _taskGroup.Value = null;
        }
        else if (SelectedEvent() is { } selected)
        {
            _taskGroup.Value = selected.Group;
        }
    }

    /// <summary>
    /// 33.5: the selected event's session, or for an orchestrator event of a task that task's last session in snapshot
    /// order, on the Conversation page; nothing without one.
    /// </summary>
    private void ShowConversation()
    {
        if (SelectedEvent() is not { } selected)
        {
            return;
        }
        var key = selected.Session?.Files.Key;
        if (key is null && selected.Kind == TimelineKind.Orchestrator)
        {
            key = _context.Snapshot.Value.Sessions.LastOrDefault(s => s.Files.TaskId == selected.Group)?.Files.Key;
        }
        if (key is not null)
        {
            _context.SelectedSessionKey.Value = key;
            _context.ShowPage(ConversationPageId);
        }
    }

    /// <summary>33.6: the whole dashboard at the selected event's time.</summary>
    private void ReplayHere()
    {
        if (SelectedEvent() is { } selected)
        {
            _context.Replay(selected.Time);
        }
    }

    private void AddCommands(Visual list)
    {
        Add("conversation", "Conversation", new KeyGesture('c'), ShowConversation);
        Add("replay", "Replay here", new KeyGesture('t'), ReplayHere);
        Add("task", "Task filter", new KeyGesture('f'), ToggleTask);
        foreach (var filter in TimelineText.Filters)
        {
            Add($"kind.{filter.Key}", filter.CommandLabel, new KeyGesture(filter.Key), () => ToggleKind(filter));
        }

        void Add(string name, string label, KeyGesture gesture, Action execute) =>
            list.AddCommand(new Command
            {
                Id = $"timeline.{name}",
                LabelMarkup = label,
                Gesture = gesture,
                Presentation = CommandPresentation.CommandBar,
                Execute = _ => execute(),
            });
    }
}

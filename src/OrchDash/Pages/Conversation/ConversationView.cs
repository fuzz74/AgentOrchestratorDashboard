using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Pages.Conversation;

/// <summary>
/// The visuals and the selection state of one <see cref="ConversationPage"/>. Everything shown is a function of
/// <see cref="IAppContext.Snapshot"/>, <see cref="IAppContext.Now"/>, <see cref="IAppContext.SelectedSessionKey"/> and
/// the entry the user selected last; the derived lists are cached until one of those changes.
/// </summary>
internal sealed class ConversationView
{
    private const double ListShare = 1.0 / 3;

    private readonly IAppContext _context;
    private readonly State<EntryCursor> _cursor = new(EntryCursor.None);
    private readonly SelectableList _sessionList;
    private readonly SelectableList _entryList;

    private RunSnapshot? _itemsFor;
    private ImmutableArray<SessionItem> _items = [];
    private (ImmutableArray<SessionItem> Items, DateTimeOffset Now)? _rowsFor;
    private ImmutableArray<ImmutableArray<string>> _rows = [];
    private (Session Session, string? AgentId, DateTimeOffset Now)? _entriesFor;
    private ImmutableArray<ConversationEntry> _entries = [];
    private ImmutableArray<ImmutableArray<string>> _entryLines = [];

    public ConversationView(IAppContext context)
    {
        _context = context;
        _sessionList = new SelectableList(
            "conversation.sessions", SessionRows, SessionSelection, SelectSession, _ => FocusEntries(),
            activateLabel: "Entries", wrap: true, emptyText: "No sessions yet");
        _entryList = new SelectableList(
            "conversation.entries", EntryLines, EntrySelection, SelectEntry, OpenEntry,
            activateLabel: "Open", endLabel: "Follow", activateOnClick: true);
        _sessionList.View.AutoFocus(true);

        Root = new HSplitter(Pane("Sessions", _sessionList.View), Pane("Entries", _entryList.View)).Ratio(ListShare);
        AddCommands(Root);
    }

    public Visual Root { get; }

    private static Group Pane(string title, Visual content) =>
        new Group().TopLeftText(new Markup(Look.Tag("bold", title))).Content(content).Stretch();

    /// <summary>Tab and Shift+Tab move the focus between the two lists, wherever it is on the page.</summary>
    private void AddCommands(Visual root)
    {
        root.AddCommand(new Command
        {
            Id = "conversation.switch",
            LabelMarkup = "Switch list",
            Gesture = new KeyGesture(TerminalKey.Tab),
            Execute = _ => SwitchList(),
        });
        root.AddCommand(new Command
        {
            Id = "conversation.switchback",
            LabelMarkup = "Switch list",
            Gesture = new KeyGesture(TerminalKey.Tab, TerminalModifiers.Shift),
            Presentation = CommandPresentation.None,
            Execute = _ => SwitchList(),
        });
    }

    private void SwitchList()
    {
        if (_entryList.View.HasFocus)
        {
            Root.App?.Focus(_sessionList.View);
        }
        else
        {
            FocusEntries();
        }
    }

    private void FocusEntries() => Root.App?.Focus(_entryList.View);

    /// <summary>The sessions in <see cref="ConversationText.OrderSessions"/> order, each followed by its child rows (41.1).</summary>
    private ImmutableArray<SessionItem> Items()
    {
        var snapshot = _context.Snapshot.Value;
        if (!ReferenceEquals(snapshot, _itemsFor))
        {
            var items = ImmutableArray.CreateBuilder<SessionItem>();
            foreach (var session in ConversationText.OrderSessions(snapshot))
            {
                items.Add(new SessionItem(session, null));
                items.AddRange(AgentTree.Rows([session]).Select(row => new SessionItem(session, row)));
            }
            _items = items.ToImmutable();
            _itemsFor = snapshot;
        }
        return _items;
    }

    private ImmutableArray<ImmutableArray<string>> SessionRows()
    {
        var items = Items();
        var now = _context.Now.Value;
        if (_rowsFor is not { } key || key.Items != items || key.Now != now)
        {
            _rows = [.. items.Select(item => ImmutableArray.Create(item.Child is { } row
                ? ConversationText.ChildRow(row, now)
                : ConversationText.SessionRow(item.Session, now)))];
            _rowsFor = (items, now);
        }
        return _rows;
    }

    /// <summary>
    /// The item of <see cref="IAppContext.SelectedSessionKey"/>: the session of its session part, else the first one, and
    /// there the child row of its agent part, else the session's own row (41.1); -1 without sessions.
    /// </summary>
    private int SelectedItemIndex()
    {
        var items = Items();
        if (items.IsEmpty)
        {
            return -1;
        }
        var (sessionKey, agentId) = AgentKey.Parse(_context.SelectedSessionKey.Value);
        var sessionIndex = -1;
        for (var i = 0; i < items.Length && sessionIndex < 0; i++)
        {
            if (items[i].Child is null && items[i].Session.Files.Key == sessionKey)
            {
                sessionIndex = i;
            }
        }
        if (sessionIndex < 0)
        {
            return 0;
        }
        for (var i = sessionIndex + 1; i < items.Length && items[i].Child is not null; i++)
        {
            if (items[i].AgentId == agentId)
            {
                return i;
            }
        }
        return sessionIndex;
    }

    private SessionItem? SelectedItem() => SelectedItemIndex() is var index and >= 0 ? Items()[index] : null;

    private ListSelection SessionSelection() => new(null, SelectedItemIndex());

    private void SelectSession(int index) => _context.SelectedSessionKey.Value = Items()[index].Key;

    private ImmutableArray<ConversationEntry> Entries() => EntriesAndLines().Entries;

    private ImmutableArray<ImmutableArray<string>> EntryLines() => EntriesAndLines().Lines;

    /// <summary>
    /// The entries of the selected session (41.2) or sub-agent (41.4) and the lines of each, cached per item and time.
    /// </summary>
    private (ImmutableArray<ConversationEntry> Entries, ImmutableArray<ImmutableArray<string>> Lines) EntriesAndLines()
    {
        if (SelectedItem() is not { } item)
        {
            return ([], []);
        }
        var now = _context.Now.Value;
        if (_entriesFor is not { } key || !ReferenceEquals(key.Session, item.Session) || key.AgentId != item.AgentId
            || key.Now != now)
        {
            _entries = item.AgentId is { } agentId
                ? ConversationText.SubAgentEntries(item.Session, agentId, now)
                : ConversationText.Entries(item.Session, now);
            _entryLines = [.. _entries.Select(entry => entry.Lines)];
            _entriesFor = (item.Session, item.AgentId, now);
        }
        return (_entries, _entryLines);
    }

    /// <summary>
    /// The selected entry: on a change of session or sub-agent the last entry while it runs (so that following starts)
    /// and the first one otherwise; the last entry while the user's last pick was the last entry and the session or
    /// sub-agent runs (8.7, 41.4); else the user's pick by position (8.8).
    /// </summary>
    private ListSelection EntrySelection()
    {
        var item = SelectedItem();
        var count = Entries().Length;
        if (item is null || count == 0)
        {
            return new ListSelection(null, -1);
        }
        var key = item.Key;
        var running = item.Running;
        var cursor = _cursor.Value;
        var index = cursor.SessionKey != key ? (running ? count - 1 : 0)
            : cursor.AtEnd && running ? count - 1
            : Math.Min(cursor.Index, count - 1);
        return new ListSelection(key, index);
    }

    private void SelectEntry(int index)
    {
        if (SelectedItem() is { } item)
        {
            _cursor.Value = new EntryCursor(item.Key, index, index == Entries().Length - 1);
        }
    }

    /// <summary>
    /// Opens the entry's pop-up; on the entry that started a sub-agent, selects that sub-agent instead and keeps the
    /// keys on the entries, which are then its own (41.3).
    /// </summary>
    private void OpenEntry(int index)
    {
        var entries = Entries();
        if (index >= entries.Length)
        {
            return;
        }
        var entry = entries[index];
        if (entry.SelectsAgentId is { } agentId)
        {
            if (SelectedItem() is { } item)
            {
                _context.SelectedSessionKey.Value = AgentKey.Of(item.Session, agentId);
                FocusEntries();
            }
        }
        else if (!entry.Popup.IsEmpty)
        {
            _context.ShowPopup(entry.PopupTitle, entry.Popup);
        }
    }

    /// <summary>An item of the session list: a session's own row, or one of its child rows (41.1).</summary>
    private sealed record SessionItem(Session Session, AgentRow? Child)
    {
        public string? AgentId => Child?.SubAgent.Id;

        /// <summary>The <see cref="IAppContext.SelectedSessionKey"/> that selects this item: <see cref="AgentKey.Of"/>.</summary>
        public string Key => AgentKey.Of(Session, AgentId);

        /// <summary>Whether the session runs, or for a child row whether <see cref="SubAgents.StateOf"/> the sub-agent is Running.</summary>
        public bool Running =>
            (Child is { } child ? SubAgents.StateOf(Session, child.SubAgent) : Session.State) == SessionState.Running;
    }
}

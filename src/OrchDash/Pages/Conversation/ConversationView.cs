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

    private RunSnapshot? _sessionsFor;
    private ImmutableArray<Session> _sessions = [];
    private (ImmutableArray<Session> Sessions, DateTimeOffset Now)? _rowsFor;
    private ImmutableArray<ImmutableArray<string>> _rows = [];
    private (Session Session, DateTimeOffset Now)? _entriesFor;
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

    private ImmutableArray<Session> Sessions()
    {
        var snapshot = _context.Snapshot.Value;
        if (!ReferenceEquals(snapshot, _sessionsFor))
        {
            _sessions = ConversationText.OrderSessions(snapshot);
            _sessionsFor = snapshot;
        }
        return _sessions;
    }

    private ImmutableArray<ImmutableArray<string>> SessionRows()
    {
        var sessions = Sessions();
        var now = _context.Now.Value;
        if (_rowsFor is not { } key || key.Sessions != sessions || key.Now != now)
        {
            _rows = [.. sessions.Select(session => ImmutableArray.Create(ConversationText.SessionRow(session, now)))];
            _rowsFor = (sessions, now);
        }
        return _rows;
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

    private ImmutableArray<ConversationEntry> Entries() => EntriesAndLines().Entries;

    private ImmutableArray<ImmutableArray<string>> EntryLines() => EntriesAndLines().Lines;

    /// <summary>The entries of the selected session and the lines of each, cached per session and time.</summary>
    private (ImmutableArray<ConversationEntry> Entries, ImmutableArray<ImmutableArray<string>> Lines) EntriesAndLines()
    {
        if (SelectedSession() is not { } session)
        {
            return ([], []);
        }
        var now = _context.Now.Value;
        if (_entriesFor is not { } key || !ReferenceEquals(key.Session, session) || key.Now != now)
        {
            _entries = ConversationText.Entries(session, now);
            _entryLines = [.. _entries.Select(entry => entry.Lines)];
            _entriesFor = (session, now);
        }
        return (_entries, _entryLines);
    }

    /// <summary>
    /// The selected entry: on a change of session the last entry while it runs (so that following starts) and the
    /// first one otherwise; the last entry while the user's last pick was the last entry and the session runs (8.7);
    /// else the user's pick by position (8.8).
    /// </summary>
    private ListSelection EntrySelection()
    {
        var session = SelectedSession();
        var count = Entries().Length;
        if (session is null || count == 0)
        {
            return new ListSelection(null, -1);
        }
        var key = session.Files.Key;
        var running = session.State == SessionState.Running;
        var cursor = _cursor.Value;
        var index = cursor.SessionKey != key ? (running ? count - 1 : 0)
            : cursor.AtEnd && running ? count - 1
            : Math.Min(cursor.Index, count - 1);
        return new ListSelection(key, index);
    }

    private void SelectEntry(int index)
    {
        if (SelectedSession() is { } session)
        {
            _cursor.Value = new EntryCursor(session.Files.Key, index, index == Entries().Length - 1);
        }
    }

    private void OpenEntry(int index)
    {
        var entries = Entries();
        if (index < entries.Length && !entries[index].Popup.IsEmpty)
        {
            _context.ShowPopup(entries[index].PopupTitle, entries[index].Popup);
        }
    }
}

using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.CommandLogs.Format;
using OrchDash.Pages.Conversation;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Pages.CommandLogs;

/// <summary>
/// The visuals and the selection state of one <see cref="CommandsPage"/>. Everything shown is a function of
/// <see cref="IAppContext.Snapshot"/> and the key of the log the user selected last; the rows are cached per snapshot.
/// </summary>
internal sealed class CommandsView
{
    private const double ListShare = 0.45;

    private readonly IAppContext _context;
    private readonly State<string?> _selectedKey = new(null);
    private readonly SelectableList _list;
    private readonly ScrollViewer _output;

    private RunSnapshot? _rowsFor;
    private ImmutableArray<ImmutableArray<string>> _rows = [];

    // Whether the last select was a click on the row that was already selected; null once that input is handled.
    private bool? _clickOnSelected;

    // 26.3: the log the output shows, whether its end is kept in view, the height of its text and the offset this
    // view scrolled to last (an offset above it means the user scrolled up).
    private string? _shownKey;
    private bool _following;
    private int _contentHeight;
    private int _lastSetOffset;

    public CommandsView(IAppContext context)
    {
        _context = context;
        _list = new SelectableList(
            "commands.logs", Rows, Selection, Select, Open,
            activateLabel: "Open", activateOnClick: true, emptyText: CommandsText.NoLogs);
        _list.View.AutoFocus(true);
        var text = new Markup(() => Look.Tag("", SelectedLog() is { } log ? CommandsText.OutputText(log) : "")) { Wrap = true };
        _output = new ScrollViewer(new ArrangeProbe(text, OnOutputArranged), focusable: true).HorizontalScrollEnabled(false);
        AddOutputCommands();

        var header = new Markup(() => SelectedLog() is { } log ? string.Join('\n', CommandsText.HeaderLines(log)) : "");
        Root = new HSplitter(
                Pane("Logs", _list.View),
                Pane("Output", new DockLayout().Top(header).Content(_output)))
            .Ratio(ListShare);
        AddCommands(Root);
    }

    public Visual Root { get; }

    private static Group Pane(string title, Visual content) =>
        new Group().TopLeftText(new Markup(Look.Tag("bold", title))).Content(content).Stretch();

    /// <summary>Tab and Shift+Tab move the focus between the list and the output, wherever it is on the page (26.5).</summary>
    private void AddCommands(Visual root)
    {
        root.AddCommand(new Command
        {
            Id = "commands.switch",
            LabelMarkup = "Switch list",
            Gesture = new KeyGesture(TerminalKey.Tab),
            Execute = _ => SwitchList(),
        });
        root.AddCommand(new Command
        {
            Id = "commands.switchback",
            LabelMarkup = "Switch list",
            Gesture = new KeyGesture(TerminalKey.Tab, TerminalModifiers.Shift),
            Presentation = CommandPresentation.None,
            Execute = _ => SwitchList(),
        });
    }

    private void SwitchList() => Root.App?.Focus(_output.HasFocus ? _list.View : _output);

    /// <summary>
    /// The scroll keys of the output (26.3): End scrolls to the end and follows the output again; Home, PageUp and Up
    /// stop following. The wheel scrolls natively; <see cref="OnOutputArranged"/> notices when it scrolled up.
    /// </summary>
    private void AddOutputCommands()
    {
        Add("end", "Follow", TerminalKey.End, () =>
        {
            _following = true;
            ScrollToEnd();
        }, visible: true);
        Add("home", "Top", TerminalKey.Home, () => ScrollUp(_output.VerticalOffset));
        Add("pageup", "Page up", TerminalKey.PageUp, () => ScrollUp(Math.Max(1, _output.ViewportHeight)));
        Add("up", "Up", TerminalKey.Up, () => ScrollUp(1));
        Add("pagedown", "Page down", TerminalKey.PageDown, () => ScrollTo(_output.VerticalOffset + Math.Max(1, _output.ViewportHeight)));
        Add("down", "Down", TerminalKey.Down, () => ScrollTo(_output.VerticalOffset + 1));

        void Add(string name, string label, TerminalKey key, Action execute, bool visible = false) =>
            _output.AddCommand(new Command
            {
                Id = $"commands.output.{name}",
                LabelMarkup = label,
                Gesture = new KeyGesture(key),
                Presentation = visible ? CommandPresentation.CommandBar : CommandPresentation.None,
                Execute = _ => execute(),
            });
    }

    private void ScrollUp(int rows)
    {
        _following = false;
        ScrollTo(_output.VerticalOffset - rows);
    }

    /// <summary>The largest offset: the one that shows the last row of the output at the bottom of the view.</summary>
    private int EndOffset() => Math.Max(0, _contentHeight - _output.ViewportHeight);

    private void ScrollTo(int offset)
    {
        var target = Math.Clamp(offset, 0, EndOffset());
        if (target != _output.VerticalOffset)
        {
            _output.VerticalOffset = target;
        }
    }

    private void ScrollToEnd()
    {
        _lastSetOffset = EndOffset();
        ScrollTo(_lastSetOffset);
    }

    /// <summary>
    /// After each layout of the output text: a newly shown log starts at its top, or at its end while it runs (26.3);
    /// an offset above the one this view set means the user scrolled up, which stops following; while following, an
    /// end that moved out of view is scrolled back in. Arrange runs in a tracking context, which may not write the
    /// offset it read, so the scrolls are posted to run after the frame.
    /// </summary>
    private void OnOutputArranged(int contentHeight)
    {
        _contentHeight = contentHeight;
        var log = SelectedLog();
        var offset = _output.VerticalOffset;
        if (log?.Key != _shownKey)
        {
            _shownKey = log?.Key;
            _following = log?.Outcome == CommandOutcome.Running;
            _lastSetOffset = 0;
            if (!_following && offset != 0)
            {
                _output.App?.Post(() => ScrollTo(0));
            }
        }
        else if (offset < _lastSetOffset && offset < EndOffset())
        {
            _following = false;
        }
        if (_following && offset < EndOffset())
        {
            _output.App?.Post(() =>
            {
                if (_following)
                {
                    ScrollToEnd();
                }
            });
        }
    }

    private ImmutableArray<CommandLog> Logs() => _context.Snapshot.Value.Commands;

    private ImmutableArray<ImmutableArray<string>> Rows()
    {
        var snapshot = _context.Snapshot.Value;
        if (!ReferenceEquals(snapshot, _rowsFor))
        {
            _rows = [.. snapshot.Commands.Select(log => ImmutableArray.Create(CommandsText.Row(log)))];
            _rowsFor = snapshot;
        }
        return _rows;
    }

    /// <summary>The log with the selected key, else the first one (26.5); -1 without logs.</summary>
    private int SelectedIndex()
    {
        var logs = Logs();
        if (logs.IsEmpty)
        {
            return -1;
        }
        var key = _selectedKey.Value;
        for (var i = 0; i < logs.Length; i++)
        {
            if (logs[i].Key == key)
            {
                return i;
            }
        }
        return 0;
    }

    private CommandLog? SelectedLog() => SelectedIndex() is var index and >= 0 ? Logs()[index] : null;

    private ListSelection Selection() => new(null, SelectedIndex());

    private void Select(int index)
    {
        _clickOnSelected = index == SelectedIndex();
        Root.App?.Post(() => _clickOnSelected = null);
        _selectedKey.Value = Logs()[index].Key;
    }

    /// <summary>
    /// 26.4: Enter on a row, or a click on the row that was already selected, opens the log's pop-up. A click calls
    /// <see cref="Select"/> and then this; Enter never passes through <see cref="Select"/>, so only a click on another
    /// row (false) keeps the pop-up closed.
    /// </summary>
    private void Open(int index)
    {
        var logs = Logs();
        if (_clickOnSelected is false || index >= logs.Length)
        {
            return;
        }
        _context.ShowPopup(CommandsText.PopupTitle(logs[index]), CommandsText.Popup(logs[index]));
    }

    /// <summary>
    /// The content of the output view: the text, reporting the height of each of its arranges. (ScrollViewer is
    /// sealed; Padder is the simplest container that is not.)
    /// </summary>
    private sealed class ArrangeProbe(Visual content, Action<int> arranged) : Padder(content)
    {
        protected override void ArrangeCore(in Rectangle finalRect)
        {
            base.ArrangeCore(finalRect);
            arranged(finalRect.Height);
        }
    }
}

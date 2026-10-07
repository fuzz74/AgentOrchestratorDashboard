using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Core.Replay;
using OrchDash.Core.Timeline;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Shell;

/// <summary>
/// The app frame: a header line, the time bar, one tab per page, the selected page and a command bar. It owns the
/// <see cref="IAppContext"/> of the pages, the replay cursor, the pop-up and the <c>Runs</c> dialog. Run it with
/// <c>Terminal.Run(shell.Root, shell.OnUpdate)</c>.
/// </summary>
public sealed class AppShell
{
    private static readonly TimeSpan ClockStep = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan JumpStep = TimeSpan.FromMinutes(1);

    private readonly Func<RunSnapshot> _latest;
    private readonly TimeProvider _time;
    private readonly IRunHost? _runs;
    private readonly ShellContext _context;
    private readonly IPage[] _pages;
    private readonly Visual[] _pageVisuals;
    private readonly Visual?[] _pageFocus;
    private readonly TabControl _tabs;
    private readonly Popup _popup;
    private readonly RunsDialog _runsDialog;

    // The live snapshot last taken from latest(), the replay cursor T (null while live) and the clock of the time bar.
    private readonly State<RunSnapshot> _live;
    private readonly State<DateTimeOffset?> _replayAt = new(null);
    private readonly State<DateTimeOffset> _clock;

    // What the run host had at the last update.
    private readonly State<string?> _loading = new(null);
    private readonly State<string?> _problem = new(null);
    private RunCatalog? _catalog;

    // The timeline of the live snapshot.
    private ImmutableArray<TimelineEvent> _events;
    private Visual? _focusBeforeModal;
    private bool _quitRequested;

    /// <summary>Builds every page once and the visual tree; runs on the UI thread.</summary>
    /// <param name="pages">The pages, in tab order.</param>
    /// <param name="latest">The latest live snapshot; called on every update.</param>
    /// <param name="time">The clock; the system clock when null.</param>
    /// <param name="runs">The run host behind the <c>Runs</c> dialog; without one there is no dialog.</param>
    public AppShell(IReadOnlyList<IPage> pages, Func<RunSnapshot> latest, TimeProvider? time = null, IRunHost? runs = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(latest);
        _latest = latest;
        _time = time ?? TimeProvider.System;
        _runs = runs;
        var snapshot = latest();
        var now = _time.GetLocalNow();
        _live = new State<RunSnapshot>(snapshot);
        _events = TimelineBuilder.Build(snapshot);
        _clock = new State<DateTimeOffset>(now);
        _context = new ShellContext(this, snapshot, now);
        _pages = [.. pages];
        _pageVisuals = [.. _pages.Select(page => page.Build(_context))];
        _pageFocus = new Visual?[_pages.Length];
        _tabs = new TabControl([.. _pages.Select((page, i) => new TabPage(new TextBlock(page.Title), _pageVisuals[i]))]);

        Root = new DockLayout()
            .Top(new VStack(BuildHeader(), BuildTimeBar()))
            .Content(_tabs)
            .Bottom(new CommandBar());
        _popup = new Popup(Root, RestoreFocus);
        _runsDialog = new RunsDialog(Root, path => _runs?.SwitchTo(path), RestoreFocus);
        AddCommands(Root);
    }

    /// <summary>The root of the visual tree, for <c>Terminal.Run</c>.</summary>
    public Visual Root { get; }

    /// <summary>
    /// The update callback of <c>Terminal.Run</c>: takes the latest snapshot when it is a new instance (32.6), moves
    /// the clock once per second, takes up what the run host has, keeps the keyboard focus on the selected page, and
    /// returns Stop once quit was requested.
    /// </summary>
    public TerminalLoopResult OnUpdate()
    {
        var latest = _latest();
        if (!ReferenceEquals(latest, _live.Value))
        {
            TakeLive(latest);
        }
        var now = _time.GetLocalNow();
        if ((now - _clock.Value).Duration() >= ClockStep)
        {
            _clock.Value = now;
        }
        // While replaying, Now stays T.
        if (_replayAt.Value is null && (now - _context.Now.Value).Duration() >= ClockStep)
        {
            _context.Now.Value = now;
        }
        PollRunHost();
        KeepFocusOnPage();
        return _quitRequested ? TerminalLoopResult.Stop : TerminalLoopResult.Continue;
    }

    internal void ShowPage(string pageId)
    {
        var index = Array.FindIndex(_pages, page => page.Id == pageId);
        if (index >= 0)
        {
            _tabs.SelectedIndex = index;
        }
    }

    internal void ShowPopup(string title, IReadOnlyList<PopupSection> sections)
    {
        if (!_popup.IsOpen)
        {
            _focusBeforeModal = Root.App?.FocusedElement;
        }
        _popup.Show(title, sections);
    }

    /// <summary>
    /// 32.7: replays at <paramref name="at"/>, not earlier than the first event; returns to live for null or a time at
    /// or after the span's end.
    /// </summary>
    internal void Replay(DateTimeOffset? at)
    {
        if (at is { } time && Span() is (var start, var end) && time < end)
        {
            SetCursor(time < start ? start : time);
        }
        else
        {
            SetCursor(null);
        }
    }

    /// <summary>
    /// Takes a new live snapshot and its timeline; after a switch to another run it returns to live and clears the
    /// selected session (32.10). Writes what the pages see (32.5).
    /// </summary>
    private void TakeLive(RunSnapshot latest)
    {
        var otherRun = !string.Equals(latest.RepoPath, _live.Value.RepoPath, StringComparison.Ordinal);
        _live.Value = latest;
        _events = TimelineBuilder.Build(latest);
        if (otherRun)
        {
            _context.SelectedSessionKey.Value = null;
            SetCursor(null);
        }
        PublishSnapshot();
    }

    /// <summary>Moves the replay cursor (null is live) and writes the snapshot and the time the pages see (32.5).</summary>
    private void SetCursor(DateTimeOffset? at)
    {
        if (at == _replayAt.Value)
        {
            return;
        }
        _replayAt.Value = at;
        PublishSnapshot();
        var now = at ?? _time.GetLocalNow();
        if (now != _context.Now.Value)
        {
            _context.Now.Value = now;
        }
    }

    /// <summary>Writes the live snapshot, or the snapshot at T while replaying, unless it is already the one shown.</summary>
    private void PublishSnapshot()
    {
        var shown = _replayAt.Value is { } at ? SnapshotReplay.At(_live.Value, at) : _live.Value;
        if (!ReferenceEquals(shown, _context.Snapshot.Value))
        {
            _context.Snapshot.Value = shown;
        }
    }

    /// <summary>The live timeline; it reads the live snapshot's state, so that a markup calling it follows new snapshots.</summary>
    private ImmutableArray<TimelineEvent> Events()
    {
        _ = _live.Value;
        return _events;
    }

    /// <summary>The span of the time bar; null while the live timeline is empty.</summary>
    private (DateTimeOffset Start, DateTimeOffset End)? Span() => TimeBarText.Span(Events(), _live.Value.Run, _clock.Value);

    /// <summary>32.2: Left moves T to the latest event time earlier than T (than the span's end while live).</summary>
    private void StepBack()
    {
        if (Span() is (_, var end) && TimelineBuilder.Before(_events, _replayAt.Value ?? end) is { } time)
        {
            SetCursor(time);
        }
    }

    /// <summary>32.2: Right moves T to the earliest event time later than T, or returns to live when there is none.</summary>
    private void StepForward()
    {
        if (_replayAt.Value is { } at)
        {
            SetCursor(TimelineBuilder.After(_events, at));
        }
    }

    /// <summary>32.3: Shift+Left moves T one minute back (from the span's end while live), not before the first event.</summary>
    private void JumpBack()
    {
        if (Span() is (var start, var end))
        {
            var time = (_replayAt.Value ?? end) - JumpStep;
            SetCursor(time < start ? start : time);
        }
    }

    /// <summary>32.3: Shift+Right moves T one minute on, or returns to live when that is not earlier than the span's end.</summary>
    private void JumpForward()
    {
        if (_replayAt.Value is { } at)
        {
            Replay(at + JumpStep);
        }
    }

    private (string Run, string Problems, string Quit) Header => HeaderText.Build(_context.Snapshot.Value, _context.Now.Value);

    /// <summary>
    /// The header line; the run text (which opens the <c>Runs</c> dialog), the problem count and <c>quit</c> are
    /// separate visuals so that they can be clicked.
    /// </summary>
    private Visual BuildHeader() => new HStack(
            new Markup(() => Header.Run).Stretch().PointerPressed(ShowRuns),
            new Markup(() => Header.Problems)
                .IsVisible(() => !_context.Snapshot.Value.Problems.IsEmpty)
                .PointerPressed(ShowProblems),
            new Markup(() => Header.Quit).PointerPressed(RequestQuit))
        .Spacing(2);

    /// <summary>The time bar (32.1): one line of <see cref="TimeBarText.Build"/>.</summary>
    private Visual BuildTimeBar() =>
        new Markup(() => TimeBarText.Build(Events(), _live.Value.Run, _clock.Value, _replayAt.Value, _loading.Value, _problem.Value))
            {
                Wrap = false,
            }
            .IsSelectable(false)
            .PointerPressed((_, e) => OnTimeBarPressed(e));

    /// <summary>32.4: a click on a cell moves T to the cell's time; on the last cell or on the state text it returns to live.</summary>
    private void OnTimeBarPressed(PointerEventArgs e)
    {
        if (e.Button != TerminalMouseButton.Left || Span() is not (var start, var end))
        {
            return;
        }
        e.Handled = true;
        var cell = e.LocalX - TimeBarText.BarColumn;
        if (cell == TimeBarText.CellCount - 1 || e.LocalX >= TimeBarText.StateColumn)
        {
            SetCursor(null);
        }
        else if (cell >= 0 && cell < TimeBarText.CellCount)
        {
            SetCursor(TimeBarText.CellTime(start, end, cell));
        }
    }

    /// <summary>
    /// The shell's keys, on the root so that they work wherever the focus is, and listed by the command bar. Commands
    /// do not fire behind a modal dialog, so these keys reach an open pop-up or the <c>Runs</c> dialog instead.
    /// </summary>
    private void AddCommands(Visual root)
    {
        for (var i = 0; i < Math.Min(_pages.Length, 9); i++)
        {
            var index = i;
            root.AddCommand(new Command
            {
                Id = $"shell.page{index + 1}",
                LabelMarkup = AnsiMarkup.Escape(_pages[index].Title),
                Gesture = new KeyGesture((char)('1' + index)),
                Execute = _ => _tabs.SelectedIndex = index,
            });
        }
        root.AddCommand(new Command
        {
            Id = "shell.step-back",
            LabelMarkup = "Step",
            Gesture = new KeyGesture(TerminalKey.Left),
            Execute = _ => StepBack(),
        });
        root.AddCommand(new Command
        {
            Id = "shell.step-forward",
            LabelMarkup = "Step forward",
            Gesture = new KeyGesture(TerminalKey.Right),
            Presentation = CommandPresentation.None,
            Execute = _ => StepForward(),
        });
        root.AddCommand(new Command
        {
            Id = "shell.minute-back",
            LabelMarkup = "Minute back",
            Gesture = new KeyGesture(TerminalKey.Left, TerminalModifiers.Shift),
            Presentation = CommandPresentation.None,
            Execute = _ => JumpBack(),
        });
        root.AddCommand(new Command
        {
            Id = "shell.minute-forward",
            LabelMarkup = "Minute forward",
            Gesture = new KeyGesture(TerminalKey.Right, TerminalModifiers.Shift),
            Presentation = CommandPresentation.None,
            Execute = _ => JumpForward(),
        });
        root.AddCommand(new Command
        {
            Id = "shell.live",
            LabelMarkup = "Live",
            Gesture = new KeyGesture(TerminalKey.Escape),
            IsVisible = _ => _replayAt.Value is not null,
            ConsumesGestureWhenUnavailable = false,
            Execute = _ => SetCursor(null),
        });
        if (_runs is not null)
        {
            root.AddCommand(new Command
            {
                Id = "shell.runs",
                LabelMarkup = "Runs",
                Gesture = new KeyGesture('r'),
                Execute = _ => ShowRuns(),
            });
        }
        root.AddCommand(new Command
        {
            Id = "shell.problems",
            LabelMarkup = "Problems",
            Gesture = new KeyGesture('p'),
            Execute = _ => ShowProblems(),
        });
        root.AddCommand(new Command
        {
            Id = "shell.quit",
            LabelMarkup = "Quit",
            Gesture = new KeyGesture('q'),
            Execute = _ => RequestQuit(),
        });
    }

    /// <summary>Takes up the run host's loading text, problem and catalog; a new catalog refreshes the open <c>Runs</c> dialog.</summary>
    private void PollRunHost()
    {
        if (_runs is null)
        {
            return;
        }
        var loading = _runs.Loading;
        if (loading != _loading.Value)
        {
            _loading.Value = loading;
        }
        var problem = _runs.Problem;
        if (problem != _problem.Value)
        {
            _problem.Value = problem;
        }
        var catalog = _runs.Runs;
        if (!ReferenceEquals(catalog, _catalog))
        {
            _catalog = catalog;
            _runsDialog.Update(catalog, _context.Snapshot.Value.RepoPath);
        }
    }

    /// <summary>32.8: asks the host for a new listing and opens the <c>Runs</c> dialog with the listing it has.</summary>
    private void ShowRuns()
    {
        if (_runs is null || _runsDialog.IsOpen || _popup.IsOpen)
        {
            return;
        }
        _runs.RefreshRuns();
        _catalog = _runs.Runs;
        _focusBeforeModal = Root.App?.FocusedElement;
        _runsDialog.Show(_catalog, _context.Snapshot.Value.RepoPath);
    }

    private void ShowProblems()
    {
        var problems = _context.Snapshot.Value.Problems;
        var text = problems.IsEmpty ? "No problems" : string.Join('\n', problems);
        ShowPopup("Problems", [new PopupSection("Problems", text)]);
    }

    private void RequestQuit() => _quitRequested = true;

    /// <summary>Gives the keyboard focus back to where it was before the pop-up or dialog opened, or else to the selected page.</summary>
    private void RestoreFocus()
    {
        var previous = _focusBeforeModal;
        _focusBeforeModal = null;
        var page = SelectedPageVisual;
        if (previous is not null && page is not null && IsWithin(previous, page))
        {
            Root.App?.Focus(previous);
        }
        else
        {
            KeepFocusOnPage();
        }
    }

    /// <summary>
    /// While no pop-up or dialog is open, keeps the keyboard focus inside the selected page. It remembers the focused
    /// element of each page. When the focus is outside the selected page (after a tab switch, or a click on a tab, which
    /// focuses the tab control) or on the page's root itself (a click on a part of the page that takes no focus moves
    /// the focus up to the root, a focusable splitter), it moves the focus back to that page's remembered element; for a
    /// page without one it clears the focus, so that the library focuses the first visible focusable element, preferring
    /// one with AutoFocus. A focus inside a window (a page's own pop-up) is left alone.
    /// </summary>
    private void KeepFocusOnPage()
    {
        var app = Root.App;
        var page = SelectedPageVisual;
        if (app is null || page is null || _popup.IsOpen || _runsDialog.IsOpen)
        {
            return;
        }
        var index = _tabs.SelectedIndex;
        var focused = app.FocusedElement;
        if (focused is not null && !IsWithin(focused, Root))
        {
            return;
        }
        if (focused is not null && focused != page && IsWithin(focused, page))
        {
            _pageFocus[index] = focused;
        }
        else if (_pageFocus[index] is { } remembered && IsWithin(remembered, page) && remembered.Focusable && remembered.IsVisible && remembered.IsEnabled)
        {
            app.Focus(remembered);
        }
        else if (focused is not null)
        {
            app.Focus(null);
        }
    }

    private Visual? SelectedPageVisual =>
        _tabs.SelectedIndex >= 0 && _tabs.SelectedIndex < _pageVisuals.Length ? _pageVisuals[_tabs.SelectedIndex] : null;

    private static bool IsWithin(Visual visual, Visual ancestor)
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

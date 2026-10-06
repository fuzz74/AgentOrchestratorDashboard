using OrchDash.Contracts;
using OrchDash.Core.Model;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Shell;

/// <summary>
/// The app frame: a header line, one tab per page, the selected page and a command bar. It owns the
/// <see cref="IAppContext"/> of the pages and the pop-up. Run it with <c>Terminal.Run(shell.Root, shell.OnUpdate)</c>.
/// </summary>
public sealed class AppShell
{
    private static readonly TimeSpan ClockStep = TimeSpan.FromSeconds(1);

    private readonly Func<RunSnapshot> _latest;
    private readonly TimeProvider _time;
    private readonly ShellContext _context;
    private readonly IPage[] _pages;
    private readonly Visual[] _pageVisuals;
    private readonly Visual?[] _pageFocus;
    private readonly TabControl _tabs;
    private readonly Popup _popup;
    private Visual? _focusBeforePopup;
    private bool _quitRequested;

    /// <summary>Builds every page once and the visual tree; runs on the UI thread.</summary>
    public AppShell(IReadOnlyList<IPage> pages, Func<RunSnapshot> latest, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(latest);
        _latest = latest;
        _time = time ?? TimeProvider.System;
        _context = new ShellContext(this, latest(), _time.GetLocalNow());
        _pages = [.. pages];
        _pageVisuals = [.. _pages.Select(page => page.Build(_context))];
        _pageFocus = new Visual?[_pages.Length];
        _tabs = new TabControl([.. _pages.Select((page, i) => new TabPage(new TextBlock(page.Title), _pageVisuals[i]))]);

        Root = new DockLayout()
            .Top(BuildHeader())
            .Content(_tabs)
            .Bottom(new CommandBar());
        _popup = new Popup(Root, RestoreFocus);
        AddCommands(Root);
    }

    /// <summary>The root of the visual tree, for <c>Terminal.Run</c>.</summary>
    public Visual Root { get; }

    /// <summary>
    /// The update callback of <c>Terminal.Run</c>: takes a new snapshot when its version changed, moves the clock
    /// once per second, keeps the keyboard focus on the selected page, and returns Stop once quit was requested.
    /// </summary>
    public TerminalLoopResult OnUpdate()
    {
        var latest = _latest();
        if (latest.Version != _context.Snapshot.Value.Version)
        {
            _context.Snapshot.Value = latest;
        }
        var now = _time.GetLocalNow();
        if ((now - _context.Now.Value).Duration() >= ClockStep)
        {
            _context.Now.Value = now;
        }
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
            _focusBeforePopup = Root.App?.FocusedElement;
        }
        _popup.Show(title, sections);
    }

    private (string Run, string Problems, string Quit) Header => HeaderText.Build(_context.Snapshot.Value, _context.Now.Value);

    /// <summary>The header line; the problem count and <c>quit</c> are separate visuals so that they can be clicked.</summary>
    private Visual BuildHeader() => new HStack(
            new Markup(() => Header.Run).Stretch(),
            new Markup(() => Header.Problems)
                .IsVisible(() => !_context.Snapshot.Value.Problems.IsEmpty)
                .PointerPressed(ShowProblems),
            new Markup(() => Header.Quit).PointerPressed(RequestQuit))
        .Spacing(2);

    /// <summary>The shell's keys, on the root so that they work wherever the focus is, and listed by the command bar.</summary>
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

    private void ShowProblems()
    {
        var problems = _context.Snapshot.Value.Problems;
        var text = problems.IsEmpty ? "No problems" : string.Join('\n', problems);
        ShowPopup("Problems", [new PopupSection("Problems", text)]);
    }

    private void RequestQuit() => _quitRequested = true;

    /// <summary>Gives the keyboard focus back to where it was before the pop-up opened, or else to the selected page.</summary>
    private void RestoreFocus()
    {
        var previous = _focusBeforePopup;
        _focusBeforePopup = null;
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
    /// While no pop-up is open, keeps the keyboard focus inside the selected page. It remembers the focused element of
    /// each page. When the focus is outside the selected page (after a tab switch, or a click on a tab, which focuses the
    /// tab control) or on the page's root itself (a click on a part of the page that takes no focus moves the focus up
    /// to the root, a focusable splitter), it moves the focus back to that page's remembered element; for a page
    /// without one it clears the focus, so that the library focuses the first visible focusable element, preferring one
    /// with AutoFocus. A focus inside a window (a page's own pop-up) is left alone.
    /// </summary>
    private void KeepFocusOnPage()
    {
        var app = Root.App;
        var page = SelectedPageVisual;
        if (app is null || page is null || _popup.IsOpen)
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

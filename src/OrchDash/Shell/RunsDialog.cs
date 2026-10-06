using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Templating;

namespace OrchDash.Shell;

/// <summary>
/// The modal <c>Runs</c> dialog (32.8, 32.9): a title, a <c>[X]</c> close button and a list of the rows of
/// <see cref="RunsDialogText.Rows"/>. Enter or a click on a run row closes it and switches to that run, unless it is the
/// run shown; Escape, <c>q</c> and <c>[X]</c> close it.
/// </summary>
/// <param name="screen">The visual that fills the screen; the dialog takes 90 % of its width and height.</param>
/// <param name="switchTo">Called with the <c>RepoPath</c> of the run the user picked, after the dialog closed.</param>
/// <param name="closed">Called after the dialog closed.</param>
internal sealed class RunsDialog(Visual screen, Action<string> switchTo, Action closed)
{
    private Dialog? _dialog;
    private OptionList<RunsRow>? _list;
    private ImmutableArray<RunsRow> _rows = [];

    public bool IsOpen => _dialog is not null;

    /// <summary>Shows the dialog with the shown run's row selected, else the first run row, and gives the list the focus.</summary>
    public void Show(RunCatalog? catalog, string shownRepoPath)
    {
        _dialog?.Close();
        _list = new OptionList<RunsRow>()
            .ItemTemplate(new DataTemplate<RunsRow>((value, in _) => new Markup(value.GetValue().Markup) { Wrap = false }, null))
            .ActivateOnClick(true)
            .ItemActivated((_, e) => Activate(e.Index))
            .KeyDown(OnKeyDown);
        _rows = [];
        Update(catalog, shownRepoPath);
        _dialog = new Dialog()
            .Title(new Markup(Look.Tag("bold", "Runs")))
            .TopRightText(new Markup(AnsiMarkup.Escape("[X]")).PointerPressed(Close))
            .IsModal(true)
            .IsDraggable(false)
            .IsResizable(false)
            .Width(() => screen.Bounds.Width * 9 / 10)
            .Height(() => screen.Bounds.Height * 9 / 10)
            .Content(_list);
        _dialog.Show();
        screen.App?.Focus(_list);
    }

    /// <summary>
    /// Shows the rows of the catalog. The selected run stays selected; without one (the first rows after
    /// <c>loading…</c>), the shown run's row is selected, else the first run row.
    /// </summary>
    public void Update(RunCatalog? catalog, string shownRepoPath)
    {
        if (_list is null)
        {
            return;
        }
        var selected = _list.SelectedIndex >= 0 && _list.SelectedIndex < _rows.Length ? _rows[_list.SelectedIndex].Entry : null;
        _rows = RunsDialogText.Rows(catalog, shownRepoPath);
        _list.Items.Clear();
        _list.Items.AddRange(_rows);
        var index = selected is null ? -1 : IndexOf(row => row.Entry?.RepoPath == selected.RepoPath);
        if (index < 0)
        {
            index = IndexOf(row => row.Shown);
        }
        if (index < 0)
        {
            index = IndexOf(row => row.Entry is not null);
        }
        _list.SelectedIndex = Math.Max(index, 0);
    }

    private int IndexOf(Func<RunsRow, bool> match)
    {
        for (var i = 0; i < _rows.Length; i++)
        {
            if (match(_rows[i]))
            {
                return i;
            }
        }
        return -1;
    }

    private void Activate(int index)
    {
        if (index < 0 || index >= _rows.Length || _rows[index] is not { Entry: { } entry } row)
        {
            return;
        }
        Close();
        if (!row.Shown)
        {
            switchTo(entry.RepoPath);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == TerminalKey.Escape || (e.Char == 'q' && e.Modifiers == TerminalModifiers.None))
        {
            Close();
            e.Handled = true;
        }
    }

    private void Close()
    {
        if (_dialog is null)
        {
            return;
        }
        _dialog.Close();
        _dialog = null;
        _list = null;
        _rows = [];
        closed();
    }
}

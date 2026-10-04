using OrchDash.Contracts;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Shell;

/// <summary>The shell's modal pop-up: a title, a <c>[X]</c> close button and one scrollable text area.</summary>
/// <param name="screen">The visual that fills the screen; the pop-up takes 90 % of its width and height.</param>
/// <param name="closed">Called after the user closed the pop-up.</param>
internal sealed class Popup(Visual screen, Action closed)
{
    private Dialog? _dialog;

    public bool IsOpen => _dialog is not null;

    /// <summary>Shows a pop-up, replacing the one that is open, and gives its text area the keyboard focus.</summary>
    public void Show(string title, IReadOnlyList<PopupSection> sections)
    {
        _dialog?.Close();
        var text = new ScrollViewer(new Markup(PopupText.Build(sections)) { Wrap = true }, focusable: true)
            .KeyDown(OnKeyDown);
        _dialog = new Dialog()
            .Title(new Markup(Look.Tag("bold", title)))
            .TopRightText(new Markup(AnsiMarkup.Escape("[X]")).PointerPressed(Close))
            .IsModal(true)
            .IsDraggable(false)
            .IsResizable(false)
            .Width(() => screen.Bounds.Width * 9 / 10)
            .Height(() => screen.Bounds.Height * 9 / 10)
            .Content(text);
        _dialog.Show();
        screen.App?.Focus(text);
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
        closed();
    }
}

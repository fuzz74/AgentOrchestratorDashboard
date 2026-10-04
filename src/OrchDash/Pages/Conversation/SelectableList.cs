using System.Collections.Immutable;
using OrchDash.Contracts;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Pages.Conversation;

/// <summary>
/// A scrollable list of items of one or more markup lines, each selected as one. The owner keeps the selection: the
/// list reads it through <c>selection</c> and reports moves through <c>select</c>, so it may also change from outside.
/// Up, Down, PageUp, PageDown, Home and End move the selection, Enter activates it, a click selects an item (and
/// activates it with <c>activateOnClick</c>) and the wheel scrolls. The view scrolls only as far as needed to show a
/// newly selected item, and otherwise keeps its position, also when the items change.
/// </summary>
/// <remarks>
/// <see cref="OptionList{T}"/> gives every item the height of the highest one, and its items cannot follow a state,
/// so the list is built here on a <see cref="ScrollViewer"/>.
/// </remarks>
internal sealed class SelectableList
{
    private const int MarkerWidth = 2;

    private readonly Func<ImmutableArray<ImmutableArray<string>>> _items;
    private readonly Func<ListSelection> _selection;
    private readonly Action<int> _select;
    private readonly Action<int> _activate;
    private readonly bool _activateOnClick;
    private readonly bool _wrap;
    private readonly string _emptyText;
    private Visual[] _itemVisuals = [];
    private ListSelection _shownSelection = new(null, -1);

    /// <param name="id">Prefix of the command ids.</param>
    /// <param name="items">The lines of each item; read inside reactive lambdas.</param>
    /// <param name="selection">The selected item; read inside reactive lambdas.</param>
    /// <param name="select">Called when the user moves the selection to an index.</param>
    /// <param name="activate">Called on Enter, and on a click when <paramref name="activateOnClick"/> is set.</param>
    /// <param name="activateLabel">Command bar label of Enter.</param>
    /// <param name="endLabel">Command bar label of End, or null to leave End out of the command bar.</param>
    /// <param name="activateOnClick">Whether a click activates the item as well as selecting it.</param>
    /// <param name="wrap">Whether long lines wrap; otherwise they are cut at the right edge.</param>
    /// <param name="emptyText">Shown while there is no item.</param>
    public SelectableList(
        string id,
        Func<ImmutableArray<ImmutableArray<string>>> items,
        Func<ListSelection> selection,
        Action<int> select,
        Action<int> activate,
        string activateLabel,
        string? endLabel = null,
        bool activateOnClick = false,
        bool wrap = false,
        string emptyText = "")
    {
        _items = items;
        _selection = selection;
        _select = select;
        _activate = activate;
        _activateOnClick = activateOnClick;
        _wrap = wrap;
        _emptyText = emptyText;
        // The content's lambdas run at once and read View, so the view exists before its content.
        View = new ScrollViewer(focusable: true).HorizontalScrollEnabled(false);
        View.Content = new ArrangeProbe(BuildContent, OnArranged);
        AddCommands(id, activateLabel, endLabel);
    }

    /// <summary>The focusable, scrollable visual of the list.</summary>
    public ScrollViewer View { get; }

    private Visual BuildContent()
    {
        var items = _items();
        if (items.IsEmpty)
        {
            _itemVisuals = [];
            return new Markup(Look.Tag("muted", _emptyText));
        }
        _itemVisuals = [.. items.Select(Item)];
        return new VStack(_itemVisuals);
    }

    private Visual Item(ImmutableArray<string> lines, int index) =>
        new HStack(
                new Markup(() => Marker(index)).MinWidth(MarkerWidth).MaxWidth(MarkerWidth),
                new Markup(string.Join('\n', lines)) { Wrap = _wrap }.Stretch())
            .PointerPressed((_, e) => OnItemPressed(index, e));

    /// <summary>An arrow before the first line of the selected item, in the accent colour while the list has the focus.</summary>
    private string Marker(int index)
    {
        if (_selection().Index != index)
        {
            return "";
        }
        return View.HasFocus ? Look.Tag("accent", "→") : Look.Tag("muted", "→");
    }

    /// <summary>
    /// After a layout, scrolls to a selection that changed from outside (a new snapshot or another page). Arrange runs
    /// in a tracking context, which may not write the offset it read, so the scroll is posted to run after the frame.
    /// </summary>
    private void OnArranged()
    {
        if (_selection() != _shownSelection)
        {
            View.App?.Post(ScrollToSelection);
        }
    }

    /// <summary>
    /// Once the selection differs from the one last scrolled to and its item is laid out, scrolls as little as needed
    /// to show the whole item (or its start when it is higher than the view).
    /// </summary>
    private void ScrollToSelection()
    {
        var selection = _selection();
        if (selection == _shownSelection || ItemRows(selection.Index) is not var (start, height))
        {
            return;
        }
        _shownSelection = selection;
        var top = View.VerticalOffset;
        var viewport = View.ViewportHeight;
        if (start < top || height > viewport)
        {
            top = start;
        }
        else if (start + height > top + viewport)
        {
            top = start + height - viewport;
        }
        if (top != View.VerticalOffset)
        {
            View.VerticalOffset = top;
        }
    }

    /// <summary>The first row and the height of an item within the content, once it has been laid out.</summary>
    private (int Start, int Height)? ItemRows(int index)
    {
        var visuals = _itemVisuals;
        if (index < 0 || index >= visuals.Length || visuals[index].Bounds.Height <= 0)
        {
            return null;
        }
        return (visuals[index].Bounds.Y - visuals[0].Bounds.Y, visuals[index].Bounds.Height);
    }

    private void OnItemPressed(int index, PointerEventArgs e)
    {
        if (e.Button != TerminalMouseButton.Left)
        {
            return;
        }
        e.Handled = true;
        View.App?.Focus(View);
        MoveTo(index);
        if (_activateOnClick)
        {
            _activate(index);
        }
    }

    private void MoveTo(int index)
    {
        var count = _items().Length;
        if (count == 0)
        {
            return;
        }
        _select(Math.Clamp(index, 0, count - 1));
        ScrollToSelection();
    }

    /// <summary>The index one page up or down: at least one item away, and no more items than fill one view.</summary>
    private int PageTarget(int direction)
    {
        var items = _items();
        var start = _selection().Index;
        var viewport = Math.Max(1, View.ViewportHeight);
        var index = start;
        var rows = 0;
        while (index + direction >= 0 && index + direction < items.Length)
        {
            rows += ItemRows(index + direction)?.Height ?? items[index + direction].Length;
            if (rows > viewport && index != start)
            {
                break;
            }
            index += direction;
        }
        return index;
    }

    private void Activate()
    {
        var index = _selection().Index;
        if (index >= 0)
        {
            _activate(index);
        }
    }

    private void AddCommands(string id, string activateLabel, string? endLabel)
    {
        Add("up", "Up", TerminalKey.Up, () => MoveTo(_selection().Index - 1));
        Add("down", "Down", TerminalKey.Down, () => MoveTo(_selection().Index + 1));
        Add("pageup", "Page up", TerminalKey.PageUp, () => MoveTo(PageTarget(-1)));
        Add("pagedown", "Page down", TerminalKey.PageDown, () => MoveTo(PageTarget(1)));
        Add("home", "First", TerminalKey.Home, () => MoveTo(0));
        Add("end", endLabel ?? "Last", TerminalKey.End, () => MoveTo(_items().Length - 1), visible: endLabel is not null);
        Add("open", activateLabel, TerminalKey.Enter, Activate, visible: true);

        void Add(string name, string label, TerminalKey key, Action execute, bool visible = false) =>
            View.AddCommand(new Command
            {
                Id = $"{id}.{name}",
                LabelMarkup = label,
                Gesture = new KeyGesture(key),
                Presentation = visible ? CommandPresentation.CommandBar : CommandPresentation.None,
                Execute = _ => execute(),
            });
    }

    /// <summary>
    /// The content of the view: a computed visual that reports each of its arranges, after which the bounds of the
    /// items are known. (ScrollViewer is sealed; Padder is the simplest container that is not.)
    /// </summary>
    private sealed class ArrangeProbe(Func<Visual> content, Action arranged) : Padder(content)
    {
        protected override void ArrangeCore(in Rectangle finalRect)
        {
            base.ArrangeCore(finalRect);
            arranged();
        }
    }
}

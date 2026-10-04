using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Templating;

namespace OrchDash.Pages.Overview;

/// <summary>
/// A list panel of the Overview page: an <see cref="OptionList{T}"/> whose items come from the snapshot. When the items
/// change it keeps the selected item by key; with follow-tail it starts on the last item and keeps the last item selected,
/// and so in view, while the last item is selected. Enter on the selected item, or a click on an item, calls <c>activate</c>.
/// </summary>
/// <remarks>The list scrolls with its selection: Up, Down, PageUp, PageDown, Home, End and the wheel move the selection.</remarks>
internal sealed class PanelList<T>
    where T : class
{
    private readonly Func<IReadOnlyList<T>> _source;
    private readonly Func<T, int, string> _key;
    private readonly Action<T> _activate;
    private readonly bool _followTail;
    private T[] _items = [];
    // The list's selection, kept here because reading OptionList.SelectedIndex in Sync and then writing it is not allowed.
    private int _selectedIndex;
    private string? _selectedKey;
    private bool _following;
    private (int Viewport, int Extent) _scrollSize;

    /// <param name="id">The id of the list, used for its command ids.</param>
    /// <param name="source">The items; read inside the list's dynamic update, so it may read <see cref="State{T}"/> values.</param>
    /// <param name="key">The key of the item at an index; the selection follows the key when the items change.</param>
    /// <param name="template">Builds the visual of one item.</param>
    /// <param name="activate">Called with the item that the user activated.</param>
    /// <param name="activateLabel">The command bar label of Enter.</param>
    /// <param name="followTail">Start on the last item and keep the last item selected while it is selected.</param>
    public PanelList(
        string id,
        Func<IReadOnlyList<T>> source,
        Func<T, int, string> key,
        Func<T, Visual> template,
        Action<T> activate,
        string activateLabel,
        bool followTail = false)
    {
        _source = source;
        _key = key;
        _activate = activate;
        _followTail = followTail;
        _following = followTail;
        List = new OptionList<T>()
            .ItemTemplate(new DataTemplate<T>((value, in _) => template(value.GetValue()), null))
            .ActivateOnClick(true)
            .SelectionChanged((_, e) => OnSelectionChanged(e.NewIndex))
            .ItemActivated((_, e) => Activate(e.Index));
        List.RegisterDynamicUpdate(_ => Sync());
        List.Scroll.Changed += KeepTailInView;
        // A command takes Enter before the list's own key handling; the command bar lists it while the list has the focus.
        List.AddCommand(new Command
        {
            Id = $"overview.{id}.activate",
            LabelMarkup = activateLabel,
            Gesture = new KeyGesture(TerminalKey.Enter),
            Execute = _ => Activate(_selectedIndex),
        });
        if (followTail)
        {
            List.AddCommand(new Command
            {
                Id = $"overview.{id}.follow",
                LabelMarkup = "Follow",
                Gesture = new KeyGesture(TerminalKey.End),
                Execute = _ => List.SelectedIndex = _items.Length - 1,
            });
        }
    }

    public OptionList<T> List { get; }

    /// <summary>A bordered panel with the title, the content (the list and what goes with it) and the selected position.</summary>
    public Group Panel(string title, Visual content) => new Group(title)
        .Content(content)
        .BottomRightText(new Markup(() =>
        {
            var count = List.Items.Count;
            return count == 0 ? "" : $"{Math.Clamp(List.SelectedIndex, 0, count - 1) + 1}/{count}";
        }).IsSelectable(false))
        .Stretch();

    /// <summary>Takes the current items when they changed, keeping the selected key, or the last item while following.</summary>
    private void Sync()
    {
        var items = _source();
        if (items.Count == _items.Length && Enumerable.Range(0, items.Count).All(i => ReferenceEquals(items[i], _items[i])))
        {
            return;
        }
        var following = _following;
        _items = [.. items];
        List.Items.Clear();
        List.Items.AddRange(_items);
        if (_items.Length == 0)
        {
            return;
        }

        var index = following ? _items.Length - 1 : IndexOfKey(_selectedKey);
        if (index < 0)
        {
            index = Math.Clamp(_selectedIndex, 0, _items.Length - 1);
        }
        List.SelectedIndex = index;
        _selectedIndex = index;
        _selectedKey = _key(_items[index], index);
        _following = following;
    }

    /// <summary>
    /// While following, scrolls to the end when the list's extent or viewport changed. Selecting the new last item in
    /// <see cref="Sync"/> does not do it: the list scrolls the selection into view before it knows the new extent.
    /// </summary>
    private void KeepTailInView()
    {
        var scroll = List.Scroll;
        var size = (scroll.ViewportHeight, scroll.ExtentHeight);
        if (size == _scrollSize)
        {
            return;
        }
        _scrollSize = size;
        if (_following)
        {
            scroll.SetOffset(scroll.OffsetX, Math.Max(0, scroll.ExtentHeight - scroll.ViewportHeight));
        }
    }

    private int IndexOfKey(string? key)
    {
        for (var i = 0; i < _items.Length; i++)
        {
            if (_key(_items[i], i) == key)
            {
                return i;
            }
        }
        return -1;
    }

    private void OnSelectionChanged(int index)
    {
        if (index >= 0 && index < _items.Length)
        {
            _selectedIndex = index;
            _selectedKey = _key(_items[index], index);
            _following = _followTail && index == _items.Length - 1;
        }
    }

    private void Activate(int index)
    {
        if (index >= 0 && index < _items.Length)
        {
            _activate(_items[index]);
        }
    }
}

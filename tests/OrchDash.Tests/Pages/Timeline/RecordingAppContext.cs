using OrchDash.Contracts;
using OrchDash.Core.Model;
using XenoAtom.Terminal.UI;

namespace OrchDash.Tests.Pages.Timeline;

/// <summary>
/// An <see cref="IAppContext"/> without a shell, for hosting a page's visual directly: it records the calls of
/// <see cref="Replay"/>, <see cref="ShowPage"/> and <see cref="ShowPopup"/>. The UI thread records; tests read the
/// copies after the harness has rendered.
/// </summary>
internal sealed class RecordingAppContext(RunSnapshot snapshot) : IAppContext
{
    private readonly object _gate = new();
    private readonly List<DateTimeOffset?> _replays = [];
    private readonly List<string> _pages = [];
    private readonly List<string> _popups = [];

    public State<RunSnapshot> Snapshot { get; } = new(snapshot);

    public State<DateTimeOffset> Now { get; } = new(snapshot.ReadAt);

    public State<string?> SelectedSessionKey { get; } = new(null);

    public IReadOnlyList<DateTimeOffset?> Replays => Copy(_replays);

    public IReadOnlyList<string> Pages => Copy(_pages);

    /// <summary>The titles of the pop-ups shown.</summary>
    public IReadOnlyList<string> Popups => Copy(_popups);

    public void ShowPage(string pageId) => Add(_pages, pageId);

    public void ShowPopup(string title, IReadOnlyList<PopupSection> sections) => Add(_popups, title);

    public void Replay(DateTimeOffset? at) => Add(_replays, at);

    private void Add<T>(List<T> list, T item)
    {
        lock (_gate)
        {
            list.Add(item);
        }
    }

    private T[] Copy<T>(List<T> list)
    {
        lock (_gate)
        {
            return [.. list];
        }
    }
}

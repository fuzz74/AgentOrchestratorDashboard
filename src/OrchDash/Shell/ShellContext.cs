using OrchDash.Contracts;
using OrchDash.Core.Model;
using XenoAtom.Terminal.UI;

namespace OrchDash.Shell;

/// <summary>The <see cref="IAppContext"/> that <see cref="AppShell"/> gives its pages.</summary>
internal sealed class ShellContext(AppShell shell, RunSnapshot snapshot, DateTimeOffset now) : IAppContext
{
    public State<RunSnapshot> Snapshot { get; } = new(snapshot);

    public State<DateTimeOffset> Now { get; } = new(now);

    public State<string?> SelectedSessionKey { get; } = new(null);

    public void ShowPage(string pageId) => shell.ShowPage(pageId);

    public void ShowPopup(string title, IReadOnlyList<PopupSection> sections) => shell.ShowPopup(title, sections);

    public void Replay(DateTimeOffset? at) { }
}

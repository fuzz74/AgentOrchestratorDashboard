using OrchDash.Core.Model;
using XenoAtom.Terminal.UI;

namespace OrchDash.Contracts;

public interface IAppContext
{
    State<RunSnapshot> Snapshot { get; }          // written only by the shell, on the UI thread
    State<DateTimeOffset> Now { get; }            // written by the shell once per second
    State<string?> SelectedSessionKey { get; }    // a SessionFiles.Key; shared by the pages
    void ShowPage(string pageId);
    void ShowPopup(string title, IReadOnlyList<PopupSection> sections);
    void Replay(DateTimeOffset? at);   // set the replay time; null returns to live (32.7)
}

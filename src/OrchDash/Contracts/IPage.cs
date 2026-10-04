using XenoAtom.Terminal.UI;

namespace OrchDash.Contracts;

public interface IPage
{
    string Id { get; }        // "overview", "conversation"
    string Title { get; }     // tab text: "Overview", "Conversation"
    Visual Build(IAppContext context);   // called once, on the UI thread, before the first frame
}

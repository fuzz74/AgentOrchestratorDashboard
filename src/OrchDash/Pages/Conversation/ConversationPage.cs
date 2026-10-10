using OrchDash.Contracts;
using XenoAtom.Terminal.UI;

namespace OrchDash.Pages.Conversation;

/// <summary>
/// The Conversation page (8.1-8.9, 41.1-41.4): the session list with each session's sub-agents as child rows on the left,
/// and the entries of the selected session or sub-agent on the right.
/// </summary>
public sealed class ConversationPage : IPage
{
    public string Id => "conversation";

    public string Title => "Conversation";

    public Visual Build(IAppContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ConversationView(context).Root;
    }
}

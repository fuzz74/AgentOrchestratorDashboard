using OrchDash.Contracts;
using XenoAtom.Terminal.UI;

namespace OrchDash.Pages.Conversation;

/// <summary>
/// The Conversation page (8.1-8.9): the session list on the left and the entries of the selected session on the right.
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

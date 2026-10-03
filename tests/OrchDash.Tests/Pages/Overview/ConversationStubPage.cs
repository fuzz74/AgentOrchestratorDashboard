using OrchDash.Contracts;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace OrchDash.Tests.Pages.Overview;

/// <summary>A stand-in for the Conversation page that shows <c>selected session &lt;key&gt;</c>.</summary>
internal sealed class ConversationStubPage : IPage
{
    public const string Prefix = "selected session ";

    public string Id => "conversation";

    public string Title => "Conversation";

    public Visual Build(IAppContext context) =>
        new ScrollViewer(new TextBlock(() => Prefix + (context.SelectedSessionKey.Value ?? "none")), focusable: true);
}

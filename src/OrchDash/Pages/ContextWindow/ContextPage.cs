using OrchDash.Contracts;
using XenoAtom.Terminal.UI;

namespace OrchDash.Pages.ContextWindow;

/// <summary>
/// The Context page (15.1-15.14): the session list on the left; on the right the header, the context per call, the
/// call list, the make-up of the selected call and its parts.
/// </summary>
public sealed class ContextPage : IPage
{
    public string Id => "context";

    public string Title => "Context";

    public Visual Build(IAppContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ContextView(context).Root;
    }
}

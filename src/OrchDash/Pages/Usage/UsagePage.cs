using OrchDash.Contracts;
using XenoAtom.Terminal.UI;

namespace OrchDash.Pages.Usage;

/// <summary>
/// The Usage page (16.1-16.10): the run panel with the rate limit and versions lines on top, the group table with the
/// tokens per group beside it, and the session table of the selected group at the bottom.
/// </summary>
public sealed class UsagePage : IPage
{
    public string Id => "usage";

    public string Title => "Usage";

    public Visual Build(IAppContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new UsageView(context).Root;
    }
}

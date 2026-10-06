using OrchDash.Contracts;
using XenoAtom.Terminal.UI;

namespace OrchDash.Pages.Graph;

/// <summary>
/// The Graph page (24.1-24.7): the task graph by waves with the selected task's deps and dependents, and a detail panel
/// for the selected task below it.
/// </summary>
public sealed class GraphPage : IPage
{
    public string Id => "graph";

    public string Title => "Graph";

    public Visual Build(IAppContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new GraphView(context).Root;
    }
}

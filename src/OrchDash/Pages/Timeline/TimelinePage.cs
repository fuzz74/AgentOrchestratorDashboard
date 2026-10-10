using OrchDash.Contracts;
using XenoAtom.Terminal.UI;

namespace OrchDash.Pages.Timeline;

/// <summary>
/// The Timeline page (33.1-33.7, 44.1-44.4): one merged timeline of the orchestrator's log and every agent's and
/// sub-agent's prompt, calls, tool calls, texts and result, one row per event, filterable by kind, task and sub-agents,
/// with pop-ups and a jump into replay.
/// </summary>
public sealed class TimelinePage : IPage
{
    public string Id => "timeline";

    public string Title => "Timeline";

    public Visual Build(IAppContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TimelineView(context).Root;
    }
}

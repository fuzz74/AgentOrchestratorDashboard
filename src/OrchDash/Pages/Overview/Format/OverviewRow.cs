using OrchDash.Contracts;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Overview.Format;

// One row of the task table (39.1): the task's own row when Child is null, else a child row of the task's sessions.
public sealed record OverviewRow(TaskView Task, AgentRow? Child)
{
    // The key that keeps the selection when the rows change: the task id, or the sub-agent's AgentKey.
    public string Key => Child is { } child ? AgentKey.Of(child.Session, child.SubAgent.Id) : Task.Id;
}

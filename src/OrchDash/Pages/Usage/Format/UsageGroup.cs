using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Usage.Format;

// A row of the group table (16.4): a task id, "bootstrap" or "planner", its sessions in snapshot order and their sums.
public sealed record UsageGroup(string Name, ImmutableArray<Session> Sessions, UsageFigures Figures)
{
    // 43.4: the sub-agents of its sessions, nested ones included.
    public int SubAgentCount { get; init; }

    // 43.4: the tokens of those sub-agents' calls, by the rule of UsageFigures.Tokens.
    public long SubAgentTokens { get; init; }
}

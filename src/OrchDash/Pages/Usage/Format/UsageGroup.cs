using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Usage.Format;

// A row of the group table (16.4): a task id, "bootstrap" or "planner", its sessions in snapshot order and their sums.
public sealed record UsageGroup(string Name, ImmutableArray<Session> Sessions, UsageFigures Figures);

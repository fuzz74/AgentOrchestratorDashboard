using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record RunFolderData(RunInfo Run, PlanInfo? Plan, ImmutableArray<TaskView> Tasks,
    ImmutableArray<ProgressEntry> Progress, ImmutableArray<SessionFiles> Sessions, ImmutableArray<string> Problems);

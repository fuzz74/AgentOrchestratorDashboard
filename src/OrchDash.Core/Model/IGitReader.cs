using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public interface IGitReader          // never throws
{
    GitInfo Read(string repoPath, PlanInfo? plan, ImmutableArray<TaskView> tasks, DateTimeOffset now);
}

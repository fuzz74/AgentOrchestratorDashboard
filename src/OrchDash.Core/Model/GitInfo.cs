using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record GitInfo(DateTimeOffset? ReadAt, string? RepoRoot,
    string? IntegrationBranch, string? IntegrationTip, string? BaseBranch, string? BaseTip,
    ImmutableArray<GitWorktree> Worktrees, ImmutableArray<GitBranch> Branches,
    ImmutableArray<GitTask> Tasks,          // one per plan task, in the tasks' order
    string? Problem)                        // lines joined with '\n'
{
    // nulls and empty arrays; always the same instance
    public static GitInfo Empty { get; } = new(
        null, null, null, null, null, null,
        ImmutableArray<GitWorktree>.Empty, ImmutableArray<GitBranch>.Empty, ImmutableArray<GitTask>.Empty,
        null);
}

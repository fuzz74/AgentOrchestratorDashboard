namespace OrchDash.Core.Git;

/// <summary>
/// What a task's diffs depend on (20.7): its tip, its <c>MergedSha</c> and the <c>status --porcelain</c> output of its
/// worktree (null when not run), plus the repo root they were read in. Strings compare ordinally.
/// </summary>
internal readonly record struct DiffKey(string RepoRoot, string? Tip, string? MergedSha, string? Status);

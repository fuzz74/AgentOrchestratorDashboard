using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record GitTask(string TaskId, string? Branch, string? Tip, string? WorktreePath, bool WorktreeExists,
    ImmutableArray<string> UncommittedFiles,          // status --porcelain lines as printed
    DiffStat? Uncommitted, DiffStat? Committed,       // null when unknown
    ImmutableArray<GitCommit> Commits,                // Attempt and Sync commits, oldest first
    GitCommit? MergeCommit, ImmutableArray<string> ArchiveBranches,
    string? CommittedDiff, string? UncommittedDiff);  // null when unknown, "" when empty

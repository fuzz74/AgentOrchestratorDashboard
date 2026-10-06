using OrchDash.Core.Model;

namespace OrchDash.Core.Git;

/// <summary>A task's diff values of a completed read and the key they were read for (20.7).</summary>
internal sealed record TaskDiffs(DiffKey Key, DiffStat? Uncommitted, DiffStat? Committed,
    string? UncommittedDiff, string? CommittedDiff);

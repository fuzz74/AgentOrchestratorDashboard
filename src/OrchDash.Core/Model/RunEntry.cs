namespace OrchDash.Core.Model;

// One run of the run catalog (31.3).
public sealed record RunEntry(
    string RepoPath,                // the folder above .orchestrator: <RepoRoot>, or <RepoRoot>.runs\<Stamp>
    string? Stamp,                  // null for the repo's own run
    string? Spec,                   // the file name of tasks.json "spec"; null without
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
    int TaskCount, int Done, int Failed,
    Provider Provider, string? Model,
    string? Problem);               // "<file name>: <reason>" lines joined with '\n'; null when none

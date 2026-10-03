using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record TaskView(
    string Id, string Title, string Prompt, ImmutableArray<string> Deps, ImmutableArray<string> Owns,
    string? Acceptance, string? Model, int Wave, int Dependents,
    TaskState Status, string Mode, int Attempts, int SyncRuns, int SpecRejections, double CostUsd,
    string? SessionId, string? Summary, string? Notes, string? Error, string? Feedback,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? MergedSha, string Detail);

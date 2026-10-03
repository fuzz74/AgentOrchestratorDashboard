using OrchDash.Core.Model;

namespace OrchDash.Core.RunFolder;

/// <summary>One task of <c>state.json</c>; <see cref="Default"/> stands for a task without an entry (spec 2.2).</summary>
internal sealed record TaskStateEntry(
    TaskState Status, string Mode, int Attempts, int SyncRuns, int SpecRejections, double CostUsd,
    string? SessionId, string? Summary, string? Notes, string? Error, string? Feedback,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? MergedSha)
{
    public const string DefaultMode = "fresh";

    public static TaskStateEntry Default { get; } =
        new(TaskState.Pending, DefaultMode, 0, 0, 0, 0, null, null, null, null, null, null, null, null);
}

using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record RunSnapshot(
    long Version, DateTimeOffset ReadAt, string RepoPath, RunInfo Run, PlanInfo? Plan,
    ImmutableArray<TaskView> Tasks,          // by Wave, then Id (ordinal)
    ImmutableArray<Session> Sessions,        // by StartedAt (null last), then Files.Key
    ImmutableArray<ProgressEntry> Progress,  // file order
    ImmutableArray<string> Problems)
{
    public GitInfo Git { get; init; } = GitInfo.Empty;
    public ProcessInfo Processes { get; init; } = ProcessInfo.Empty;
    public ImmutableArray<CommandLog> Commands { get; init; } = [];   // newest first (22.1)

    // Version 0, phase NotStarted, empty arrays
    public static RunSnapshot Empty(string repoPath) => new(
        0, default, repoPath,
        new RunInfo(RunPhase.NotStarted, null, null, null, Provider.Unknown, null, false),
        null,
        ImmutableArray<TaskView>.Empty,
        ImmutableArray<Session>.Empty,
        ImmutableArray<ProgressEntry>.Empty,
        ImmutableArray<string>.Empty);
}

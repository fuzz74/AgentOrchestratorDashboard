using System.Collections.Immutable;
using OrchDash.Core.CommandLogs;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;

namespace OrchDash.Core.Replay;

/// <summary>
/// Rebuilds a snapshot as it was at a given time (spec 30.1-30.6): progress and sessions cut at that time, the run
/// info and the task states rebuilt from the cut progress. Pure; never throws; a default array counts as empty.
/// </summary>
public static class SnapshotReplay
{
    private const string GracefulStopPrefix = "Graceful stop requested";

    /// <summary>The snapshot at <paramref name="at"/>; parts that nothing is cut from are the live instances (30.6).</summary>
    public static RunSnapshot At(RunSnapshot live, DateTimeOffset at)
    {
        var progress = CutProgress(live.Progress, at);
        var run = RunInfoRules.Derive(progress, StopRequested(progress), lockHeld: true);
        var sessions = SessionCut.Cut(live.Sessions, at, run.Phase);
        var tasks = TaskReplay.Replay(live.Tasks, progress, sessions, run.MaxParallel);
        var commands = CutCommands(live.Commands, at, progress, live.Plan, tasks, run.Phase);

        return live with
        {
            ReadAt = at,
            Run = run,
            Tasks = tasks,
            Sessions = sessions,
            Progress = progress,
            Processes = ProcessInfo.Empty,
            Commands = commands,
        };
    }

    // 30.2: the entries up to the time, in file order; the live array while none is cut.
    private static ImmutableArray<ProgressEntry> CutProgress(ImmutableArray<ProgressEntry> progress, DateTimeOffset at)
    {
        if (progress.IsDefault)
            return [];
        return progress.All(entry => entry.Time <= at) ? progress : [.. progress.Where(entry => entry.Time <= at)];
    }

    // 30.2: the cut progress holds the orchestrator's own stop entry.
    private static bool StopRequested(ImmutableArray<ProgressEntry> progress) =>
        progress.Any(entry => entry.Source is null && entry.Message.StartsWith(GracefulStopPrefix, StringComparison.Ordinal));

    // 30.5: the logs written up to the time, resolved again; the live array while none is cut.
    private static ImmutableArray<CommandLog> CutCommands(ImmutableArray<CommandLog> logs, DateTimeOffset at,
        ImmutableArray<ProgressEntry> progress, PlanInfo? plan, ImmutableArray<TaskView> tasks, RunPhase phase)
    {
        if (logs.IsDefault)
            return [];
        if (logs.All(log => log.WrittenAt <= at))
            return logs;
        return CommandRules.Resolve([.. logs.Where(log => log.WrittenAt <= at)], progress, plan, tasks, phase);
    }
}

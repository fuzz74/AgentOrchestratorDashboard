using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;

namespace OrchDash.Core.Replay;

/// <summary>The tasks at a replay time by the task replay table (spec 30.4).</summary>
internal static class TaskReplay
{
    private const string Pass = "pass";

    /// <summary>
    /// The tasks in snapshot order, rebuilt from the cut progress and the cut sessions; a task whose every field equals
    /// is the live instance, and the live array is returned while every task is.
    /// </summary>
    public static ImmutableArray<TaskView> Replay(ImmutableArray<TaskView> tasks, ImmutableArray<ProgressEntry> progress,
        ImmutableArray<Session> sessions, int? maxParallel)
    {
        if (tasks.IsDefault)
            return [];

        var entriesByTask = progress.Where(entry => entry.Source is not null).ToLookup(entry => entry.Source!, StringComparer.Ordinal);
        var sessionsByTask = sessions.Where(session => session.Files.TaskId is not null)
            .ToLookup(session => session.Files.TaskId!, StringComparer.Ordinal);
        var facts = tasks.Select(task => new TaskProgress(entriesByTask[task.Id])).ToArray();
        var deps = DirectDeps(tasks);

        var statuses = facts.Select(fact => fact.Status).ToArray();
        for (var i = 0; i < statuses.Length; i++)
        {
            if (statuses[i] == TaskState.Pending && AnyAncestor(i, deps, dep => statuses[dep] == TaskState.Failed))
                statuses[i] = TaskState.Blocked;
        }
        var running = Enumerable.Range(0, tasks.Length).Where(i => statuses[i] == TaskState.Running).ToArray();

        var replayed = ImmutableArray.CreateBuilder<TaskView>(tasks.Length);
        for (var i = 0; i < tasks.Length; i++)
        {
            var task = tasks[i];
            var fact = facts[i];
            var taskSessions = sessionsByTask[task.Id].ToList();
            var cost = taskSessions.Sum(session => session.Content.Result?.CostUsd ?? 0.0);
            var worker = Latest(taskSessions.Where(session => session.Content.Result?.Worker is not null))?.Content.Result!.Worker;
            var reviews = taskSessions.Where(session => session.Content.Result?.Review is not null).ToList();
            var feedback = Latest(reviews.Where(session => !Passed(session.Content.Result!.Review!)));
            var error = fact.Error;

            var detail = statuses[i] switch
            {
                TaskState.Done => TaskDetails.Done(cost, fact.Attempts),
                TaskState.Failed => TaskDetails.Failed(error),
                TaskState.Blocked => TaskDetails.Blocked,
                TaskState.Running => fact.RunningStep(),
                _ => PendingDetail(i),
            };

            var view = task with
            {
                Status = statuses[i],
                Mode = fact.Mode,
                Attempts = fact.Attempts,
                SyncRuns = fact.SyncRuns,
                SpecRejections = reviews.Count(session => session.Content.Result!.Review!.SpecVerdict != Pass),
                CostUsd = cost,
                SessionId = Latest(taskSessions.Where(session => session.Files.Role == AgentRole.Worker))?.Content.SessionId,
                Summary = worker?.Summary,
                Notes = worker?.NotesForDependents,
                Error = error,
                Feedback = feedback?.Content.Result!.Review!.Summary,
                StartedAt = fact.StartedAt,
                FinishedAt = fact.FinishedAt,
                MergedSha = statuses[i] == TaskState.Done ? task.MergedSha : null,
                Detail = detail,
            };
            replayed.Add(view == task ? task : view);
        }

        return replayed.Zip(tasks).All(pair => ReferenceEquals(pair.First, pair.Second)) ? tasks : replayed.MoveToImmutable();

        // As RunFolderReader's PendingDetail, with the statuses at the replay time.
        string PendingDetail(int task)
        {
            var waitingFor = deps[task].Where(dep => statuses[dep] != TaskState.Done).ToArray();
            if (waitingFor.Length > 0)
                return TaskDetails.WaitingForDeps(waitingFor.Select(dep => tasks[dep].Id));

            var overlap = running.Where(other => TaskDetails.OwnsOverlap(tasks[task].Owns, tasks[other].Owns)).ToArray();
            if (overlap.Length > 0)
                return TaskDetails.WaitingForOwns(tasks[overlap[0]].Id);

            return maxParallel is { } max && running.Length >= max ? TaskDetails.FreeSlot : TaskDetails.Ready;
        }
    }

    // As TaskGraph: the positions of each task's direct deps in deps order; an id that is not a task is left out,
    // and an id that occurs twice refers to its first task.
    private static int[][] DirectDeps(ImmutableArray<TaskView> tasks)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < tasks.Length; i++)
            index.TryAdd(tasks[i].Id, i);

        return [.. tasks.Select(task => (task.Deps.IsDefault ? [] : task.Deps)
            .Select(dep => index.TryGetValue(dep, out var d) ? d : -1)
            .Where(d => d >= 0)
            .Distinct()
            .ToArray())];
    }

    // True when a direct or indirect dep satisfies the predicate; a cycle ends the walk.
    private static bool AnyAncestor(int start, int[][] deps, Func<int, bool> predicate)
    {
        var seen = new HashSet<int>();
        var queue = new Queue<int>(deps[start]);
        while (queue.TryDequeue(out var next))
        {
            if (next == start || !seen.Add(next))
                continue;
            if (predicate(next))
                return true;
            foreach (var dep in deps[next])
                queue.Enqueue(dep);
        }
        return false;
    }

    private static bool Passed(ReviewVerdict review) => review.SpecVerdict == Pass && review.QualityVerdict == Pass;

    // The session with the latest start; of equal starts the last in snapshot order.
    private static Session? Latest(IEnumerable<Session> sessions)
    {
        Session? latest = null;
        foreach (var session in sessions)
        {
            if (latest is null || session.StartedAt >= latest.StartedAt)
                latest = session;
        }
        return latest;
    }
}

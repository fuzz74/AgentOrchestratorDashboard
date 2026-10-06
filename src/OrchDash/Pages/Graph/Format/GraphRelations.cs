using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Graph.Format;

/// <summary>
/// The deps and dependents of a task among the tasks of a snapshot. Only ids that are tasks count, a task is never its
/// own dep or dependent, and cycles are safe.
/// </summary>
public static class GraphRelations
{
    /// <summary>The direct deps of the task, in its deps order.</summary>
    public static ImmutableArray<string> Deps(ImmutableArray<TaskView> tasks, string id)
    {
        var task = tasks.FirstOrDefault(t => t.Id == id);
        if (task is null)
            return [];
        return [.. task.Deps.Where(dep => dep != id && tasks.Any(t => t.Id == dep)).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The tasks that depend directly on the task, in snapshot order.</summary>
    public static ImmutableArray<string> Dependents(ImmutableArray<TaskView> tasks, string id) =>
        [.. tasks.Where(t => t.Id != id && t.Deps.Contains(id, StringComparer.Ordinal)).Select(t => t.Id)];

    /// <summary>The direct and indirect deps of the task, in snapshot order.</summary>
    public static ImmutableArray<string> AllDeps(ImmutableArray<TaskView> tasks, string id) =>
        Closure(tasks, id, next => Deps(tasks, next));

    /// <summary>The direct and indirect dependents of the task, in snapshot order.</summary>
    public static ImmutableArray<string> AllDependents(ImmutableArray<TaskView> tasks, string id) =>
        Closure(tasks, id, next => Dependents(tasks, next));

    private static ImmutableArray<string> Closure(ImmutableArray<TaskView> tasks, string id,
        Func<string, ImmutableArray<string>> step)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([id]);
        while (queue.TryDequeue(out var next))
        {
            foreach (var related in step(next))
            {
                if (related != id && found.Add(related))
                    queue.Enqueue(related);
            }
        }
        return [.. tasks.Select(t => t.Id).Where(found.Contains).Distinct(StringComparer.Ordinal)];
    }
}

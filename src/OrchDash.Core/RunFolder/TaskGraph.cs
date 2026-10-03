namespace OrchDash.Core.RunFolder;

/// <summary>
/// The dependency graph of the tasks, by position in the task list (spec 2.1, 2.3). A dep id that is not a
/// task is left out; when an id occurs twice, deps refer to its first task. Cycles neither hang nor throw:
/// a dep that closes a cycle does not count for the wave, and a task is never its own dependent.
/// </summary>
internal sealed class TaskGraph
{
    private readonly int[][] _deps;
    private readonly int[][] _dependents;
    private readonly int[] _waves;

    public TaskGraph(IReadOnlyList<TaskDefinition> tasks)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < tasks.Count; i++)
            index.TryAdd(tasks[i].Id, i);

        _deps = [.. tasks.Select(task => task.Deps
            .Select(dep => index.TryGetValue(dep, out var d) ? d : -1)
            .Where(d => d >= 0)
            .Distinct()
            .ToArray())];

        var dependents = Enumerable.Range(0, tasks.Count).Select(_ => new List<int>()).ToArray();
        for (var i = 0; i < _deps.Length; i++)
        {
            foreach (var dep in _deps[i])
                dependents[dep].Add(i);
        }
        _dependents = [.. dependents.Select(list => list.ToArray())];
        _waves = ComputeWaves();
    }

    /// <summary>Positions of the task's direct deps, in deps order.</summary>
    public IReadOnlyList<int> Deps(int task) => _deps[task];

    /// <summary>1 without deps, otherwise 1 + the highest wave of the deps.</summary>
    public int Wave(int task) => _waves[task];

    /// <summary>The number of tasks that depend on the task directly or indirectly.</summary>
    public int Dependents(int task) => Reachable(task, _dependents).Count;

    /// <summary>True when a direct or indirect dep satisfies the predicate.</summary>
    public bool AnyAncestor(int task, Func<int, bool> predicate) => Reachable(task, _deps).Any(predicate);

    // All tasks reachable from start along edges, without start itself.
    private static HashSet<int> Reachable(int start, int[][] edges)
    {
        var seen = new HashSet<int>();
        var queue = new Queue<int>(edges[start]);
        while (queue.TryDequeue(out var next))
        {
            if (next == start || !seen.Add(next))
                continue;
            foreach (var edge in edges[next])
                queue.Enqueue(edge);
        }
        return seen;
    }

    // Depth-first with an explicit stack, so that a long chain cannot overflow the call stack.
    private int[] ComputeWaves()
    {
        var waves = new int[_deps.Length];   // 0 = not computed yet
        var onStack = new bool[_deps.Length];
        var stack = new Stack<(int Task, int NextDep)>();
        for (var root = 0; root < _deps.Length; root++)
        {
            if (waves[root] != 0)
                continue;

            onStack[root] = true;
            stack.Push((root, 0));
            while (stack.TryPop(out var frame))
            {
                var deps = _deps[frame.Task];
                if (frame.NextDep < deps.Length)
                {
                    stack.Push((frame.Task, frame.NextDep + 1));
                    var dep = deps[frame.NextDep];
                    if (waves[dep] == 0 && !onStack[dep])
                    {
                        onStack[dep] = true;
                        stack.Push((dep, 0));
                    }
                    continue;
                }

                // A dep still on the stack closes a cycle; its wave is 0 here and so does not count.
                waves[frame.Task] = 1 + deps.Select(d => waves[d]).DefaultIfEmpty(0).Max();
                onStack[frame.Task] = false;
            }
        }
        return waves;
    }
}

using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Returns whatever Next builds and records the arguments of the last call; disposable, so the store's
// Dispose can be checked (spec 23.6).
public sealed class FakeGitReader(Func<GitInfo> next) : IGitReader, IDisposable
{
    private readonly Lock _lock = new();
    private Func<GitInfo> _next = next;
    private Exception? _throwOnce;
    private int _reads;
    private int _disposeCount;
    private int _readsAtDispose;

    public string? LastRepoPath { get; private set; }
    public PlanInfo? LastPlan { get; private set; }
    public ImmutableArray<TaskView> LastTasks { get; private set; }
    public DateTimeOffset LastNow { get; private set; }

    public int Reads
    {
        get { lock (_lock) return _reads; }
    }

    public bool Disposed
    {
        get { lock (_lock) return _disposeCount > 0; }
    }

    public int DisposeCount
    {
        get { lock (_lock) return _disposeCount; }
    }

    // The number of reads when Dispose was first called.
    public int ReadsAtDispose
    {
        get { lock (_lock) return _readsAtDispose; }
    }

    public Func<GitInfo> Next
    {
        get { lock (_lock) return _next; }
        set { lock (_lock) _next = value; }
    }

    public void ThrowOnce(Exception exception)
    {
        lock (_lock)
            _throwOnce = exception;
    }

    public GitInfo Read(string repoPath, PlanInfo? plan, ImmutableArray<TaskView> tasks, DateTimeOffset now)
    {
        Func<GitInfo> next;
        lock (_lock)
        {
            _reads++;
            LastRepoPath = repoPath;
            LastPlan = plan;
            LastTasks = tasks;
            LastNow = now;
            if (_throwOnce is { } exception)
            {
                _throwOnce = null;
                throw exception;
            }
            next = _next;
        }
        return next();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposeCount++ == 0)
                _readsAtDispose = _reads;
        }
    }
}

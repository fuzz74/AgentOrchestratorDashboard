using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Core.Runs;
using OrchDash.Core.Store;

namespace OrchDash.App;

/// <summary>
/// Owns the store of the run the dashboard shows and switches it for another run, and lists the repo's runs (spec 34.2,
/// 34.3). The shell reads <see cref="Latest"/>, <see cref="Runs"/>, <see cref="Loading"/> and <see cref="Problem"/> on
/// the UI thread each tick; a switch and a listing run on thread-pool threads, possibly at the same time, and publish
/// their results with <see cref="Volatile"/> and <see cref="Interlocked"/>. No call blocks the caller, and no exception of
/// the factory, a store's <c>Start</c> or the listing reaches the caller: it shows as <see cref="Problem"/> (4.5).
/// </summary>
/// <remarks>
/// <see cref="Dispose"/> does not wait for a switch in flight: it disposes the store that is current, and a switch that
/// ends after it disposes the store it made instead of making it current. So every store the host made is disposed,
/// and the UI thread that disposes the host on exit never waits for a slow store start.
/// </remarks>
public sealed class RunHost : IRunHost, IDisposable
{
    private readonly string _repoPath;
    private readonly Func<string, RunStore> _createStore;
    private readonly RunSnapshot _empty;
    private readonly Lock _gate = new();   // orders making a store current against Dispose
    private Current? _current;
    private RunCatalog? _runs;
    private string? _loading;
    private string? _problem;
    private bool _disposed;

    /// <summary>A host for the run at <paramref name="repoPath"/>; touches nothing until <see cref="Start"/>.</summary>
    public RunHost(string repoPath, Func<string, RunStore> createStore)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(createStore);
        _repoPath = repoPath;
        _createStore = createStore;
        _empty = RunSnapshot.Empty(repoPath);
    }

    /// <summary>The repo path of the current store; the path given to the constructor before <see cref="Start"/>.</summary>
    public string RepoPath => Volatile.Read(ref _current)?.RepoPath ?? _repoPath;

    public RunCatalog? Runs => Volatile.Read(ref _runs);

    public string? Loading => Volatile.Read(ref _loading);

    public string? Problem => Volatile.Read(ref _problem);

    /// <summary>
    /// Creates and starts the store of the constructor's path on the calling thread. An exception of the factory or of
    /// <c>Start</c> is thrown to the caller, after a store that failed to start is disposed.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is not null)
                throw new InvalidOperationException("The run host has already been started.");

            var store = CreateAndStart(_repoPath);
            Volatile.Write(ref _current, new Current(_repoPath, store));
        }
    }

    /// <summary>
    /// The current store's snapshot; before <see cref="Start"/> one cached empty snapshot of the constructor's path;
    /// after <see cref="Dispose"/> the last snapshot of the store that was current.
    /// </summary>
    public RunSnapshot Latest() => Volatile.Read(ref _current)?.Store.Current ?? _empty;

    /// <summary>
    /// Switches to the run at <paramref name="repoPath"/> on a thread-pool thread and returns at once. Ignored while a
    /// switch is loading, for the current path, before <see cref="Start"/> and after <see cref="Dispose"/>.
    /// </summary>
    public void SwitchTo(string repoPath)
    {
        if (string.IsNullOrEmpty(repoPath) || Volatile.Read(ref _current) is not { } current || Volatile.Read(ref _disposed)
            || RunPaths.Same(repoPath, current.RepoPath))
            return;

        var name = RunPaths.Stamp(repoPath) ?? Path.GetFileName(RunPaths.RepoRoot(repoPath));
        if (Interlocked.CompareExchange(ref _loading, name, null) is not null)
            return;

        ThreadPool.QueueUserWorkItem(_ => Switch(repoPath));
    }

    /// <summary>Lists the runs of the current repo on a thread-pool thread and returns at once; ignored after <see cref="Dispose"/>.</summary>
    public void RefreshRuns()
    {
        if (Volatile.Read(ref _disposed))
            return;

        var repoPath = RepoPath;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                // A new instance each time, so the shell refreshes an open Runs dialog.
                Volatile.Write(ref _runs, RunCatalogReader.List(repoPath));
                Volatile.Write(ref _problem, null);
            }
            catch (Exception e)
            {
                // RunCatalogReader never throws by contract; this keeps a broken one off the thread pool's back.
                Volatile.Write(ref _problem, e.Message);
            }
        });
    }

    /// <summary>Disposes the current store; a switch in flight disposes its new store when it ends (see the remarks).</summary>
    public void Dispose()
    {
        Current? current;
        lock (_gate)
        {
            if (_disposed)
                return;
            Volatile.Write(ref _disposed, true);
            current = _current;
        }
        current?.Store.Dispose();
    }

    private void Switch(string repoPath)
    {
        try
        {
            var store = CreateAndStart(repoPath);
            Current? previous;
            lock (_gate)
            {
                previous = _disposed ? null : _current;
                if (!_disposed)
                    Volatile.Write(ref _current, new Current(repoPath, store));
            }
            // After Dispose the new store is not made current, so it is the one to dispose.
            (previous?.Store ?? store).Dispose();
            Volatile.Write(ref _problem, null);
        }
        catch (Exception e)
        {
            Volatile.Write(ref _problem, e.Message);
        }
        finally
        {
            // Last, so that the shell sees the new store, or the problem, once Loading is gone.
            Volatile.Write(ref _loading, null);
        }
    }

    /// <summary>A started store on <paramref name="repoPath"/>; a store whose <c>Start</c> throws is disposed before the exception goes on.</summary>
    private RunStore CreateAndStart(string repoPath)
    {
        var store = _createStore(repoPath);
        try
        {
            store.Start();
            return store;
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    /// <summary>The current store and the repo path it was created for.</summary>
    private sealed record Current(string RepoPath, RunStore Store);
}

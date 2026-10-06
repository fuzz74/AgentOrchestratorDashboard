using OrchDash.Contracts;
using OrchDash.Core.Model;

namespace OrchDash.Tests.Shell;

/// <summary>
/// A run host whose catalog, loading text and problem the test sets, and which records <c>RefreshRuns</c> and
/// <c>SwitchTo</c>. The shell reads it on the UI thread while the test thread sets it.
/// </summary>
internal sealed class FakeRunHost : IRunHost
{
    private readonly object _gate = new();
    private readonly List<string> _switchedTo = [];
    private RunCatalog? _runs;
    private string? _loading;
    private string? _problem;
    private int _refreshes;

    public RunCatalog? Runs
    {
        get => Volatile.Read(ref _runs);
        set => Volatile.Write(ref _runs, value);
    }

    public string? Loading
    {
        get => Volatile.Read(ref _loading);
        set => Volatile.Write(ref _loading, value);
    }

    public string? Problem
    {
        get => Volatile.Read(ref _problem);
        set => Volatile.Write(ref _problem, value);
    }

    /// <summary>How often <see cref="RefreshRuns"/> was called.</summary>
    public int Refreshes => Volatile.Read(ref _refreshes);

    /// <summary>The paths given to <see cref="SwitchTo"/>, in call order.</summary>
    public IReadOnlyList<string> SwitchedTo
    {
        get
        {
            lock (_gate)
            {
                return [.. _switchedTo];
            }
        }
    }

    public void RefreshRuns() => Interlocked.Increment(ref _refreshes);

    public void SwitchTo(string repoPath)
    {
        lock (_gate)
        {
            _switchedTo.Add(repoPath);
        }
    }
}

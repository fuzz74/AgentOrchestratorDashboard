using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Git;

/// <summary>
/// Reads each task's branch, worktree, commits and diffs with read-only git commands (20). <see cref="Read"/> returns
/// the latest completed read at once and starts the next one on a background thread at most every 5 seconds (60 after
/// git could not be started); <see cref="ReadNow"/> runs one read on the calling thread. Never throws.
/// </summary>
public sealed class GitReader(string gitPath = "git") : IGitReader, IDisposable
{
    private static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UnstartableInterval = TimeSpan.FromSeconds(60);

    private readonly GitRunner _runner = new(gitPath);

    // One read at a time, so the diff values to reuse (20.7) always come from the last completed read.
    private readonly Lock _readLock = new();
    private Dictionary<string, TaskDiffs> _diffs = new(StringComparer.Ordinal);

    // Guards the fields below.
    private readonly Lock _stateLock = new();
    private GitInfo _latest = GitInfo.Empty;
    private DateTimeOffset? _lastStartedAt;   // the now of the call that started the last background read
    private DateTimeOffset? _unstartableAt;   // the now of the last read that could not start git
    private bool _reading;
    private bool _disposed;

    /// <summary>20.1: the latest completed read (<see cref="GitInfo.Empty"/> before the first); starts a background read when one is due.</summary>
    public GitInfo Read(string repoPath, PlanInfo? plan, ImmutableArray<TaskView> tasks, DateTimeOffset now)
    {
        lock (_stateLock)
        {
            if (!_disposed && !_reading && IsDue(now))
                StartBackgroundRead(repoPath, plan, tasks, now);
            return _latest;
        }
    }

    /// <summary>20.2: one read on the calling thread, with <c>ReadAt</c> = <paramref name="now"/>.</summary>
    public GitInfo ReadNow(string repoPath, PlanInfo? plan, ImmutableArray<TaskView> tasks, DateTimeOffset now)
    {
        lock (_readLock)
        {
            GitInfo info;
            var unstartable = false;
            try
            {
                var pass = new GitReadPass(_runner, _diffs);
                info = pass.Run(repoPath, plan, tasks, now);
                unstartable = pass.GitUnstartable;
                _diffs = pass.Diffs;
            }
            catch (Exception e)
            {
                info = GitInfo.Empty with { ReadAt = now, Problem = $"git: {e.Message}" };
            }

            lock (_stateLock)
            {
                _latest = info;
                _unstartableAt = unstartable ? now : null;
            }

            return info;
        }
    }

    /// <summary>20.10: starts no further read; a running one ends on its own.</summary>
    public void Dispose()
    {
        lock (_stateLock)
            _disposed = true;
    }

    private bool IsDue(DateTimeOffset now) =>
        (_lastStartedAt is not { } started || now - started >= ReadInterval)
        && (_unstartableAt is not { } unstartable || now - unstartable >= UnstartableInterval);

    // Called under _stateLock.
    private void StartBackgroundRead(string repoPath, PlanInfo? plan, ImmutableArray<TaskView> tasks, DateTimeOffset now)
    {
        var thread = new Thread(() =>
        {
            try
            {
                ReadNow(repoPath, plan, tasks, now);
            }
            finally
            {
                lock (_stateLock)
                    _reading = false;
            }
        })
        {
            IsBackground = true,
            Name = "OrchDash git reader",
        };

        try
        {
            thread.Start();
            _reading = true;
            _lastStartedAt = now;
        }
        catch (OutOfMemoryException)
        {
            // no thread could be made; the next due call tries again
        }
    }
}

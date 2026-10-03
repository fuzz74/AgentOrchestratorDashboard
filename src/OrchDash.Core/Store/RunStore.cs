using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Store;

// Polls the run folder and the session logs and publishes an immutable RunSnapshot when the content
// changed (spec 5.1-5.9). Polls are serialised; Current may be read from any thread.
public sealed class RunStore : IDisposable
{
    private const string NoPlanProblem = "tasks.json: no plan in this read; keeping the previous plan and tasks";

    private readonly string _repoPath;
    private readonly IRunFolderReader _reader;
    private readonly SessionParserFactory _parsers;
    private readonly TimeSpan _pollInterval;
    private readonly TimeProvider _time;
    private readonly Lock _pollLock = new();
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Dictionary<string, SessionTracker> _trackers = new(StringComparer.Ordinal);
    private volatile RunSnapshot _current;
    private RunSnapshot? _lastRead;   // the last snapshot built by a poll that did not throw
    private Thread? _thread;
    private bool _disposed;

    public RunStore(string repoPath, IRunFolderReader reader, SessionParserFactory parsers,
                    TimeSpan? pollInterval = null, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(repoPath);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(parsers);
        if (pollInterval is { } interval)
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero, nameof(pollInterval));

        _repoPath = repoPath;
        _reader = reader;
        _parsers = parsers;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _time = time ?? TimeProvider.System;
        _current = RunSnapshot.Empty(repoPath);
    }

    public RunSnapshot Current => _current;

    public void Poll()
    {
        lock (_pollLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            PollCore();
        }
    }

    public void Start()
    {
        lock (_pollLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null)
                throw new InvalidOperationException("The store has already been started.");

            PollCore();
            _thread = new Thread(PollLoop) { IsBackground = true, Name = "RunStore poll" };
            _thread.Start();
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_pollLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            thread = _thread;
        }

        _stop.Set();
        thread?.Join();
        _stop.Dispose();
    }

    private void PollLoop()
    {
        while (!_stop.Wait(_pollInterval))
        {
            lock (_pollLock)
                PollCore();
        }
    }

    // Runs under _pollLock. Never throws: a failure is published as a problem line (5.7).
    private void PollCore()
    {
        RunSnapshot candidate;
        try
        {
            var now = _time.GetLocalNow();
            var data = _reader.Read(Path.Combine(_repoPath, ".orchestrator"), now);
            candidate = Build(data, now);
            _lastRead = candidate;
        }
        catch (Exception e)
        {
            var baseline = _lastRead ?? RunSnapshot.Empty(_repoPath);
            candidate = baseline with { Problems = baseline.Problems.Add(e.Message) };
        }

        var current = _current;
        if (current.Version == 0 || !SnapshotComparer.SameContent(current, candidate))
            _current = candidate with { Version = current.Version + 1 };
    }

    private RunSnapshot Build(RunFolderData data, DateTimeOffset now)
    {
        var problems = ImmutableArray.CreateBuilder<string>();
        problems.AddRange(OrEmpty(data.Problems));

        var plan = data.Plan;
        ImmutableArray<TaskView> tasks;
        if (plan is null && _current.Plan is not null)
        {
            plan = _current.Plan;
            tasks = _current.Tasks;
            problems.Add(NoPlanProblem);
        }
        else
        {
            tasks = [.. OrEmpty(data.Tasks).OrderBy(t => t.Wave).ThenBy(t => t.Id, StringComparer.Ordinal)];
        }

        var sessions = ReadSessions(OrEmpty(data.Sessions), data.Run.Phase);
        return new RunSnapshot(0, now, _repoPath, data.Run, plan, tasks, sessions,
            OrEmpty(data.Progress), problems.ToImmutable());
    }

    private ImmutableArray<Session> ReadSessions(ImmutableArray<SessionFiles> sessionFiles, RunPhase phase)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var read = new List<(SessionFiles Files, SessionTracker Tracker, DateTimeOffset? StartedAt)>();
        foreach (var files in sessionFiles)
        {
            if (!seen.Add(files.Key))
                continue;
            if (!_trackers.TryGetValue(files.Key, out var tracker))
                _trackers[files.Key] = tracker = new SessionTracker(_parsers, WorkDir(files));

            try
            {
                tracker.Refresh(files);
            }
            catch
            {
                tracker.Reset();   // the next poll feeds a new parser from the start
                throw;
            }
            read.Add((files, tracker, tracker.Content.FirstEventAt ?? files.PromptWrittenAt));
        }

        foreach (var key in _trackers.Keys.Where(k => !seen.Contains(k)).ToList())
            _trackers.Remove(key);

        // For the Running rule: the latest start per task, and over all sessions for bootstrap and planner.
        DateTimeOffset? latestOfAll = null;
        var latestByTask = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var (files, _, startedAt) in read)
        {
            if (startedAt is not { } started)
                continue;
            if (latestOfAll is null || started > latestOfAll)
                latestOfAll = started;
            if (files.TaskId is { } taskId && (!latestByTask.TryGetValue(taskId, out var latest) || started > latest))
                latestByTask[taskId] = started;
        }

        return [.. read
            .Select(r =>
            {
                var latest = r.Files.TaskId is not { } taskId ? latestOfAll
                    : latestByTask.TryGetValue(taskId, out var ofTask) ? ofTask
                    : null;
                var state = StateOf(r.Files, r.Tracker.Content, phase, laterSessionExists: latest > r.StartedAt);
                return new Session(r.Files, r.Tracker.Provider, state, r.Tracker.Prompt, r.StartedAt, r.Tracker.Content);
            })
            .OrderBy(s => s.StartedAt is null)
            .ThenBy(s => s.StartedAt)
            .ThenBy(s => s.Files.Key, StringComparer.Ordinal)];
    }

    // Session state rules in spec 4.3.
    private static SessionState StateOf(SessionFiles files, SessionContent content, RunPhase phase, bool laterSessionExists)
    {
        if (content.Result is { } result)
            return result.IsError ? SessionState.Failed : SessionState.Succeeded;
        if (files.HasResultFile)
            return SessionState.Failed;
        return phase is RunPhase.Planning or RunPhase.Running or RunPhase.Stopping && !laterSessionExists
            ? SessionState.Running
            : SessionState.Aborted;
    }

    // <repo>.worktrees/<TaskId> for task sessions, the repo path for bootstrap and planner.
    private string WorkDir(SessionFiles files) =>
        files.TaskId is { } taskId
            ? Path.Combine(Path.TrimEndingDirectorySeparator(_repoPath) + ".worktrees", taskId)
            : _repoPath;

    private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> items) => items.IsDefault ? [] : items;
}

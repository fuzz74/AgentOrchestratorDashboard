using System.Collections.Immutable;
using OrchDash.Core.CommandLogs;
using OrchDash.Core.Model;
using OrchDash.Core.Processes;

namespace OrchDash.Core.Store;

// Polls the run folder and the session logs and publishes an immutable RunSnapshot when the content
// changed (spec 5.1-5.9), with the data of the provider stores added to each session (spec 14) and the
// git, process and command-log data of the insight sources (spec 23). Polls are serialised; Current may be
// read from any thread.
public sealed class RunStore : IDisposable
{
    private const string NoPlanProblem = "tasks.json: no plan in this read; keeping the previous plan and tasks";

    private readonly string _repoPath;
    private readonly IRunFolderReader _reader;
    private readonly SessionParserFactory _parsers;
    private readonly TimeSpan _pollInterval;
    private readonly TimeProvider _time;
    private readonly ProviderStores? _stores;
    private readonly InsightSources? _sources;
    private readonly Lock _pollLock = new();
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Dictionary<string, SessionTracker> _trackers = new(StringComparer.Ordinal);
    private volatile RunSnapshot _current;
    private RunSnapshot? _lastRead;   // the last snapshot built by a poll that did not throw
    private Thread? _thread;
    private bool _disposed;

    public RunStore(string repoPath, IRunFolderReader reader, SessionParserFactory parsers,
                    TimeSpan? pollInterval = null, TimeProvider? time = null, ProviderStores? stores = null,
                    InsightSources? sources = null)
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
        _stores = stores;
        _sources = sources;
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
        DisposeSources();
    }

    // Spec 23.6: after the poll thread has ended; a source given for two members is disposed once.
    private void DisposeSources()
    {
        if (_sources is null)
            return;

        var disposables = new object?[] { _sources.Git, _sources.Processes, _sources.Commands }
            .OfType<IDisposable>()
            .Distinct<IDisposable>(ReferenceEqualityComparer.Instance);
        foreach (var disposable in disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception)
            {
                // a source that fails to dispose must not keep the others from being disposed
            }
        }
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

        // Spec 14.1-14.7: the provider stores' data; their problem lines follow the ones above.
        var merged = ProviderMerge.Apply(ReadSessions(OrEmpty(data.Sessions), data.Run.Phase), WorkDir, _stores);
        problems.AddRange(merged.Problems);
        var snapshot = new RunSnapshot(0, now, _repoPath, data.Run, plan, tasks, merged.Sessions,
            OrEmpty(data.Progress), []);
        if (_sources is not null)
            snapshot = AddInsight(snapshot, _sources, problems);
        return snapshot with { Problems = problems.ToImmutable() };
    }

    // Spec 23.2-23.4: the sources are asked in the order commands, processes, git, each only when it is set,
    // and their problem lines follow the store's own in the order git, processes, commands. A source that
    // throws keeps the member of the published snapshot, and its message becomes the problem line.
    private RunSnapshot AddInsight(RunSnapshot snapshot, InsightSources sources, ImmutableArray<string>.Builder problems)
    {
        var commands = snapshot.Commands;
        ImmutableArray<string> commandProblems = [];
        if (sources.Commands is { } commandReader)
        {
            try
            {
                var read = commandReader.Read(Path.Combine(_repoPath, ".orchestrator"));
                commands = CommandRules.Resolve(read.Logs, snapshot.Progress, snapshot.Plan, snapshot.Tasks, snapshot.Run.Phase);
                commandProblems = OrEmpty(read.Problems);
            }
            catch (Exception e)
            {
                commands = _current.Commands;
                commandProblems = [e.Message];
            }
        }

        var processes = snapshot.Processes;
        string? processProblem = null;
        if (sources.Processes is { } lister)
        {
            try
            {
                var listed = lister.List(snapshot.ReadAt);
                processes = listed with { Processes = ProcessRules.Match(listed.Processes, snapshot.Tasks, snapshot.Sessions) };
                processProblem = processes.Problem;
            }
            catch (Exception e)
            {
                processes = _current.Processes;
                processProblem = e.Message;
            }
        }

        var git = snapshot.Git;
        IEnumerable<string> gitProblems = [];
        if (sources.Git is { } gitReader)
        {
            try
            {
                git = gitReader.Read(_repoPath, snapshot.Plan, snapshot.Tasks, snapshot.ReadAt);
                gitProblems = git.Problem?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];
            }
            catch (Exception e)
            {
                git = _current.Git;
                gitProblems = [e.Message];
            }
        }

        problems.AddRange(gitProblems);
        if (!string.IsNullOrEmpty(processProblem))
            problems.Add(processProblem);
        problems.AddRange(commandProblems);
        return snapshot with { Git = git, Processes = processes, Commands = commands };
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

    // Spec 31.2: <RepoRoot>.worktrees\<TaskId> for task sessions, the repo root for bootstrap and planner.
    private string WorkDir(SessionFiles files) => RunPaths.WorkDir(_repoPath, files.TaskId);

    private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> items) => items.IsDefault ? [] : items;
}

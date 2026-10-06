using System.Collections.Immutable;
using OrchDash.Core.CommandLogs;
using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Replay;

// A constructed run in the shape of the spec's SampleRun.CreateTimeline(), and builders for smaller snapshots.
// Tasks in snapshot order: alpha, gamma (wave 1), beta (deps alpha), delta (deps gamma), epsilon (deps delta).
// Every call creates new instances.
public static class ReplayData
{
    public const string AlphaSummary = "Parser done";
    public const string AlphaNotes = "Use Parser.Parse";
    public const string SpecFeedback = "Spec: the parser skips escapes";
    public const string QualityFeedback = "Quality: two issues";
    public const string GammaSummary = "Gamma second try";
    public const string GammaError = "FAILED after 3 attempts: acceptance failed";

    // Indexes of the live sessions (snapshot order).
    public const int Planner = 0, AlphaWorker = 1, GammaWorker1 = 2, AlphaReview1 = 3, AlphaReview2 = 4,
        BetaWorker1 = 5, GammaWorker2 = 6, BetaWorker2 = 7, GammaWorker3 = 8;

    // Indexes of the tasks (snapshot order).
    public const int Alpha = 0, Gamma = 1, Beta = 2, Delta = 3, Epsilon = 4;

    public static DateTimeOffset At(int hour, int minute, int second) => new(2026, 10, 3, hour, minute, second, TimeSpan.Zero);

    public static DateTimeOffset End { get; } = At(12, 30, 0);

    public static RunSnapshot Create(bool withStop = true)
    {
        var progress = Progress(withStop);
        var tasks = ImmutableArray.Create(
            Task("alpha", 1, [], ["src/Alpha/**"]) with
            {
                Status = TaskState.Done, Attempts = 1, SpecRejections = 1, CostUsd = 0.5, SessionId = "s-alpha-1",
                Summary = AlphaSummary, Notes = AlphaNotes, Feedback = QualityFeedback,
                StartedAt = At(12, 0, 5), FinishedAt = At(12, 9, 30), MergedSha = "3f9c2e1", Detail = "0.50 USD, 1 attempt(s)",
            },
            Task("gamma", 1, [], ["src/Shared/Gamma/**"]) with
            {
                Status = TaskState.Failed, Attempts = 3, CostUsd = 0.75, SessionId = "s-gamma-3", Summary = GammaSummary,
                Error = GammaError, StartedAt = At(12, 0, 5), FinishedAt = At(12, 20, 0), Detail = GammaError,
            },
            Task("beta", 2, ["alpha"], ["src/Shared/**", "src/Beta/**"]) with
            {
                Status = TaskState.Running, Mode = "sync", Attempts = 2, SyncRuns = 1, SessionId = "s-beta-2",
                StartedAt = At(12, 12, 1), Detail = "working",
            },
            Task("delta", 2, ["gamma"], ["src/Delta/**"]) with { Status = TaskState.Blocked, Detail = "a dependency failed" },
            Task("epsilon", 3, ["delta", "unknown"], ["src/Epsilon/**"]) with { Status = TaskState.Blocked, Detail = "a dependency failed" });

        var sessions = ImmutableArray.Create(
            PlannerSession(),
            AlphaWorkerSession(),
            GammaWorker1Session(),
            Review("alpha", 1, At(12, 6, 30), At(12, 7, 30), "fail", "pass", SpecFeedback, 0),
            Review("alpha", 2, At(12, 7, 40), At(12, 8, 55), "pass", "fail", QualityFeedback, 2),
            Worker("beta", 1, At(12, 10, 5), "s-beta-1", SessionState.Aborted, [Text(At(12, 10, 30), "Looking")], At(12, 11, 20)),
            WithResult(
                Worker("gamma", 2, At(12, 11, 0), "s-gamma-2", SessionState.Succeeded, [Text(At(12, 12, 0), "Retrying")], At(12, 15, 30)),
                Result(0.5, worker: new WorkerReport("done", GammaSummary, null, null))),
            BetaWorker2Session(),
            Worker("gamma", 3, At(12, 17, 5), "s-gamma-3", SessionState.Failed, [Text(At(12, 18, 0), "Third try")], At(12, 19, 40),
                hasResultFile: true));

        var plan = new PlanInfo(".orchestrator/spec.md", "main", "orch/integration",
            ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create("setup", "\"dotnet restore Sample.slnx\"")]));
        var run = new RunInfo(withStop ? RunPhase.Stopping : RunPhase.Running, At(12, 0, 0), null, 2, Provider.Claude,
            @"C:\bin\claude.exe", withStop);
        var logs = ImmutableArray.Create(
            Log(CommandKind.Acceptance, "gamma", At(12, 19, 45)),
            Log(CommandKind.Acceptance, "alpha", At(12, 6, 0)),
            Log(CommandKind.Setup, "alpha", At(12, 0, 8)));

        return new RunSnapshot(7, End, @"C:\Work\SampleRepo", run, plan, tasks, sessions, progress, ["a problem"])
        {
            Git = GitInfo.Empty with { ReadAt = End, IntegrationBranch = "orch/integration" },
            Processes = new ProcessInfo(End, [], "a process problem"),
            Commands = CommandRules.Resolve(logs, progress, plan, tasks, run.Phase),
        };
    }

    public static ImmutableArray<ProgressEntry> Progress(bool withStop = true)
    {
        List<ProgressEntry> entries =
        [
            Entry(At(11, 58, 0), null, "Planning from .orchestrator/spec.md with opus"),
            Entry(At(12, 0, 0), null, "Run started: 5 tasks, max 2 in parallel"),
            Entry(At(12, 0, 0), null, @"Claude: C:\bin\claude.exe"),
            Entry(At(12, 0, 5), "alpha", @"started (fresh) in C:\Work\SampleRepo.worktrees\alpha"),
            Entry(At(12, 0, 5), "gamma", @"started (fresh) in C:\Work\SampleRepo.worktrees\gamma"),
            Entry(At(12, 0, 8), "alpha", "setup: dotnet restore Sample.slnx"),
            Entry(At(12, 0, 9), "alpha", "attempt 1/3: worker started (sonnet)"),
            Entry(At(12, 0, 10), "gamma", "setup: dotnet restore Sample.slnx"),
            Entry(At(12, 0, 28), "gamma", "attempt 1/3: worker started (opus)"),
            Entry(At(12, 1, 0), "alpha", "worker: 5 tool calls, last: Edit src/Alpha/Parser.cs", ProgressKind.Activity),
            Entry(At(12, 4, 25), "gamma", "acceptance: dotnet test tests/Gamma"),
            Entry(At(12, 4, 31), "gamma", "acceptance failed (exit 1)", ProgressKind.Failure),
            Entry(At(12, 5, 20), "alpha", "merge conflicts with orch/integration; starting resolver"),
            Entry(At(12, 6, 0), "alpha", "acceptance: dotnet test tests/Alpha"),
            Entry(At(12, 6, 25), "alpha", "review started (sonnet)"),
            Entry(At(12, 9, 0), "alpha", "review passed", ProgressKind.Success),
            Entry(At(12, 9, 30), "alpha", "DONE in 9m25s, 0.50 USD\nmerged as 3f9c2e1", ProgressKind.Success),
            Entry(At(12, 10, 0), "beta", @"started (fresh) in C:\Work\SampleRepo.worktrees\beta"),
            Entry(At(12, 10, 3), "beta", "attempt 1/3: worker started (sonnet)"),
            Entry(At(12, 10, 55), "gamma", "attempt 2/3: worker started (opus)"),
            Entry(At(12, 11, 30), "beta", "paused: usage limit reached"),
            Entry(At(12, 12, 0), "beta", "merge conflict with newer integration work; re-queued to sync"),
            Entry(At(12, 12, 1), "beta", @"started (sync) in C:\Work\SampleRepo.worktrees\beta"),
            Entry(At(12, 12, 2), "beta", "attempt 2/3: worker started (sonnet)"),
            Entry(At(12, 15, 35), "gamma", "acceptance: dotnet test tests/Gamma"),
            Entry(At(12, 15, 41), "gamma", "acceptance failed (exit 1)", ProgressKind.Failure),
            Entry(At(12, 17, 0), "gamma", "attempt 3/3: worker started (opus)"),
            Entry(At(12, 19, 45), "gamma", "acceptance: dotnet test tests/Gamma"),
            Entry(At(12, 19, 51), "gamma", "acceptance failed (exit 1)", ProgressKind.Failure),
            Entry(At(12, 20, 0), "gamma", GammaError, ProgressKind.Failure),
        ];
        if (withStop)
            entries.Add(Entry(At(12, 21, 0), null, "Graceful stop requested; waiting for running tasks"));
        entries.Add(Entry(At(12, 25, 0), "beta", "worker: 3 tool calls, last: Read src/Beta/Sync.cs", ProgressKind.Activity));
        return [.. entries];
    }

    public static ProgressEntry Entry(DateTimeOffset time, string? source, string message, ProgressKind kind = ProgressKind.Info) =>
        new(time, source, message, kind);

    public static TaskView Task(string id, int wave = 1, string[]? deps = null, string[]? owns = null) =>
        new(id, "Title " + id, "Prompt " + id, [.. deps ?? []], [.. owns ?? ["src/" + id + "/**"]],
            "dotnet test", null, wave, 0, TaskState.Pending, "fresh", 0, 0, 0, 0.0,
            null, null, null, null, null, null, null, null, "ready");

    // A snapshot of the given parts; its run is a placeholder, as the replay derives its own from the progress.
    public static RunSnapshot Snapshot(IEnumerable<ProgressEntry> progress, IEnumerable<TaskView>? tasks = null,
        IEnumerable<Session>? sessions = null) =>
        new(1, End, @"C:\Work\Repo", new RunInfo(RunPhase.Running, null, null, null, Provider.Unknown, null, false), null,
            [.. tasks ?? []], [.. sessions ?? []], [.. progress], []);

    public static SessionFiles Files(string? taskId, AgentRole role, int attempt = 1, int reviewTry = 0, bool hasResultFile = false)
    {
        var name = role switch
        {
            AgentRole.Reviewer => $"attempt-{attempt}-review-{reviewTry}",
            AgentRole.Planner => "planner",
            _ => $"attempt-{attempt}-{role.ToString().ToLowerInvariant()}",
        };
        var key = taskId is null ? name + ".json" : $"{taskId}-20261003-120005/{name}.json";
        var path = @"C:\Work\SampleRepo\.orchestrator\logs\" + key.Replace('/', '\\');
        return new SessionFiles(key, taskId, role, taskId is null ? null : taskId + "-20261003-120005", attempt, reviewTry, false,
            path, path + ".prompt", path + ".events", path + ".stderr", hasResultFile, true, null);
    }

    public static Session Session(SessionFiles files, DateTimeOffset startedAt, SessionState state, string sessionId,
        IEnumerable<ConversationItem>? items = null, IEnumerable<ModelCall>? calls = null, DateTimeOffset? lastEventAt = null) =>
        new(files, Provider.Claude, state, "Prompt of " + files.Key, startedAt,
            SessionContent.Empty with
            {
                SessionId = sessionId, Model = "claude-sonnet-4-5",
                Items = [.. items ?? []], Calls = [.. calls ?? []],
                FirstEventAt = startedAt, LastEventAt = lastEventAt,
            });

    public static Session WithResult(Session session, SessionResult result) =>
        session with
        {
            Files = session.Files with { HasResultFile = true },
            Content = session.Content with { Result = result },
        };

    public static SessionResult Result(double? cost, bool isError = false, WorkerReport? worker = null, ReviewVerdict? review = null) =>
        new(isError, isError ? "error_during_execution" : "success", "text", null, worker, review,
            cost, 3, TimeSpan.FromMinutes(1), null, null, null, null, null, null);

    public static AssistantText Text(DateTimeOffset? time, string text) => new("c1", time, text);

    public static ToolCall Tool(DateTimeOffset? time, string name, DateTimeOffset? resultTime, bool hasResult = true) =>
        new("c1", time, "tool-" + name, name, "{}", name + " summary",
            hasResult ? new ToolResult(resultTime, false, "ok", null, 0) : null);

    public static ModelCall Call(string id, DateTimeOffset? startedAt) => new(id, "claude-sonnet-4-5", startedAt, null);

    private static Session PlannerSession() =>
        WithResult(
            Session(Files(null, AgentRole.Planner), At(11, 58, 5), SessionState.Succeeded, "s-planner",
                [Text(At(11, 58, 30), "Planning")], [Call("p1", At(11, 58, 5))], At(11, 59, 50)),
            Result(1.0));

    // The alpha worker: an untimed item, a tool call whose result comes at 12:05:30, a result at 12:05:50,
    // a checkpoint, a rate limit seen at 12:05:45 and store entries with and without times.
    private static Session AlphaWorkerSession()
    {
        var session = Session(Files("alpha", AgentRole.Worker), At(12, 0, 10), SessionState.Succeeded, "s-alpha-1",
            [
                Text(At(12, 0, 12), "Reading the parser"),
                Tool(At(12, 0, 15), "Edit", At(12, 0, 16)),
                Text(null, "Edited"),
                Tool(At(12, 3, 0), "Bash", At(12, 5, 30)),
                Text(At(12, 5, 40), "Done"),
            ],
            [Call("a1", At(12, 0, 10)), Call("a2", At(12, 3, 30)), Call("a3", At(12, 5, 40))],
            At(12, 5, 50));
        session = WithResult(session, Result(0.25, worker: new WorkerReport("done", AlphaSummary, AlphaNotes, null)));
        return session with
        {
            Content = session.Content with
            {
                Checkpoint = new ContextCheckpoint(1000, 200, [], [], null, null),
                RateLimit = new RateLimit("allowed", "five_hour", 0.2, null, null, null, At(12, 5, 45)),
            },
            Stores = StoreData.Empty with
            {
                Calls = [Figures(At(12, 0, 10)), Figures(At(12, 3, 30)), Figures(At(12, 5, 40)), Figures(null)],
                Injected = [Injected(At(12, 0, 10)), Injected(At(12, 5, 41)), Injected(null)],
                CostUsd = 0.25,
            },
        };
    }

    // The first gamma worker ends with an error; its Read result has no time and counts at the call's time.
    private static Session GammaWorker1Session() =>
        WithResult(
            Session(Files("gamma", AgentRole.Worker), At(12, 0, 30), SessionState.Failed, "s-gamma-1",
                [Tool(At(12, 1, 0), "Read", null), Text(At(12, 4, 0), "Giving up")], [Call("g1", null)], At(12, 4, 20)),
            Result(0.25, isError: true));

    // The second beta worker still runs; its rate limit has no time and is always kept.
    private static Session BetaWorker2Session()
    {
        var session = Worker("beta", 2, At(12, 12, 5), "s-beta-2", SessionState.Running, [Text(At(12, 25, 10), "Syncing")], At(12, 25, 10));
        return session with { Content = session.Content with { RateLimit = new RateLimit("allowed", null, 0.2, null, null, null, null) } };
    }

    private static Session Worker(string taskId, int attempt, DateTimeOffset startedAt, string sessionId, SessionState state,
        IEnumerable<ConversationItem> items, DateTimeOffset lastEventAt, bool hasResultFile = false) =>
        Session(Files(taskId, AgentRole.Worker, attempt, hasResultFile: hasResultFile), startedAt, state, sessionId,
            items, [Call(sessionId + "-c1", startedAt)], lastEventAt);

    private static Session Review(string taskId, int reviewTry, DateTimeOffset startedAt, DateTimeOffset lastEventAt,
        string spec, string quality, string summary, int issues) =>
        WithResult(
            Session(Files(taskId, AgentRole.Reviewer, 1, reviewTry), startedAt, SessionState.Succeeded, $"s-{taskId}-r{reviewTry}",
                [Text(startedAt.AddSeconds(10), "Reviewing")], [Call($"r{reviewTry}", startedAt)], lastEventAt),
            Result(0.125, review: new ReviewVerdict(spec, quality, summary,
                [.. Enumerable.Range(1, issues).Select(i => new ReviewIssue("minor", null, "issue " + i))])));

    private static CallFigures Figures(DateTimeOffset? time) => new("call", time, new TokenUsage(10, 0, 0, 5), null, null, null, null);

    private static InjectedItem Injected(DateTimeOffset? time) => new("reminder", "user", time, "injected");

    private static CommandLog Log(CommandKind kind, string taskId, DateTimeOffset writtenAt)
    {
        var key = $"{taskId}-20261003-120005/{(kind == CommandKind.Setup ? "setup" : "attempt-1-acceptance")}.log";
        var path = @"C:\Work\SampleRepo\.orchestrator\logs\" + key.Replace('/', '\\');
        return new CommandLog(kind, taskId, taskId + "-20261003-120005", kind == CommandKind.Setup ? null : 1, key,
            path, path + ".stderr", writtenAt, 10, "output", "", null, CommandOutcome.Unknown, null);
    }
}

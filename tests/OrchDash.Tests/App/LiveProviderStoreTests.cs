using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OrchDash.App;
using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;

namespace OrchDash.Tests.App;

// N.6 with the real reader, parsers and provider stores, the default poll interval and a started store: a complete line
// appended to a Claude transcript or to a Copilot session folder's events.jsonl, and a usage row committed to the
// Copilot database, are in the store's snapshot within 2 seconds. The run and the store folders are temp folders.
public sealed class LiveProviderStoreTests : IDisposable
{
    private const string ClaudeKey = "audio-synth/20261004-120000/attempt-1-worker.json";
    private const string ClaudeSessionId = "66a6a33c-01ca-42bf-85bb-4eec9505a991";
    private const string CopilotKey = "core/20261004-120000/attempt-1-worker.json";

    // Not the fixture's id, so that only the workspace.yaml below can give it to the session.
    private const string CopilotSessionId = "0f1e2d3c-4b5a-4968-8776-a5b4c3d2e1f0";

    private static readonly string ClaudeLog = Path.Combine(
        FixtureRuns.ClaudeRepo, ".orchestrator", "logs", "audio-synth", "20261001-104634", "attempt-1-worker.json.events.jsonl");

    private static readonly string ClaudeTranscript = Path.Combine(
        FixtureRuns.ClaudeStore, "projects", "C--Data-AI-AnsiDemo-worktrees-audio-synth", ClaudeSessionId + ".jsonl");

    private static readonly string CopilotLog = Path.Combine(
        FixtureRuns.CopilotRepo, ".orchestrator", "logs", "core", "20261003-113444", "attempt-1-worker.json.events.jsonl");

    private static readonly string CopilotFolder = Path.Combine(
        FixtureRuns.CopilotStore, "session-state", "ae783abf-0989-4988-88c1-089deac14062");

    /// <summary>The created_at of the fixture's workspace.yaml for the core worker, as written there.</summary>
    private static readonly string CreatedAt = File.ReadLines(Path.Combine(CopilotFolder, "workspace.yaml"))
        .Single(line => line.StartsWith("created_at: ", StringComparison.Ordinal))["created_at: ".Length..];

    private readonly TempFolder _repo = new();
    private readonly TempFolder _stores = new();

    public void Dispose()
    {
        // The tests dispose their stores and databases first; no pooled connection may keep a file open.
        SqliteConnection.ClearAllPools();
        _repo.Dispose();
        _stores.Dispose();
    }

    [Fact]
    public async Task A_line_appended_to_a_claude_transcript_is_published_within_two_seconds()
    {
        // The worker log's first lines: a title change, the commands, init, then a Read and a Glob call.
        using var runLock = StartRun("audio-synth", ClaudeKey, File.ReadLines(ClaudeLog).Take(6));
        // The transcript up to its first assistant record: prompt, attachments and the prompt snapshot.
        var lines = File.ReadAllLines(ClaudeTranscript);
        var firstCall = Array.FindIndex(lines, line => TypeOf(line) == "assistant");
        Assert.True(firstCall > 0);
        var transcript = _stores.Write(
            Path.Combine("claude", Path.GetRelativePath(FixtureRuns.ClaudeStore, ClaudeTranscript)),
            string.Concat(lines[..firstCall].Select(line => line + "\n")));

        using var store = CreateStore();
        store.Start();
        var session = Assert.Single(store.Current.Sessions);
        Assert.Equal(Provider.Claude, session.Provider);
        Assert.Equal(SessionState.Running, session.State);
        Assert.Equal(ClaudeSessionId, session.Content.SessionId);
        Assert.Empty(session.Unavailable);
        Assert.NotEmpty(session.Stores.SystemPrompt);
        Assert.Empty(session.Stores.Calls);

        File.AppendAllText(transcript, lines[firstCall] + "\n");

        await PublishedWithinTwoSeconds(store, s => !Single(s).Stores.Calls.IsEmpty);
        var call = Assert.Single(Single(store.Current).Stores.Calls);
        Assert.Equal(MessageIdOf(lines[firstCall]), call.CallId);
    }

    [Fact]
    public async Task A_system_message_appended_to_a_copilot_session_folder_is_published_within_two_seconds()
    {
        using var runLock = StartCopilotRun();
        // The session folder's events up to its system.message: session.start and user.message.
        var events = File.ReadAllLines(Path.Combine(CopilotFolder, "events.jsonl"));
        var systemMessage = Array.FindIndex(events, line => TypeOf(line) == "system.message");
        Assert.True(systemMessage > 0);
        var eventsFile = WriteSessionFolder(events[..systemMessage]);

        using var store = CreateStore();
        store.Start();
        var session = Assert.Single(store.Current.Sessions);
        Assert.Equal(SessionState.Running, session.State);
        AssertStartedNearCreatedAt(session);
        Assert.Equal(CopilotSessionId, session.Content.SessionId);
        Assert.Equal(["no database rows"], session.Unavailable);
        Assert.Equal(TestedVersions.CopilotCli, session.Stores.CliVersion);
        Assert.Empty(session.Stores.SystemPrompt);

        File.AppendAllText(eventsFile, events[systemMessage] + "\n");

        await PublishedWithinTwoSeconds(store, s => !Single(s).Stores.SystemPrompt.IsEmpty);
        Assert.Equal(2, Single(store.Current).Stores.SystemPrompt.Length);
    }

    [Fact]
    public async Task A_usage_row_committed_to_the_copilot_database_is_published_within_two_seconds()
    {
        using var runLock = StartCopilotRun();
        WriteSessionFolder(File.ReadAllLines(Path.Combine(CopilotFolder, "events.jsonl")));
        using var database = new UsageDatabase(Path.Combine(_stores.Folder("copilot"), "session-store.db"));

        using var store = CreateStore();
        store.Start();
        var session = Assert.Single(store.Current.Sessions);
        AssertStartedNearCreatedAt(session);
        Assert.Equal(CopilotSessionId, session.Content.SessionId);
        // The log has 10 calls, at least as many as the rows committed below.
        Assert.Equal(10, session.Content.Calls.Length);
        Assert.Equal(["no database rows"], session.Unavailable);
        Assert.Empty(store.Current.Problems);

        // The fixture's first row of the core worker, then a made-up second one.
        database.Commit(CopilotSessionId, "2026-10-03T09:34:53.034Z", input: 12069, cacheRead: 0, cacheWrite: 12066, output: 191);

        var first = new TokenUsage(3, 0, 12066, 191);
        await PublishedWithinTwoSeconds(store, s => Single(s).Content.Calls[0].Usage == first);
        Assert.Empty(Single(store.Current).Unavailable);
        Assert.Single(Single(store.Current).Stores.Calls);

        database.Commit(CopilotSessionId, "2026-10-03T09:34:58.500Z", input: 12500, cacheRead: 12066, cacheWrite: 431, output: 120);

        var second = new TokenUsage(3, 12066, 431, 120);
        await PublishedWithinTwoSeconds(store, s => Single(s).Content.Calls[1].Usage == second);
        Assert.Equal(first, Single(store.Current).Content.Calls[0].Usage);
        Assert.Equal(2, Single(store.Current).Stores.Calls.Length);
    }

    private RunStore CreateStore() =>
        AppRunner.CreateStore(_repo.Path, claudeDir: _stores.Folder("claude"), copilotDir: _stores.Folder("copilot"));

    /// <summary>
    /// A running run of one task with one session whose log is <paramref name="log"/>; the run stays running while
    /// the returned run.lock is held.
    /// </summary>
    private FileStream StartRun(string task, string key, IEnumerable<string> log)
    {
        _repo.Write(".orchestrator/tasks.json", $$"""{ "tasks": [ { "id": "{{task}}", "title": "{{task}}" } ] }""");
        _repo.Write(".orchestrator/state.json", $$"""{ "tasks": { "{{task}}": { "status": "running", "mode": "fresh", "attempts": 1 } } }""");
        _repo.Write(".orchestrator/progress.md", "2026-10-04 12:00:00  Run started: 1 tasks, max 1 in parallel\n");
        _repo.Write($".orchestrator/logs/{key}.prompt.md", "Build it.\n");
        _repo.Write($".orchestrator/logs/{key}.events.jsonl", string.Concat(log.Select(line => line + "\n")));
        var runLock = _repo.Write(".orchestrator/run.lock", "");
        return new FileStream(runLock, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>The core worker of the Copilot fixture without its result line, so its log holds no session id.</summary>
    private FileStream StartCopilotRun()
    {
        var log = File.ReadAllLines(CopilotLog);
        Assert.Equal("result", TypeOf(log[^1]));
        return StartRun("core", CopilotKey, log[..^1]);
    }

    /// <summary>
    /// The session folder of <see cref="CopilotSessionId"/> with the events <paramref name="events"/> and a
    /// workspace.yaml that the store's id rule 3 matches: name 'orch:core', the task's worktree folder as cwd and the
    /// fixture's <see cref="CreatedAt"/>. Returns the path of events.jsonl.
    /// </summary>
    private string WriteSessionFolder(IEnumerable<string> events)
    {
        var worktree = Path.Combine(_repo.Path + ".worktrees", "core");
        var folder = $"copilot/session-state/{CopilotSessionId}";
        _stores.Write($"{folder}/workspace.yaml", $"id: {CopilotSessionId}\ncwd: {worktree}\nname: orch:core\ncreated_at: {CreatedAt}\n");
        return _stores.Write($"{folder}/events.jsonl", string.Concat(events.Select(line => line + "\n")));
    }

    /// <summary>The id rule 3 takes a session folder created at most 30 seconds before or after the session's start.</summary>
    private static void AssertStartedNearCreatedAt(Session session)
    {
        Assert.NotNull(session.StartedAt);
        var created = DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture);
        Assert.InRange((session.StartedAt.Value - created).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    /// <summary>Waits until <paramref name="condition"/> holds for the store's snapshot, and asserts it took less than 2 seconds.</summary>
    private static async Task PublishedWithinTwoSeconds(RunStore store, Func<RunSnapshot, bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition(store.Current) && watch.Elapsed < TimeSpan.FromSeconds(4))
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        watch.Stop();

        Assert.True(condition(store.Current), "the change was not published");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    private static Session Single(RunSnapshot s) => Assert.Single(s.Sessions);

    private static string? TypeOf(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;
    }

    private static string? MessageIdOf(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.GetProperty("message").GetProperty("id").GetString();
    }
}

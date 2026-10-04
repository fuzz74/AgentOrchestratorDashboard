using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Core.Store;

namespace OrchDash.Core.Tests.Store;

// Builders for in-memory sessions and provider-store results for the ProviderMerge tests.
// Every call creates new arrays and dictionaries.
public static class MergeData
{
    public const string StartFolder = "20261003-120000";

    // The work folder the tests pass to ProviderMerge.Apply.
    public static string WorkDir(SessionFiles files) => @"C:\work\" + (files.TaskId ?? "repo");

    public static MergeResult Merge(ProviderStores? stores, params Session[] sessions) =>
        ProviderMerge.Apply([.. sessions], WorkDir, stores);

    public static SessionFiles Files(string? taskId, AgentRole role = AgentRole.Worker, int attempt = 1,
        int reviewTry = 0, bool isNudge = false, string? startFolder = StartFolder)
    {
        var name = $"attempt-{attempt}-{role.ToString().ToLowerInvariant()}{(reviewTry > 0 ? "-" + reviewTry : "")}{(isNudge ? "-nudge" : "")}";
        var key = taskId is null ? $"{name}.json" : $"{taskId}/{startFolder}/{name}.json";
        var path = @"C:\repo\.orchestrator\logs\" + key.Replace('/', '\\');
        return new SessionFiles(key, taskId, role, taskId is null ? null : startFolder, attempt, reviewTry, isNudge,
            path, path + ".prompt.md", path + ".events.jsonl", path + ".stderr", false, true, null);
    }

    public static Session SessionOf(Provider provider, SessionFiles files, string? sessionId = null,
        IEnumerable<ModelCall>? calls = null, DateTimeOffset? startedAt = null, SessionInit? init = null) =>
        new(files, provider, SessionState.Running, "Prompt", startedAt,
            new SessionContent(sessionId, "model-a", init, [.. calls ?? []], [], null, startedAt, startedAt, 0));

    public static Session ClaudeSession(SessionFiles files, string? sessionId = "c-1", IEnumerable<ModelCall>? calls = null,
        DateTimeOffset? startedAt = null, SessionInit? init = null) =>
        SessionOf(Provider.Claude, files, sessionId, calls, startedAt, init);

    public static Session CopilotSession(SessionFiles files, string? sessionId = "p-1", IEnumerable<ModelCall>? calls = null,
        DateTimeOffset? startedAt = null) =>
        SessionOf(Provider.Copilot, files, sessionId, calls, startedAt);

    public static Session UnknownSession(SessionFiles files) =>
        new(files, Provider.Unknown, SessionState.Running, "", null, SessionContent.Empty);

    public static SessionInit Init(string? cwd = null, string? cliVersion = null) => new(cwd, "default", cliVersion, [], []);

    public static ModelCall Call(string id, DateTimeOffset? startedAt = null, TokenUsage? usage = null) =>
        new(id, "model-a", startedAt, usage);

    public static CallFigures Figures(string? callId = null, DateTimeOffset? time = null, long input = 10, long? output = 5,
        long? thinking = null, long? nanoAiu = null, TimeSpan? duration = null, string? stopReason = null) =>
        new(callId, time, new TokenUsage(input, 100, 20, output), thinking, nanoAiu, duration, stopReason);

    public static StoreData Stored(string? cliVersion = null, IEnumerable<CallFigures>? calls = null) =>
        StoreData.Empty with
        {
            CliVersion = cliVersion,
            SystemPrompt = ["You are an agent."],
            Calls = [.. calls ?? []],
        };

    public static UsageRows Rows(params (string Id, CallFigures[] Rows)[] bySession) =>
        Rows(null, null, bySession);

    public static UsageRows Rows(int? schemaVersion, string? problem, params (string Id, CallFigures[] Rows)[] bySession) =>
        new(bySession.ToImmutableDictionary(s => s.Id, s => s.Rows.ToImmutableArray(), StringComparer.Ordinal),
            schemaVersion, problem);
}

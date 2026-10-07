using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Timeline;

// Builders for constructed timeline snapshots. Every call creates new arrays.
public static class TimelineData
{
    public const string RepoPath = @"C:\X\Repo";

    public static DateTimeOffset At(int hour, int minute, int second) => new(2026, 10, 3, hour, minute, second, TimeSpan.Zero);

    public static RunSnapshot Snapshot(IEnumerable<ProgressEntry>? progress = null, IEnumerable<Session>? sessions = null) =>
        RunSnapshot.Empty(RepoPath) with { Progress = [.. progress ?? []], Sessions = [.. sessions ?? []] };

    public static ProgressEntry Entry(DateTimeOffset time, string message, string? source = null,
        ProgressKind kind = ProgressKind.Info) =>
        new(time, source, message, kind);

    public static Session SessionOf(string key, DateTimeOffset? startedAt, string? taskId = "alpha",
        AgentRole role = AgentRole.Worker, IEnumerable<ModelCall>? calls = null, IEnumerable<ConversationItem>? items = null,
        SessionResult? result = null, DateTimeOffset? lastEventAt = null)
    {
        var path = @"C:\X\Repo\.orchestrator\logs\" + key.Replace('/', '\\');
        var files = new SessionFiles(key, taskId, role, null, 1, 0, false,
            path, path + ".prompt.md", path + ".events.jsonl", path + ".stderr", result is not null, true, null);
        var content = SessionContent.Empty with
        {
            Calls = [.. calls ?? []],
            Items = [.. items ?? []],
            Result = result,
            LastEventAt = lastEventAt,
        };
        return new Session(files, Provider.Claude, SessionState.Running, "Prompt " + key, startedAt, content);
    }

    public static ModelCall Call(string id, DateTimeOffset? startedAt) => new(id, "claude-sonnet-4-5", startedAt, null);

    public static ToolCall Tool(DateTimeOffset? time, string name = "Bash") =>
        new("c1", time, "t-" + name, name, "{}", name + " summary", null);

    public static AssistantText Text(DateTimeOffset? time, string text = "Hello") => new("c1", time, text);

    public static Thinking Thought(DateTimeOffset? time) => new("c1", time, "Hmm", null);

    public static UserText User(DateTimeOffset? time) => new("c1", time, "tool output", true);

    public static Notice Note(DateTimeOffset? time) => new("c1", time, "info", "notice");

    public static SessionResult Result(bool isError = false) =>
        new(isError, isError ? "error" : "success", "done", null, null, null, 0.1, 3, null, null, null, null, null, null, null);

    public static string[] Keys(ImmutableArray<TimelineEvent> events) => [.. events.Select(e => e.Key)];
}

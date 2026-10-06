using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Processes;

// Hand-built processes, tasks and sessions for the process matching tests.
internal static class ProcessData
{
    public const string SidA = "0f1e2d3c-4b5a-4968-8776-a5b4c3d2e1f0";
    public const string SidB = "11111111-2222-4333-8444-555555555555";

    public static AgentProcess Process(int pid, string commandLine) =>
        new(pid, "claude.exe", commandLine, new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero),
            1_000_000, 0.05, TaskId: null, Role: null, SessionId: null);

    public static TaskView Task(string id, string? sessionId = null) =>
        new(id, "Title " + id, "Prompt", [], [], Acceptance: null, Model: null, Wave: 1, Dependents: 0,
            TaskState.Running, "normal", Attempts: 1, SyncRuns: 0, SpecRejections: 0, CostUsd: 0,
            sessionId, Summary: null, Notes: null, Error: null, Feedback: null,
            StartedAt: null, FinishedAt: null, MergedSha: null, Detail: "");

    public static Session Session(string key, string? taskId, AgentRole role, SessionState state, string? sessionId)
    {
        var files = new SessionFiles(key, taskId, role, StartFolder: null, Attempt: 1, ReviewTry: 0, IsNudge: false,
            ResultPath: @"C:\run\logs\" + key, PromptPath: @"C:\run\logs\" + key + ".prompt",
            EventsPath: @"C:\run\logs\" + key + ".events", StderrPath: @"C:\run\logs\" + key + ".stderr",
            HasResultFile: false, HasEventsFile: false, PromptWrittenAt: null);
        return new Session(files, Provider.Claude, state, "Prompt", StartedAt: null,
            SessionContent.Empty with { SessionId = sessionId });
    }
}

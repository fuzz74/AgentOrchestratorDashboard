using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Tests.Support;

// A running sample run for UI tests. Deterministic: every time is 2026-10-03 at a fixed local wall-clock time.
public static class SampleRun
{
    public const string RepoPath = @"C:\Work\SampleRepo";
    public const string LogsDir = @"C:\Work\SampleRepo\.orchestrator\logs\";

    public const string AlphaWorkerKey = "alpha/20261003-120005/attempt-1-worker.json";
    public const string AlphaReviewKey = "alpha/20261003-120005/attempt-1-review-1.json";
    public const string BetaWorkerKey = "beta/20261003-121000/attempt-1-worker.json";

    public static RunSnapshot Create() => new(
        Version: 1,
        ReadAt: At(12, 30, 0),
        RepoPath: RepoPath,
        Run: new RunInfo(RunPhase.Running, At(12, 0, 0), null, 2, Provider.Claude,
            @"C:\Users\sample\.local\bin\claude.exe", false),
        Plan: new PlanInfo(".orchestrator/spec.md", "main", "orch/integration",
            ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new Dictionary<string, string>
            {
                ["maxAttempts"] = "3",
                ["model"] = "\"sonnet\"",
                ["review"] = "true",
            })),
        Tasks: [Alpha(), Gamma(), Beta(), Delta(), Epsilon()],
        Sessions: [AlphaWorker(), AlphaReview(), BetaWorker()],
        Progress:
        [
            new ProgressEntry(At(12, 0, 0), null, "Run started: 5 tasks, max 2 in parallel", ProgressKind.Info),
            new ProgressEntry(At(12, 9, 30), "alpha", "DONE in 9m25s, 0.25 USD\nmerged as 3f9c2e1", ProgressKind.Success),
            new ProgressEntry(At(12, 20, 0), "gamma", "FAILED after 3 attempts: acceptance failed", ProgressKind.Failure),
        ],
        Problems: ["state.json: The process cannot access the file because it is being used by another process."]);

    // 2026-10-03 at the given local wall-clock time, with the local UTC offset.
    public static DateTimeOffset At(int hour, int minute, int second) =>
        new(new DateTime(2026, 10, 3, hour, minute, second, DateTimeKind.Local));

    private static TaskView Alpha() => new(
        Id: "alpha", Title: "Alpha parser",
        Prompt: "Implement the alpha parser in src/Alpha.\nRead the spec section 2 first.",
        Deps: [], Owns: ["src/Alpha/**", "tests/Alpha/**"],
        Acceptance: "dotnet test tests/Alpha", Model: null, Wave: 1, Dependents: 2,
        Status: TaskState.Done, Mode: "fresh", Attempts: 1, SyncRuns: 0, SpecRejections: 0, CostUsd: 0.25,
        SessionId: "0b7f3c2a-5d41-4e8a-9c11-2f6a8d3e4b70",
        Summary: "Added the alpha parser and its tests.",
        Notes: "Parser.Parse returns null on empty input.",
        Error: null, Feedback: null,
        StartedAt: At(12, 0, 5), FinishedAt: At(12, 9, 30), MergedSha: "3f9c2e1",
        Detail: "0.25 USD, 1 attempt(s)");

    private static TaskView Gamma() => new(
        Id: "gamma", Title: "Gamma formatter",
        Prompt: "Implement the gamma formatter in src/Gamma.",
        Deps: [], Owns: ["src/Gamma/**"],
        Acceptance: "dotnet test tests/Gamma", Model: "opus", Wave: 1, Dependents: 1,
        Status: TaskState.Failed, Mode: "resume", Attempts: 3, SyncRuns: 0, SpecRejections: 1, CostUsd: 0.62,
        SessionId: "6e2d9a14-0c3b-4f57-8a2e-91b4c7d05f36",
        Summary: null, Notes: null,
        Error: "acceptance failed: 2 tests failed\nGammaTests.Formats_empty_input: expected \"\" but was null",
        Feedback: "Handle empty input before formatting.",
        StartedAt: At(12, 0, 5), FinishedAt: At(12, 20, 0), MergedSha: null,
        Detail: "acceptance failed: 2 tests failed");

    private static TaskView Beta() => new(
        Id: "beta", Title: "Beta checker",
        Prompt: "Implement the beta checker on top of the alpha parser.",
        Deps: ["alpha"], Owns: ["src/Beta/**"],
        Acceptance: "dotnet test tests/Beta", Model: null, Wave: 2, Dependents: 1,
        Status: TaskState.Running, Mode: "fresh", Attempts: 1, SyncRuns: 0, SpecRejections: 0, CostUsd: 0,
        SessionId: null, Summary: null, Notes: null, Error: null, Feedback: null,
        StartedAt: At(12, 10, 0), FinishedAt: null, MergedSha: null,
        Detail: "worker (attempt 1)");

    private static TaskView Delta() => new(
        Id: "delta", Title: "Delta report",
        Prompt: "Write the delta report with the gamma formatter.",
        Deps: ["gamma"], Owns: ["src/Delta/**"],
        Acceptance: null, Model: null, Wave: 2, Dependents: 0,
        Status: TaskState.Blocked, Mode: "fresh", Attempts: 0, SyncRuns: 0, SpecRejections: 0, CostUsd: 0,
        SessionId: null, Summary: null, Notes: null, Error: null, Feedback: null,
        StartedAt: null, FinishedAt: null, MergedSha: null,
        Detail: "a dependency failed");

    private static TaskView Epsilon() => new(
        Id: "epsilon", Title: "Epsilon command line",
        Prompt: "Add the epsilon command that runs the beta checker.",
        Deps: ["beta"], Owns: ["src/Epsilon/**"],
        Acceptance: "dotnet run --project src/Epsilon -- --help", Model: null, Wave: 3, Dependents: 0,
        Status: TaskState.Pending, Mode: "fresh", Attempts: 0, SyncRuns: 0, SpecRejections: 0, CostUsd: 0,
        SessionId: null, Summary: null, Notes: null, Error: null, Feedback: null,
        StartedAt: null, FinishedAt: null, MergedSha: null,
        Detail: "waiting for beta");

    private static SessionFiles Files(string key, string taskId, AgentRole role, int reviewTry,
        bool hasResultFile, DateTimeOffset promptWrittenAt)
    {
        var resultPath = LogsDir + key.Replace('/', '\\');
        return new SessionFiles(
            key, taskId, role, StartFolder: key.Split('/')[1], Attempt: 1, reviewTry, IsNudge: false,
            resultPath, resultPath + ".prompt.md", resultPath + ".events.jsonl", resultPath + ".stderr",
            hasResultFile, HasEventsFile: true, promptWrittenAt);
    }

    private static Session AlphaWorker()
    {
        const string model = "claude-sonnet-4-5";
        const string call1 = "msg_01A7alpha";
        const string call2 = "msg_02B8alpha";
        const string summary =
            "Added Parser with Parse and TryParse.\n" +
            "Parse returns null on empty input.\n" +
            "Added six tests in tests/Alpha/ParserTests.cs.\n" +
            "All alpha tests pass.";
        const string notes = "Parser.Parse returns null on empty input.";
        const string structuredJson = """
            {
              "status": "done",
              "summary": "Added Parser with Parse and TryParse.\nParse returns null on empty input.\nAdded six tests in tests/Alpha/ParserTests.cs.\nAll alpha tests pass.",
              "notes_for_dependents": "Parser.Parse returns null on empty input."
            }
            """;

        var content = new SessionContent(
            SessionId: "0b7f3c2a-5d41-4e8a-9c11-2f6a8d3e4b70",
            Model: model,
            Init: new SessionInit(@"C:\Work\SampleRepo.worktrees\alpha", "bypassPermissions", "2.1.3",
                ["Read", "Edit", "Write", "Glob", "Grep", "Bash", "StructuredOutput"], ["github (connected)"]),
            Calls:
            [
                new ModelCall(call1, model, At(12, 0, 10), new TokenUsage(1_200, 15_000, 3_000, null)),
                new ModelCall(call2, model, At(12, 3, 0), new TokenUsage(800, 41_000, 1_500, null)),
            ],
            Items:
            [
                new Thinking(call1, At(12, 0, 10),
                    "The task asks for a parser in src/Alpha.\n" +
                    "The spec says empty input gives null.\n" +
                    "There is a stub Parser class already.\n" +
                    "I will replace it and add tests.", null),
                new AssistantText(call1, At(12, 0, 12), "I will replace the Parser stub in src/Alpha/Parser.cs."),
                new ToolCall(call1, At(12, 0, 15), "toolu_01Edit", "Edit",
                    """{"file_path":"C:\\Work\\SampleRepo.worktrees\\alpha\\src\\Alpha\\Parser.cs","old_string":"public class Parser { }","new_string":"public class Parser\n{\n}"}""",
                    "Edit src/Alpha/Parser.cs",
                    new ToolResult(At(12, 0, 16), false, "The file src/Alpha/Parser.cs has been updated.",
                        "@@ -1,2 +1,4 @@\n namespace Alpha;\n-public class Parser { }\n+public class Parser\n+{\n+}", null)),
                new Thinking(call2, At(12, 3, 0), "", 1_200),
                new ToolCall(call2, At(12, 3, 5), "toolu_02Bash", "Bash",
                    """{"command":"dotnet test tests/Alpha","description":"Run the alpha tests"}""",
                    "Bash dotnet test tests/Alpha",
                    new ToolResult(At(12, 3, 40), true,
                        "Build FAILED.\nsrc/Alpha/Parser.cs(7,5): error CS1002: ; expected", null, null)),
                new AssistantText(call2, At(12, 5, 50), "The parser is in place and all alpha tests pass."),
            ],
            Result: new SessionResult(
                IsError: false, Subtype: "success", Text: "The parser is in place and all alpha tests pass.",
                StructuredJson: structuredJson,
                Worker: new WorkerReport("done", summary, notes, null), Review: null,
                CostUsd: 0.25, Turns: 9, Duration: TimeSpan.FromSeconds(350), ApiDuration: TimeSpan.FromSeconds(290),
                Usage: new TokenUsage(2_000, 56_000, 4_500, 3_100), ContextWindow: 200_000,
                PremiumRequests: null, LinesAdded: null, LinesRemoved: null),
            FirstEventAt: At(12, 0, 10),
            LastEventAt: At(12, 5, 50),
            UnparsedLines: 0);

        return new Session(
            Files(AlphaWorkerKey, "alpha", AgentRole.Worker, 0, hasResultFile: true, At(12, 0, 5)),
            Provider.Claude, SessionState.Succeeded,
            "You are a worker agent of the orchestrator.\n" +
            "Task: alpha - Alpha parser\n" +
            "\n" +
            "Implement the alpha parser in src/Alpha.\n" +
            "Read the spec section 2 first.\n" +
            "\n" +
            "Edit only src/Alpha/** and tests/Alpha/**.\n" +
            "Acceptance: dotnet test tests/Alpha",
            At(12, 0, 10), content);
    }

    private static Session AlphaReview()
    {
        const string model = "gpt-5.1";
        const string turn0 = "0";
        const string turn1 = "1";
        const string structuredJson = """
            {
              "spec_verdict": "pass",
              "quality_verdict": "fail",
              "summary": "The parser meets the spec, but errors are swallowed.",
              "issues": [
                {
                  "severity": "major",
                  "file": "src/Alpha/Parser.cs",
                  "description": "TryParse catches every exception and hides the cause."
                },
                {
                  "severity": "minor",
                  "file": null,
                  "description": "No test covers input with only whitespace."
                }
              ]
            }
            """;

        var content = new SessionContent(
            SessionId: "c41e7b9d-2a6f-4d03-b8e5-7f19a2c6d840",
            Model: model,
            Init: null,
            Calls:
            [
                new ModelCall(turn0, model, At(12, 6, 30), null),
                new ModelCall(turn1, model, At(12, 7, 40), null),
            ],
            Items:
            [
                new Thinking(turn0, At(12, 6, 35), "I need to read the parser and its tests.", null),
                new ToolCall(turn0, At(12, 6, 36), "call_view_1", "view",
                    """{"path":"C:\\Work\\SampleRepo.worktrees\\alpha\\src\\Alpha\\Parser.cs"}""",
                    "view src/Alpha/Parser.cs",
                    new ToolResult(At(12, 6, 37), false, "namespace Alpha;\n\npublic class Parser\n{\n}", null, null)),
                new AssistantText(turn1, At(12, 8, 50), "```json\n" + structuredJson + "\n```"),
            ],
            Result: new SessionResult(
                IsError: false, Subtype: "success", Text: "```json\n" + structuredJson + "\n```",
                StructuredJson: structuredJson,
                Worker: null,
                Review: new ReviewVerdict("pass", "fail", "The parser meets the spec, but errors are swallowed.",
                [
                    new ReviewIssue("major", "src/Alpha/Parser.cs", "TryParse catches every exception and hides the cause."),
                    new ReviewIssue("minor", null, "No test covers input with only whitespace."),
                ]),
                CostUsd: null, Turns: 2, Duration: TimeSpan.FromSeconds(145), ApiDuration: TimeSpan.FromSeconds(98),
                Usage: null, ContextWindow: null, PremiumRequests: 1, LinesAdded: 0, LinesRemoved: 0),
            FirstEventAt: At(12, 6, 30),
            LastEventAt: At(12, 8, 55),
            UnparsedLines: 2);

        return new Session(
            Files(AlphaReviewKey, "alpha", AgentRole.Reviewer, 1, hasResultFile: true, At(12, 6, 25)),
            Provider.Copilot, SessionState.Succeeded,
            "You are a reviewer agent of the orchestrator.\nReview task alpha against its spec and for code quality.",
            At(12, 6, 30), content);
    }

    private static Session BetaWorker()
    {
        const string model = "claude-sonnet-4-5";
        const string call1 = "msg_01C9beta";
        const string call2 = "msg_02D0beta";

        var content = new SessionContent(
            SessionId: "9a3c5e71-8b2d-4f60-a4c9-1d7e0b2f6a58",
            Model: model,
            Init: new SessionInit(@"C:\Work\SampleRepo.worktrees\beta", "bypassPermissions", "2.1.3",
                ["Read", "Edit", "Write", "Glob", "Grep", "Bash", "StructuredOutput"], []),
            Calls:
            [
                new ModelCall(call1, model, At(12, 10, 5), new TokenUsage(1_100, 14_800, 2_900, null)),
                new ModelCall(call2, model, At(12, 20, 0), new TokenUsage(600, 33_000, 900, null)),
            ],
            Items:
            [
                new Thinking(call1, At(12, 10, 5), "Beta builds on the alpha parser.\nI should look at its API first.", null),
                new AssistantText(call1, At(12, 10, 8), "Let me read the alpha parser first."),
                new ToolCall(call1, At(12, 10, 10), "toolu_01Read", "Read",
                    """{"file_path":"C:\\Work\\SampleRepo.worktrees\\beta\\src\\Alpha\\Parser.cs"}""",
                    "Read src/Alpha/Parser.cs",
                    new ToolResult(At(12, 10, 11), false, "namespace Alpha;\n\npublic class Parser\n{\n}", null, null)),
                new ToolCall(call1, At(12, 12, 0), "toolu_02Grep", "Grep",
                    """{"pattern":"Parse\\(","path":"C:\\Work\\SampleRepo.worktrees\\beta\\src"}""",
                    "Grep Parse\\( in src",
                    new ToolResult(At(12, 12, 1), false, "src/Alpha/Parser.cs", null, null)),
                new AssistantText(call2, At(12, 20, 0), "Now I will write the checker and build it."),
                new ToolCall(call2, At(12, 29, 30), "toolu_03Bash", "Bash",
                    """{"command":"dotnet build src/Beta"}""",
                    "Bash dotnet build src/Beta",
                    null),
            ],
            Result: null,
            FirstEventAt: At(12, 10, 5),
            LastEventAt: At(12, 29, 30),
            UnparsedLines: 0);

        return new Session(
            Files(BetaWorkerKey, "beta", AgentRole.Worker, 0, hasResultFile: false, At(12, 10, 0)),
            Provider.Claude, SessionState.Running,
            "You are a worker agent of the orchestrator.\nTask: beta - Beta checker\n\nImplement the beta checker on top of the alpha parser.",
            At(12, 10, 5), content);
    }
}

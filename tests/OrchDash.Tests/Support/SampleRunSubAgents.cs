using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Tests.Support;

// CreateEnriched() with sub-agents (4.4): two foreground ones under the alpha worker, a background one that still runs
// under the beta worker, and a new Copilot planner whose three sub-agents nest. The UI tests rely on these ids, names
// and states.
public static partial class SampleRun
{
    public const string PlannerKey = "planner-20261003-115900-1.json";

    public const string AlphaSub1Id = "toolu_alpha_sub1";
    public const string AlphaSub2Id = "toolu_alpha_sub2";
    public const string BetaSub1Id = "toolu_beta_sub1";
    public const string PlannerSub1Id = "agent-p1";
    public const string PlannerSub2Id = "agent-p2";
    public const string PlannerSub3Id = "agent-p3";

    private const string HaikuModel = "claude-haiku-4-5";
    private const string PlannerModel = "gpt-5.6-luna";

    public static RunSnapshot CreateSubAgents()
    {
        var run = CreateEnriched();
        return run with
        {
            Sessions =
            [
                Planner(),
                .. run.Sessions.Select(session => session.Files.Key switch
                {
                    AlphaWorkerKey => WithAlphaSubAgents(session),
                    BetaWorkerKey => WithBetaSubAgent(session),
                    _ => session,
                }),
            ],
            Progress =
            [
                new ProgressEntry(At(11, 59, 30), "planner", "Glob **/*", ProgressKind.Activity) { SubAgent = "Map the repo" },
                .. run.Progress,
            ],
        };
    }

    // The first call starts "Survey the parser module", which succeeds; the second starts the API check, which fails.
    private static Session WithAlphaSubAgents(Session session)
    {
        const string ownCall1 = "msg_01A7alpha";
        const string ownCall2 = "msg_02B8alpha";
        const string sub1Call1 = "msg_01S1alpha";
        const string sub1Call2 = "msg_02S1alpha";
        const string sub2Call = "msg_01S2alpha";
        const string sub2Model = "claude-sonnet-4-5";
        const string report1 = "The parser module has 3 files.";
        const string error2 = "API Error: 529 Overloaded. The sub-agent stopped before it finished.";

        var sub1 = NewSubAgent(AlphaSub1Id, null, AlphaSub1Id, "Survey the parser module", "Explore", HaikuModel,
            background: false, "List the files in src/Alpha and say what each one holds.",
            At(12, 0, 17), At(12, 0, 58), SessionState.Succeeded, report1);
        var sub2 = NewSubAgent(AlphaSub2Id, null, AlphaSub2Id, "Check the public API surface of the alpha parser",
            "general-purpose", sub2Model, background: false, "Compare the public API of src/Alpha with section 2 of the spec.",
            At(12, 3, 6), At(12, 3, 50), SessionState.Failed, error2);

        // Sub-agent 1 has transcript figures (36.1), so its calls carry the output; sub-agent 2 has none.
        ImmutableArray<ModelCall> sub1Calls =
        [
            new ModelCall(sub1Call1, HaikuModel, At(12, 0, 19), new TokenUsage(3, 0, 4_000, 120))
                { StopReason = "tool_use", AgentId = AlphaSub1Id },
            new ModelCall(sub1Call2, HaikuModel, At(12, 0, 50), new TokenUsage(2, 4_000, 1_350, 260))
                { StopReason = "end_turn", AgentId = AlphaSub1Id },
        ];

        var content = session.Content;
        var items = content.Items;   // call 1: thinking, text, Edit; call 2: thinking, Bash; then the final text
        return session with
        {
            Content = content with
            {
                Calls =
                [
                    content.Calls[0],
                    .. sub1Calls,
                    content.Calls[1],
                    new ModelCall(sub2Call, sub2Model, At(12, 3, 9), new TokenUsage(3, 0, 9_800, null))
                        { StopReason = "tool_use", AgentId = AlphaSub2Id },
                ],
                Items =
                [
                    .. items[..3],
                    new ToolCall(ownCall1, At(12, 0, 17), AlphaSub1Id, "Agent", AgentInput(sub1), "Agent Survey the parser module",
                        new ToolResult(At(12, 1, 0), false, report1, null, null)),
                    .. Tagged(AlphaSub1Id,
                        new ToolCall(sub1Call1, At(12, 0, 21), "toolu_01S1Glob", "Glob",
                            """{"pattern":"src/Alpha/**"}""",
                            "Glob src/Alpha/**",
                            new ToolResult(At(12, 0, 22), false, "src/Alpha/Lexer.cs\nsrc/Alpha/Node.cs\nsrc/Alpha/Parser.cs", null, null)),
                        new ToolCall(sub1Call1, At(12, 0, 21), "toolu_02S1Read", "Read",
                            """{"file_path":"C:\\Work\\SampleRepo.worktrees\\alpha\\src\\Alpha\\Parser.cs"}""",
                            "Read src/Alpha/Parser.cs",
                            new ToolResult(At(12, 0, 22), false, "namespace Alpha;\n\npublic class Parser { }", null, null)),
                        new AssistantText(sub1Call2, At(12, 0, 58), report1)),
                    .. items[3..5],
                    new ToolCall(ownCall2, At(12, 3, 6), AlphaSub2Id, "Agent", AgentInput(sub2),
                        "Agent Check the public API surface of the alpha parser",
                        new ToolResult(At(12, 3, 51), true, error2, null, null)),
                    .. Tagged(AlphaSub2Id,
                        new ToolCall(sub2Call, At(12, 3, 12), "toolu_01S2Grep", "Grep",
                            """{"pattern":"public (class|static)","path":"C:\\Work\\SampleRepo.worktrees\\alpha\\src\\Alpha"}""",
                            "Grep public (class|static) in src/Alpha",
                            new ToolResult(At(12, 3, 13), false, "src/Alpha/Parser.cs:3:public class Parser { }", null, null))),
                    items[5],
                ],
                SubAgents = [sub1, sub2],
            },
            Stores = session.Stores with { SubAgents = StoreData.NoSubAgents.Add(AlphaSub1Id, AlphaSub1Stores(sub1Calls)) },
        };
    }

    // The transcript of the alpha worker's first sub-agent (36.1): exact figures, its system prompt and its two tools.
    private static StoreData AlphaSub1Stores(ImmutableArray<ModelCall> calls) => StoreData.Empty with
    {
        CliVersion = "2.1.3",
        SystemPrompt =
        [
            "You are Claude Code, Anthropic's official CLI for Claude.",
            "You are a file search specialist. Use Glob, Grep and Read to answer the question, then report what you found.",
        ],
        Tools =
        [
            new ToolDefinition("Glob", "Fast file pattern matching that works with any codebase size.", """
                {
                  "type": "object",
                  "properties": {
                    "pattern": {
                      "type": "string",
                      "description": "The glob pattern to match files against"
                    }
                  },
                  "required": [
                    "pattern"
                  ]
                }
                """),
            new ToolDefinition("Read", "Reads a file from the local filesystem.", """
                {
                  "type": "object",
                  "properties": {
                    "file_path": {
                      "type": "string",
                      "description": "The absolute path to the file to read"
                    }
                  },
                  "required": [
                    "file_path"
                  ]
                }
                """),
        ],
        Calls = [.. calls.Select(c => new CallFigures(c.Id, c.StartedAt, c.Usage!, c.ThinkingTokens, null, null, c.StopReason))],
    };

    // The second call starts "Survey CLI flags" in the background; it still runs, and its Read has no result yet.
    private static Session WithBetaSubAgent(Session session)
    {
        const string ownCall2 = "msg_02D0beta";
        const string subCall = "msg_01S1beta";

        var sub = NewSubAgent(BetaSub1Id, null, BetaSub1Id, "Survey CLI flags", "Explore", HaikuModel,
            background: true, "List the command-line flags that src/Beta reads and what each one does.",
            At(12, 29, 10), null, SessionState.Running, null);

        var content = session.Content;
        var items = content.Items;   // call 1: thinking, text, Read, Grep; call 2: text, then the running Bash
        return session with
        {
            Content = content with
            {
                Calls =
                [
                    .. content.Calls,
                    new ModelCall(subCall, HaikuModel, At(12, 29, 12), new TokenUsage(3, 0, 3_600, null))
                        { StopReason = "tool_use", AgentId = BetaSub1Id },
                ],
                Items =
                [
                    .. items[..5],
                    new ToolCall(ownCall2, At(12, 29, 10), BetaSub1Id, "Agent", AgentInput(sub), "Agent Survey CLI flags", null),
                    .. Tagged(BetaSub1Id,
                        new ToolCall(subCall, At(12, 29, 15), "toolu_01S1Read", "Read",
                            """{"file_path":"C:\\Work\\SampleRepo.worktrees\\beta\\src\\Beta\\Program.cs"}""",
                            "Read src/Beta/Program.cs",
                            null)),
                    items[5],
                ],
                SubAgents = [sub],
            },
        };
    }

    // A Copilot planner whose sync sub-agents run one after the other; "Read the spec" runs inside "Map the repo".
    // Each sub-agent numbers its turns from "0" again (35.4), so a call is known by its AgentId and turn id together.
    private static Session Planner()
    {
        const string prompt =
            "You are the planner of the orchestrator.\n" +
            "Split .orchestrator/spec.md into tasks with their dependencies.\n" +
            "Write the plan to .orchestrator/plan.json.";
        const string report1 = "Three folders: src, tests and docs.";
        const string report2 = "The spec asks for five tasks: alpha, beta, gamma, delta and epsilon.";
        const string report3 = "One test project, tests/Sample.Tests, with a single smoke test.";
        const string answer = "The plan has five tasks in three waves: alpha and gamma, then beta and delta, then epsilon.";

        var sub1 = NewSubAgent(PlannerSub1Id, null, "call-p1", "Map the repo", "explore", PlannerModel,
            background: false, "Map the folders of the repo and say what each one holds.",
            At(11, 59, 7), At(11, 59, 35), SessionState.Succeeded, report1);
        var sub2 = NewSubAgent(PlannerSub2Id, PlannerSub1Id, "call-p2", "Read the spec", "explore", PlannerModel,
            background: false, "Read .orchestrator/spec.md and list the tasks it asks for.",
            At(11, 59, 11), At(11, 59, 25), SessionState.Succeeded, report2);
        var sub3 = NewSubAgent(PlannerSub3Id, null, "call-p3", "Survey the tests", "explore", PlannerModel,
            background: false, "Find the test projects and say what they cover.",
            At(11, 59, 39), At(11, 59, 51), SessionState.Succeeded, report3);

        ImmutableArray<ModelCall> calls =
        [
            PlannerCall("0", null, At(11, 59, 2), new TokenUsage(30, 0, 12_400, 380), 120, 1_420_000_000, 3_800, "tool_calls"),
            PlannerCall("0", PlannerSub1Id, At(11, 59, 8), new TokenUsage(12, 0, 6_800, 210), 64, 310_000_000, 2_600, "tool_calls"),
            PlannerCall("0", PlannerSub2Id, At(11, 59, 12), new TokenUsage(10, 0, 5_900, 180), 48, 270_000_000, 2_100, "tool_calls"),
            PlannerCall("0", PlannerSub3Id, At(11, 59, 40), new TokenUsage(11, 0, 6_100, 190), 52, 290_000_000, 2_300, "tool_calls"),
            PlannerCall("1", null, At(11, 59, 54), new TokenUsage(25, 12_400, 3_350, 420), 260, 1_910_000_000, 4_200, "stop"),
        ];

        ImmutableArray<ConversationItem> items =
        [
            new AssistantText("0", At(11, 59, 4), "I will map the repo first, then survey the tests."),
            new ToolCall("0", At(11, 59, 6), "call-p1", "task", TaskArguments(sub1), "task Map the repo",
                new ToolResult(At(11, 59, 36), false, report1, null, null)),
            .. Tagged(PlannerSub1Id,
                new ToolCall("0", At(11, 59, 10), "call-p2", "task", TaskArguments(sub2), "task Read the spec",
                    new ToolResult(At(11, 59, 26), false, report2, null, null))),
            .. Tagged(PlannerSub2Id,
                new ToolCall("0", At(11, 59, 14), "call-p2-view", "view",
                    """{"path":"C:\\Work\\SampleRepo\\.orchestrator\\spec.md"}""",
                    "view .orchestrator/spec.md",
                    new ToolResult(At(11, 59, 14), false, "# Sample spec\n\n## 2. Tasks\n\nalpha, beta, gamma, delta and epsilon.", null, null)),
                new AssistantText("0", At(11, 59, 24), report2)),
            .. Tagged(PlannerSub1Id,
                new ToolCall("0", At(11, 59, 29), "call-p1-glob", "glob", """{"pattern":"**/*"}""", "glob **/*",
                    new ToolResult(At(11, 59, 30), false,
                        "src/Sample/Sample.csproj\ntests/Sample.Tests/Sample.Tests.csproj\ndocs/README.md", null, null)),
                new AssistantText("0", At(11, 59, 34), report1)),
            new ToolCall("0", At(11, 59, 38), "call-p3", "task", TaskArguments(sub3), "task Survey the tests",
                new ToolResult(At(11, 59, 52), false, report3, null, null)),
            .. Tagged(PlannerSub3Id,
                new ToolCall("0", At(11, 59, 43), "call-p3-glob", "glob", """{"pattern":"tests/**/*.cs"}""", "glob tests/**/*.cs",
                    new ToolResult(At(11, 59, 43), false, "tests/Sample.Tests/SmokeTests.cs", null, null)),
                new AssistantText("0", At(11, 59, 50), report3)),
            new AssistantText("1", At(11, 59, 57), answer),
        ];

        var content = new SessionContent(
            SessionId: "5c1e9f30-7a2b-4d84-b6e1-0f3a9c2d7e51",
            Model: PlannerModel,
            Init: null,
            Calls: calls,
            Items: items,
            Result: new SessionResult(
                IsError: false, Subtype: "success", Text: answer, StructuredJson: null,
                Worker: null, Review: null,
                CostUsd: null, Turns: 2, Duration: TimeSpan.FromSeconds(58), ApiDuration: TimeSpan.FromSeconds(15),
                Usage: null, ContextWindow: null, PremiumRequests: 1, LinesAdded: 0, LinesRemoved: 0),
            FirstEventAt: At(11, 59, 0),
            LastEventAt: At(11, 59, 58),
            UnparsedLines: 0)
        {
            SubAgents = [sub1, sub2, sub3],
        };

        // The usage rows of every call in query order, each with its agent_id and parent_tool_call_id (36.2).
        var stores = StoreData.Empty with
        {
            CliVersion = TestedVersions.CopilotCli,
            SystemPrompt = ["You are the GitHub Copilot CLI, a terminal assistant built by GitHub."],
            Calls =
            [
                .. calls.Select(c => new CallFigures(null, c.StartedAt, c.Usage!, c.ThinkingTokens, c.NanoAiu, c.Duration, c.StopReason)
                {
                    AgentId = c.AgentId,
                    ParentToolCallId = SubAgents.Find(content, c.AgentId)?.ToolCallId,
                }),
            ],
        };

        return new Session(PlannerFiles(), Provider.Copilot, SessionState.Succeeded, prompt, At(11, 59, 0), content)
        {
            Stores = stores,
        };
    }

    // Files() takes the start folder from the key, which a planner's key does not have.
    private static SessionFiles PlannerFiles()
    {
        var resultPath = LogsDir + PlannerKey;
        return new SessionFiles(
            PlannerKey, TaskId: null, AgentRole.Planner, StartFolder: null, Attempt: 1, ReviewTry: 0, IsNudge: false,
            resultPath, resultPath + ".prompt.md", resultPath + ".events.jsonl", resultPath + ".stderr",
            HasResultFile: true, HasEventsFile: true, PromptWrittenAt: At(11, 58, 55));
    }

    private static SubAgent NewSubAgent(string id, string? parentId, string toolCallId, string description,
        string agentType, string model, bool background, string prompt, DateTimeOffset startedAt,
        DateTimeOffset? finishedAt, SessionState state, string? report) =>
        new(id, parentId, toolCallId, SubAgents.Name(description), description, agentType, model, background, prompt,
            startedAt, finishedAt, state, report);

    private static ModelCall PlannerCall(string turnId, string? agentId, DateTimeOffset startedAt, TokenUsage usage,
        long thinkingTokens, long nanoAiu, int durationMs, string stopReason) =>
        new(turnId, PlannerModel, startedAt, usage)
        {
            ThinkingTokens = thinkingTokens,
            NanoAiu = nanoAiu,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            StopReason = stopReason,
            AgentId = agentId,
        };

    // The input of the Claude Agent tool call that starts the sub-agent (35.1).
    private static string AgentInput(SubAgent sub) =>
        $$"""{"description":"{{sub.Description}}","prompt":"{{sub.Prompt}}","subagent_type":"{{sub.AgentType}}","run_in_background":{{(sub.Background ? "true" : "false")}}}""";

    // The arguments of the Copilot task tool call that starts the sync sub-agent (35.4).
    private static string TaskArguments(SubAgent sub) =>
        $$"""{"description":"{{sub.Description}}","prompt":"{{sub.Prompt}}","agent_type":"{{sub.AgentType}}","mode":"sync"}""";

    private static IEnumerable<ConversationItem> Tagged(string agentId, params ConversationItem[] items) =>
        items.Select(item => item with { AgentId = agentId });
}

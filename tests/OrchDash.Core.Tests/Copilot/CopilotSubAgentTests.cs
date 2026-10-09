using OrchDash.Core.Copilot;
using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Copilot.CopilotEvents;

namespace OrchDash.Core.Tests.Copilot;

/// <summary>Sub-agents in the Copilot stream (spec 35.4-35.7).</summary>
public sealed class CopilotSubAgentTests
{
    private const string P1 = "agent-p1";
    private const string P2 = "agent-p2";

    /// <summary>The time of a spec 4.3 sample line, written at 10:00:<paramref name="second"/> UTC.</summary>
    private static DateTimeOffset SampleAt(double second) =>
        new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero).AddSeconds(second);

    [Fact]
    public void Spec_sample_lines_give_a_finished_sub_agent_with_its_call_and_items()
    {
        var content = Parse(
            """{"type":"tool.execution_start","data":{"toolCallId":"call-sub","toolName":"task","arguments":{"description":"Map the repo","prompt":"Map the folders.","agent_type":"explore","mode":"sync"}},"timestamp":"2026-10-09T10:00:00.000Z"}""",
            """{"type":"subagent.started","agentId":"agent-p1","data":{"toolCallId":"call-sub","agentDescription":"Map the repo","agentDisplayName":"map","agentType":"explore","model":"gpt-5.6-luna","executionMode":"sync"},"timestamp":"2026-10-09T10:00:01.000Z"}""",
            """{"type":"assistant.turn_start","agentId":"agent-p1","data":{"turnId":"0"},"timestamp":"2026-10-09T10:00:02.000Z"}""",
            """{"type":"tool.execution_start","agentId":"agent-p1","data":{"toolCallId":"call-glob","toolName":"glob","parentToolCallId":"call-sub","arguments":{"pattern":"**/*"}},"timestamp":"2026-10-09T10:00:03.000Z"}""",
            """{"type":"assistant.message","agentId":"agent-p1","data":{"turnId":"0","phase":"final_answer","content":"Three folders: src, tests, docs."},"timestamp":"2026-10-09T10:00:08.000Z"}""",
            """{"type":"subagent.completed","agentId":"agent-p1","data":{"toolCallId":"call-sub"},"timestamp":"2026-10-09T10:00:09.000Z"}""",
            """{"type":"tool.execution_complete","data":{"toolCallId":"call-sub","success":true,"result":{"content":"Three folders: src, tests, docs."}},"timestamp":"2026-10-09T10:00:09.500Z"}""");

        Assert.Equal(
            [new SubAgent(P1, null, "call-sub", "Map the repo", "Map the repo", "explore", "gpt-5.6-luna", false,
                "Map the folders.", SampleAt(1), SampleAt(9), SessionState.Succeeded, "Three folders: src, tests, docs.")],
            content.SubAgents);
        Assert.Equal([new ModelCall("0", null, SampleAt(2), null) { AgentId = P1 }], content.Calls);
        Assert.Collection(content.Items,
            item =>
            {
                var task = Assert.IsType<ToolCall>(item);
                Assert.Null(task.AgentId);
                Assert.Equal("call-sub", task.ToolId);
                Assert.Equal(new ToolResult(SampleAt(9.5), false, "Three folders: src, tests, docs.", null, null), task.Result);
            },
            item =>
            {
                var glob = Assert.IsType<ToolCall>(item);
                Assert.Equal((P1, "call-glob"), (glob.AgentId, glob.ToolId));
            },
            item => Assert.Equal(new AssistantText("0", SampleAt(8), "Three folders: src, tests, docs.") { AgentId = P1 }, item));
        Assert.Null(content.Model);
        Assert.Equal(0, content.UnparsedLines);
    }

    [Fact]
    public void A_sub_agent_event_tags_the_calls_and_items_it_adds()
    {
        var content = Parse(
            TurnStart("0", second: 1),
            Message("0", content: "Delegating.", second: 2,
                toolRequests: ToolRequest("call-sub", "task", TaskArguments("Map the repo", "Map the folders."))),
            TurnStart("0", second: 3, agentId: P1),
            Message("0", content: "Looking.", reasoning: "Plan.", model: "gpt-5.6-luna", second: 4, agentId: P1,
                toolRequests: ToolRequest("call-view", "view", """{"path":"a.cs"}""")),
            ToolStart("call-glob", "glob", """{"pattern":"**/*"}""", second: 5, agentId: P1));

        Assert.Equal(
            [
                new ModelCall("0", "gpt-6-sol", At(1), null),
                new ModelCall("0", "gpt-5.6-luna", At(3), null) { AgentId = P1 },
            ],
            content.Calls);
        Assert.Collection(content.Items,
            item => Assert.Equal(new AssistantText("0", At(2), "Delegating."), item),
            item =>
            {
                Assert.Null(item.AgentId);
                Assert.Equal("call-sub", Assert.IsType<ToolCall>(item).ToolId);
            },
            item => Assert.Equal(new Thinking("0", At(4), "Plan.", null) { AgentId = P1 }, item),
            item => Assert.Equal(new AssistantText("0", At(4), "Looking.") { AgentId = P1 }, item),
            item => Assert.Equal(
                new ToolCall("0", At(4), "call-view", "view", """{"path":"a.cs"}""", "view a.cs", null) { AgentId = P1 }, item),
            item =>
            {
                var glob = Assert.IsType<ToolCall>(item);
                Assert.Equal((P1, "call-glob"), (glob.AgentId, glob.ToolId));
                Assert.Equal(At(5), glob.Time);
            });
    }

    [Fact]
    public void A_turn_id_is_looked_up_within_its_agent()
    {
        var content = Parse(
            TurnStart("0", second: 1),
            TurnStart("0", second: 2, agentId: P1),
            Message("0", model: "gpt-6-sol", second: 3),
            Message("0", model: "gpt-5.6-luna", second: 4, agentId: P1),
            TurnStart("0", second: 5, agentId: P2),
            Message("0", model: "claude-haiku-4.5", second: 6, agentId: P2));

        Assert.Equal(
            [
                new ModelCall("0", "gpt-6-sol", At(1), null),
                new ModelCall("0", "gpt-5.6-luna", At(2), null) { AgentId = P1 },
                new ModelCall("0", "claude-haiku-4.5", At(5), null) { AgentId = P2 },
            ],
            content.Calls);
    }

    [Fact]
    public void An_event_of_an_unknown_agent_id_adds_a_running_sub_agent()
    {
        var content = Parse(
            TurnStart("0", second: 1),
            TurnStart("0", second: 4, agentId: "agent-x"),
            Message("0", content: "Working.", second: 5, agentId: "agent-x"));

        Assert.Equal(
            [new SubAgent("agent-x", null, "", "sub-agent", null, null, null, false, "", At(4), null, SessionState.Running, null)],
            content.SubAgents);
    }

    [Fact]
    public void An_empty_agent_id_counts_as_the_agent_own()
    {
        var content = Parse(
            TurnStart("0", second: 1, agentId: ""),
            Message("0", content: "Done.", phase: "final_answer", second: 2, agentId: ""),
            Result(exitCode: 0, second: 3));

        Assert.Null(Assert.Single(content.Calls).AgentId);
        Assert.Null(Assert.Single(content.Items).AgentId);
        Assert.Empty(content.SubAgents);
        Assert.Equal("gpt-6-sol", content.Model);
        Assert.Equal("Done.", content.Result?.Text);
        Assert.Equal(1, content.Result?.Turns);
    }

    [Fact]
    public void Started_takes_every_field_from_its_data()
    {
        var content = Parse(
            TaskStart("call-sub", "The arguments' description", "Map the folders.", "general-purpose", "background", second: 1),
            SubagentStarted(P1, "call-sub", "Map the repo", "explore", "gpt-5.6-luna", "sync", displayName: "map", second: 2));

        Assert.Equal(
            [new SubAgent(P1, null, "call-sub", "Map the repo", "Map the repo", "explore", "gpt-5.6-luna", false,
                "Map the folders.", At(2), null, SessionState.Running, null)],
            content.SubAgents);
    }

    [Fact]
    public void Started_falls_back_to_the_arguments_of_its_starting_call()
    {
        const string description = "Survey the tests of the alpha parser module and list the gaps";
        var content = Parse(
            TurnStart("0", second: 1),
            Message("0", second: 1, toolRequests:
                ToolRequest("call-sub", "task", TaskArguments(description, "List the test files.", "explore", "background"))),
            SubagentStarted(P1, "call-sub", second: 3));

        Assert.Equal(
            [new SubAgent(P1, null, "call-sub", "Survey the tests of the alpha…", description, "explore", null, true,
                "List the test files.", At(3), null, SessionState.Running, null)],
            content.SubAgents);
    }

    [Fact]
    public void Started_without_a_known_starting_call_has_no_prompt_or_parent()
    {
        var content = Parse(SubagentStarted(P1, "call-unknown", "Map the repo", second: 2));

        Assert.Equal(
            [new SubAgent(P1, null, "call-unknown", "Map the repo", "Map the repo", null, null, false, "", At(2), null,
                SessionState.Running, null)],
            content.SubAgents);
    }

    [Fact]
    public void Sub_agents_keep_the_order_of_first_appearance()
    {
        var content = Parse(
            TurnStart("0", second: 1, agentId: "agent-b"),
            TaskStart("call-a", "Read the spec", second: 2),
            SubagentStarted("agent-a", "call-a", second: 3),
            TaskStart("call-b", "Map the repo", second: 4),
            SubagentStarted("agent-b", "call-b", second: 5),
            Message("0", content: "Done.", second: 6, agentId: "agent-c"));

        Assert.Equal(new[] { "agent-b", "agent-a", "agent-c" }, content.SubAgents.Select(sub => sub.Id));
        Assert.Equal(new[] { "Map the repo", "Read the spec", "sub-agent" }, content.SubAgents.Select(sub => sub.Name));
        Assert.Equal(At(5), content.SubAgents[0].StartedAt);
    }

    [Fact]
    public void A_nested_sub_agent_has_the_sub_agent_whose_call_started_it_as_parent()
    {
        var content = Parse(
            TaskStart("call-p1", "Map the repo", second: 1),
            SubagentStarted(P1, "call-p1", second: 2),
            TaskStart("call-p2", "Read the spec", second: 3, agentId: P1),
            SubagentStarted(P2, "call-p2", second: 4));

        Assert.Equal(new string?[] { null, P1 }, content.SubAgents.Select(sub => sub.ParentId));
        Assert.Equal("call-p2", content.SubAgents[1].ToolCallId);
    }

    [Theory]
    [InlineData("sync", null, false)]
    [InlineData("background", null, true)]
    [InlineData("sync", "background", false)]
    [InlineData(null, "background", true)]
    [InlineData(null, "sync", false)]
    [InlineData(null, null, false)]
    public void Background_comes_from_the_execution_mode_else_the_call_mode(string? executionMode, string? mode,
        bool background)
    {
        var content = Parse(
            TaskStart("call-sub", "Map the repo", mode: mode, second: 1),
            SubagentStarted(P1, "call-sub", executionMode: executionMode, second: 2));

        Assert.Equal(background, Assert.Single(content.SubAgents).Background);
    }

    [Theory]
    [InlineData("subagent.completed", SessionState.Succeeded)]
    [InlineData("subagent.failed", SessionState.Failed)]
    public void Finish_event_names_the_sub_agent_by_its_agent_id(string type, SessionState state)
    {
        var content = Parse(
            TaskStart("call-a", "Map the repo", second: 1),
            SubagentStarted("agent-a", "call-a", second: 2),
            TaskStart("call-b", "Read the spec", second: 3),
            SubagentStarted("agent-b", "call-b", second: 4),
            Event(type, """{"toolCallId":"call-b"}""", second: 9, agentId: "agent-a"));

        Assert.Equal((state, At(9)), (content.SubAgents[0].State, content.SubAgents[0].FinishedAt!.Value));
        Assert.Equal(SessionState.Running, content.SubAgents[1].State);
    }

    [Theory]
    [InlineData("subagent.completed", SessionState.Succeeded)]
    [InlineData("subagent.failed", SessionState.Failed)]
    public void Finish_event_without_an_agent_id_names_the_sub_agent_by_its_starting_call(string type, SessionState state)
    {
        var content = Parse(
            TaskStart("call-a", "Map the repo", second: 1),
            SubagentStarted("agent-a", "call-a", second: 2),
            TaskStart("call-b", "Read the spec", second: 3),
            SubagentStarted("agent-b", "call-b", second: 4),
            Event(type, """{"toolCallId":"call-b"}""", second: 9));

        Assert.Equal(SessionState.Running, content.SubAgents[0].State);
        Assert.Equal((state, At(9)), (content.SubAgents[1].State, content.SubAgents[1].FinishedAt!.Value));
    }

    [Fact]
    public void Finish_event_for_an_unknown_or_finished_sub_agent_changes_nothing()
    {
        var parser = new CopilotSessionParser(WorkDir);
        parser.AddLine(TaskStart("call-sub", "Map the repo", second: 1));
        parser.AddLine(SubagentStarted(P1, "call-sub", second: 2));
        parser.AddLine(SubagentCompleted(P1, second: 3));
        var before = parser.Build();

        parser.AddLine(SubagentFailed(P1, second: 3));
        parser.AddLine(SubagentFailed(null, "call-sub", second: 3));
        parser.AddLine(SubagentCompleted("agent-unknown", second: 3));
        parser.AddLine(SubagentCompleted(null, "call-unknown", second: 3));
        parser.AddLine(SubagentCompleted(null, second: 3));

        Assert.Same(before, parser.Build());
        var sub = Assert.Single(before.SubAgents);
        Assert.Equal((SessionState.Succeeded, At(3)), (sub.State, sub.FinishedAt!.Value));
    }

    [Fact]
    public void Finish_event_that_changes_nothing_still_moves_the_last_event_time()
    {
        var parser = new CopilotSessionParser(WorkDir);
        parser.AddLine(TaskStart("call-sub", "Map the repo", second: 1));
        parser.AddLine(SubagentStarted(P1, "call-sub", second: 2));
        var before = parser.Build();

        parser.AddLine(SubagentCompleted("agent-unknown", second: 7));
        var after = parser.Build();

        Assert.NotSame(before, after);
        Assert.Equal(At(7), after.LastEventAt);
        Assert.Equal(before.SubAgents, after.SubAgents);
    }

    [Theory]
    [InlineData(""" "success":true,"result":{"content":"Three folders."} """, SessionState.Succeeded, "Three folders.")]
    [InlineData(""" "success":false,"error":{"message":"Model overloaded"} """, SessionState.Failed,
        """{"message":"Model overloaded"}""")]
    [InlineData(""" "success":true """, SessionState.Succeeded, null)]
    public void Completion_of_its_starting_call_finishes_a_foreground_sub_agent(string fields, SessionState state,
        string? report)
    {
        var content = Parse(
            TaskStart("call-sub", "Map the repo", mode: "sync", second: 1),
            SubagentStarted(P1, "call-sub", executionMode: "sync", second: 2),
            ToolComplete("call-sub", fields, second: 9));

        var sub = Assert.Single(content.SubAgents);
        Assert.Equal((state, At(9), report), (sub.State, sub.FinishedAt!.Value, sub.Report));
    }

    [Fact]
    public void Completion_of_its_starting_call_leaves_a_background_sub_agent_running()
    {
        var parser = new CopilotSessionParser(WorkDir);
        parser.AddLine(TaskStart("call-sub", "Map the repo", mode: "background", second: 1));
        parser.AddLine(SubagentStarted(P1, "call-sub", executionMode: "background", second: 2));
        parser.AddLine(ToolComplete("call-sub", """ "success":true,"result":{"content":"Started in the background."} """,
            second: 3));
        var started = parser.Build();

        Assert.Equal("Started in the background.", Assert.IsType<ToolCall>(Assert.Single(started.Items)).Result?.Content);
        var running = Assert.Single(started.SubAgents);
        Assert.Equal(SessionState.Running, running.State);
        Assert.Null(running.FinishedAt);
        Assert.Null(running.Report);

        parser.AddLine(Message("0", content: "Three folders.", phase: "final_answer", second: 8, agentId: P1));
        parser.AddLine(SubagentCompleted(P1, second: 9));
        var finished = Assert.Single(parser.Build().SubAgents);

        Assert.Equal((SessionState.Succeeded, At(9), "Three folders."),
            (finished.State, finished.FinishedAt!.Value, finished.Report));
    }

    [Fact]
    public void Completion_of_a_sub_agent_tool_call_sets_its_result()
    {
        var content = Parse(
            TaskStart("call-sub", "Map the repo", second: 1),
            SubagentStarted(P1, "call-sub", second: 2),
            ToolStart("call-glob", "glob", """{"pattern":"**/*"}""", second: 3, agentId: P1),
            ToolComplete("call-glob", """ "success":true,"result":{"content":"src"} """, second: 4, agentId: P1));

        var glob = Assert.IsType<ToolCall>(content.Items[1]);
        Assert.Equal((P1, "src"), (glob.AgentId, glob.Result?.Content));
        Assert.Equal(SessionState.Running, Assert.Single(content.SubAgents).State);
        Assert.Equal(0, content.UnparsedLines);
    }

    [Fact]
    public void A_sub_agent_final_answer_is_its_report_and_not_the_session_result_text()
    {
        const string answer = """{"status":"done","summary":"Mapped the repo."}""";
        var content = Parse(
            TurnStart("0", second: 1),
            Message("0", content: answer, phase: "final_answer", second: 2),
            TaskStart("call-sub", "Map the repo", mode: "sync", second: 3),
            SubagentStarted(P1, "call-sub", second: 4),
            TurnStart("0", second: 5, agentId: P1),
            Message("0", content: "Three folders.", phase: "final_answer", second: 6, agentId: P1),
            SubagentCompleted(P1, second: 7),
            ToolComplete("call-sub", """ "success":true,"result":{"content":"Hand-back: Three folders."} """, second: 8),
            Result(exitCode: 0, second: 9));

        var sub = Assert.Single(content.SubAgents);
        Assert.Equal((SessionState.Succeeded, At(7), "Three folders."), (sub.State, sub.FinishedAt!.Value, sub.Report));
        Assert.Equal(answer, content.Result?.Text);
        Assert.Equal("Mapped the repo.", content.Result?.Worker?.Summary);
    }

    [Fact]
    public void The_starting_call_result_is_the_report_when_the_sub_agent_gave_none()
    {
        var content = Parse(
            TaskStart("call-sub", "Map the repo", mode: "sync", second: 1),
            SubagentStarted(P1, "call-sub", second: 2),
            SubagentCompleted(P1, second: 7),
            ToolComplete("call-sub", """ "success":true,"result":{"content":"Three folders."} """, second: 8));

        var sub = Assert.Single(content.SubAgents);
        Assert.Equal((SessionState.Succeeded, At(7), "Three folders."), (sub.State, sub.FinishedAt!.Value, sub.Report));
    }

    [Fact]
    public void A_sub_agent_user_message_is_not_the_sent_prompt()
    {
        var parser = new CopilotSessionParser(WorkDir);
        parser.AddLine(Event("user.message", """{"transformedContent":"Map the folders."}""", second: 1, agentId: P1));
        Assert.Null(parser.Build().SentPrompt);

        parser.AddLine(Event("user.message", """{"transformedContent":"Do the task."}""", second: 2));
        Assert.Equal("Do the task.", parser.Build().SentPrompt);
    }

    [Fact]
    public void A_sub_agent_checkpoint_is_not_the_session_checkpoint()
    {
        var parser = new CopilotSessionParser(WorkDir);
        parser.AddLine(Event("session.usage_checkpoint", """{"totalPremiumRequests":2}""", second: 1, agentId: P1));
        Assert.Null(parser.Build().Checkpoint);

        parser.AddLine(Event("session.usage_checkpoint", """{"totalPremiumRequests":1}""", second: 2));
        parser.AddLine(Event("session.usage_checkpoint", """{"totalPremiumRequests":3}""", second: 3, agentId: P1));
        Assert.Equal(1, parser.Build().Checkpoint?.PremiumRequests);
    }

    [Fact]
    public void Model_and_turns_come_from_the_agent_own_events()
    {
        var parser = new CopilotSessionParser(WorkDir);
        parser.AddLine(TurnStart("0", second: 1, agentId: P1));
        parser.AddLine(Message("0", model: "gpt-5.6-luna", second: 2, agentId: P1));
        parser.AddLine(Result(exitCode: 0, second: 3));
        var subOnly = parser.Build();

        Assert.Null(subOnly.Model);
        Assert.Equal(0, subOnly.Result?.Turns);

        parser.AddLine(TurnStart("0", second: 4));
        parser.AddLine(Message("0", model: "gpt-6-sol", second: 5));
        parser.AddLine(TurnStart("1", second: 6, agentId: P1));
        parser.AddLine(Message("1", model: "gpt-5.6-luna", second: 7, agentId: P1));
        parser.AddLine(Result(exitCode: 0, second: 8));
        var content = parser.Build();

        Assert.Equal("gpt-6-sol", content.Model);
        Assert.Equal(1, content.Result?.Turns);
        Assert.Equal(new[] { "gpt-5.6-luna", "gpt-6-sol", "gpt-5.6-luna" }, content.Calls.Select(call => call.Model));
    }

    [Theory]
    [InlineData("""{"type":"subagent.started","agentId":5,"data":{"toolCallId":"call-sub"}}""", 0)]
    [InlineData("""{"type":"subagent.started","agentId":"","data":{"toolCallId":"call-sub"}}""", 0)]
    [InlineData("""{"type":"subagent.started","data":{"toolCallId":"call-sub"}}""", 0)]
    [InlineData("""{"type":"subagent.started","agentId":"agent-p1","data":"not an object"}""", 1)]
    [InlineData("""{"type":"subagent.started","agentId":"agent-p1","data":{"toolCallId":7,"agentDescription":[],"executionMode":false}}""", 1)]
    [InlineData("""{"type":"subagent.completed","agentId":7,"data":null}""", 0)]
    [InlineData("""{"type":"subagent.failed","data":{"toolCallId":["call-sub"]}}""", 0)]
    public void Malformed_sub_agent_lines_add_what_they_can(string line, int subAgents)
    {
        var content = Parse(line);

        Assert.Equal(subAgents, content.SubAgents.Length);
        Assert.All(content.SubAgents, sub =>
            Assert.Equal(new SubAgent(P1, null, "", "sub-agent", null, null, null, false, "", null, null,
                SessionState.Running, null), sub));
        Assert.Equal(0, content.UnparsedLines);
    }
}

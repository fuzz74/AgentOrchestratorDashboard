using OrchDash.Core.Claude;
using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Claude.ClaudeLines;

namespace OrchDash.Core.Tests.Claude;

public sealed class ClaudeSubAgentTests
{
    private const string Sub1 = "toolu_s1";
    private const string Sub2 = "toolu_s2";
    private const string Explore = "Explore";
    private const string Survey = "Survey the parser module";
    private const string Prompt = "List the files in src/Alpha.";
    private const string Report = "The parser module has 3 files.";

    private static readonly SubAgent Running = new(
        Sub1, null, Sub1, Survey, Survey, Explore, null, false, Prompt, At1, null, SessionState.Running, null);

    private static ClaudeSessionParser ParserOf(params string[] lines)
    {
        var parser = new ClaudeSessionParser(null);
        foreach (var line in lines)
            parser.AddLine(line);
        return parser;
    }

    private static string SubAssistant(string messageId, string time, string blocks, string model = SubModelName) =>
        Assistant(messageId, time, blocks, Sub1, Explore, Survey, model);

    private static string SubUser(string time, string content) => User(time, content, Sub1, Explore, Survey);

    private static string HandBack(string toolUseId, bool isError = false) =>
        User(Time3, $"[{ToolResultBlock(toolUseId, $"\"[Subagent hand-back] The text below is the final report.\\n  {Report}\"", isError)}]");

    [Fact]
    public void A_stream_without_sub_agents_has_empty_non_default_sub_agents()
    {
        var content = Parse(Init(), Assistant("msg_1", Time1, Text("hello")), User(Time2, "\"next\""));

        Assert.False(content.SubAgents.IsDefault);
        Assert.Empty(content.SubAgents);
        Assert.All(content.Items, item => Assert.Null(item.AgentId));
        Assert.All(content.Calls, call => Assert.Null(call.AgentId));
    }

    [Fact]
    public void An_agent_tool_use_adds_a_running_sub_agent_and_keeps_its_tool_call()
    {
        var content = Parse(Init(), Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt)));

        Assert.Equal(Running, Assert.Single(content.SubAgents));
        Assert.Equal(
            new ToolCall("msg_1", At1, Sub1, "Agent",
                $$"""{"description":"{{Survey}}","prompt":"{{Prompt}}","subagent_type":"Explore","run_in_background":false}""",
                $"Agent {Survey}", null),
            Assert.Single(content.Items));
        Assert.Equal(0, content.UnparsedLines);
    }

    [Fact]
    public void A_task_tool_use_in_the_background_without_a_prompt_adds_a_sub_agent_with_an_empty_prompt()
    {
        const string description = "Check the public API surface of the alpha parser";
        var content = Parse(Assistant("msg_1", Time2, ToolUse(Sub2, "Task", $$"""{"description":"{{description}}","run_in_background":true}""")));

        Assert.Equal(
            new SubAgent(Sub2, null, Sub2, "Check the public API surface of…", description, null, null, true, "",
                At2, null, SessionState.Running, null),
            Assert.Single(content.SubAgents));
    }

    [Fact]
    public void Run_in_background_true_makes_the_sub_agent_a_background_one()
    {
        var content = Parse(Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt, background: true)));

        Assert.Equal(Running with { Background = true }, Assert.Single(content.SubAgents));
    }

    [Fact]
    public void Other_tool_uses_add_no_sub_agent()
    {
        var content = Parse(Assistant("msg_1", Time1, ToolUse("toolu_1", "Read", """{"file_path":"a.cs","description":"x"}""")));

        Assert.Empty(content.SubAgents);
    }

    [Fact]
    public void Sub_agent_events_tag_their_items_and_calls_and_skip_the_prompt_echo()
    {
        var content = Parse(
            Init(),
            Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt)),
            SubUser(Time2, $"[{Text(Prompt)}]"),
            SubAssistant("msg_s1_1", Time2, $"{ThinkingBlock("look")},{ToolUse("toolu_r1", "Read", """{"file_path":"a.cs"}""")}"),
            SubUser(Time3, $"[{ToolResultBlock("toolu_r1", "\"class A {}\"")}]"),
            SubUser(Time3, "\"Keep going.\""),
            SubAssistant("msg_s1_2", Time3, Text("Three files.")));

        Assert.Collection(content.Items,
            item => Assert.Null(item.AgentId),
            item => Assert.Equal(new Thinking("msg_s1_1", At2, "look", null) { AgentId = Sub1 }, item),
            item => Assert.Equal(
                new ToolCall("msg_s1_1", At2, "toolu_r1", "Read", """{"file_path":"a.cs"}""", "Read a.cs",
                    new ToolResult(At3, false, "class A {}", null, null)) { AgentId = Sub1 },
                item),
            item => Assert.Equal(new UserText(null, At3, "Keep going.", false) { AgentId = Sub1 }, item),
            item => Assert.Equal(new AssistantText("msg_s1_2", At3, "Three files.") { AgentId = Sub1 }, item));
        Assert.Equal(
            [("msg_1", ModelName, null), ("msg_s1_1", SubModelName, Sub1), ("msg_s1_2", SubModelName, Sub1)],
            content.Calls.Select(call => (call.Id, call.Model, call.AgentId)));
        Assert.Equal(Running with { Model = SubModelName }, Assert.Single(content.SubAgents));
        Assert.Equal(0, content.UnparsedLines);
    }

    [Fact]
    public void A_prompt_echo_as_string_content_is_skipped_but_the_agents_own_text_is_kept()
    {
        var content = Parse(
            Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt)),
            SubUser(Time2, $"\"{Prompt}\""),
            User(Time2, $"\"{Prompt}\""));

        Assert.Equal(new UserText(null, At2, Prompt, false), content.Items[^1]);
        Assert.Equal(2, content.Items.Length);
    }

    [Fact]
    public void An_event_of_an_unknown_sub_agent_adds_it()
    {
        const string description = "  Plan   the work ";
        var content = Parse(
            Assistant("msg_s9_1", Time2, Text("planning"), "toolu_s9", "Plan", description),
            Assistant("msg_s9_2", Time3, Text("still planning"), "toolu_s9", "Other", "Other description", model: ModelName));

        Assert.Equal(
            new SubAgent("toolu_s9", null, "toolu_s9", "Plan the work", description, "Plan", SubModelName, false, "",
                At2, null, SessionState.Running, null),
            Assert.Single(content.SubAgents));
        Assert.All(content.Items, item => Assert.Equal("toolu_s9", item.AgentId));
        Assert.Equal(0, content.UnparsedLines);
    }

    [Fact]
    public void A_user_event_of_an_unknown_sub_agent_adds_it_and_keeps_its_text()
    {
        var content = Parse(User(Time1, "\"Start here.\"", "toolu_s9", "Plan", "Plan the work"));

        var sub = Assert.Single(content.SubAgents);
        Assert.Equal("toolu_s9", sub.Id);
        Assert.Equal("", sub.Prompt);
        Assert.Equal(At1, sub.StartedAt);
        Assert.Equal(new UserText(null, At1, "Start here.", false) { AgentId = "toolu_s9" }, Assert.Single(content.Items));
    }

    [Fact]
    public void Sub_agent_calls_leave_the_session_model_alone()
    {
        var withInit = Parse(Init(), SubAssistant("msg_s1_1", Time1, Text("a")));
        var withoutInit = Parse(
            Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt)),
            SubAssistant("msg_s1_1", Time2, Text("a")));
        var onlySubAgent = Parse(SubAssistant("msg_s1_1", Time1, Text("a")));

        Assert.Equal(ModelName, withInit.Model);
        Assert.Equal(ModelName, withoutInit.Model);
        Assert.Null(onlySubAgent.Model);
        Assert.Equal(SubModelName, Assert.Single(onlySubAgent.SubAgents).Model);
    }

    [Fact]
    public void A_sub_agent_model_is_the_model_of_its_first_call()
    {
        var content = Parse(
            Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt)),
            SubAssistant("msg_s1_1", Time2, Text("a"), model: "claude-sonnet-4-5"),
            SubAssistant("msg_s1_1", Time2, Text("b"), model: SubModelName),
            SubAssistant("msg_s1_2", Time3, Text("c"), model: SubModelName));

        Assert.Equal("claude-sonnet-4-5", Assert.Single(content.SubAgents).Model);
    }

    [Fact]
    public void Sub_agents_come_in_order_of_first_appearance_with_their_parent()
    {
        var content = Parse(
            Assistant("msg_s9_1", Time1, Text("first"), "toolu_s9", "Plan", "Plan the work"),
            Assistant("msg_1", Time2, AgentUse(Sub1, Survey, Prompt)),
            SubAssistant("msg_s1_1", Time3, AgentUse(Sub2, "Nested survey", "Look deeper.")),
            Assistant("msg_2", Time4, AgentUse("toolu_s9", "Late start", "Too late.")));

        Assert.Equal([("toolu_s9", null), (Sub1, null), (Sub2, Sub1)], content.SubAgents.Select(sub => (sub.Id, sub.ParentId)));
        Assert.Equal("Plan the work", content.SubAgents[0].Name);
        Assert.Equal(Sub1, content.Items.OfType<ToolCall>().Single(call => call.ToolId == Sub2).AgentId);
    }

    [Fact]
    public void A_notification_finishes_a_foreground_sub_agent_and_its_hand_back_then_changes_nothing()
    {
        var parser = ParserOf(
            Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt)),
            SubAssistant("msg_s1_1", Time2, Text("Looking.")),
            TaskNotification("a1b2c3", Sub1, "completed", Report));
        var finished = Running with { Model = SubModelName, FinishedAt = At2, State = SessionState.Succeeded, Report = Report };
        Assert.Equal(finished, Assert.Single(parser.Build().SubAgents));

        parser.AddLine(HandBack(Sub1));
        var content = parser.Build();

        Assert.Equal(finished, Assert.Single(content.SubAgents));
        var call = Assert.IsType<ToolCall>(content.Items[0]);
        Assert.StartsWith("[Subagent hand-back]", call.Result?.Content, StringComparison.Ordinal);
        Assert.Equal(0, content.UnparsedLines);
    }

    [Theory]
    [InlineData(false, SessionState.Succeeded)]
    [InlineData(true, SessionState.Failed)]
    public void Without_a_notification_the_hand_back_finishes_a_foreground_sub_agent(bool isError, SessionState state)
    {
        var content = Parse(
            Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt)),
            HandBack(Sub1, isError));

        var expected = Running with
        {
            FinishedAt = At3,
            State = state,
            Report = $"[Subagent hand-back] The text below is the final report.\n  {Report}",
        };
        Assert.Equal(expected, Assert.Single(content.SubAgents));
        Assert.Equal(0, content.UnparsedLines);
    }

    [Fact]
    public void A_hand_back_of_an_unseen_tool_use_finishes_its_sub_agent_without_counting()
    {
        var content = Parse(
            SubAssistant("msg_s1_1", Time2, Text("Looking.")),
            HandBack(Sub1));

        var sub = Assert.Single(content.SubAgents);
        Assert.Equal(SessionState.Succeeded, sub.State);
        Assert.Equal(At3, sub.FinishedAt);
        Assert.Equal(0, content.UnparsedLines);
    }

    [Fact]
    public void The_tool_result_of_a_background_sub_agent_leaves_it_running()
    {
        var content = Parse(
            Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt, background: true)),
            User(Time2, $"[{ToolResultBlock(Sub1, "\"Async agent launched successfully.\"")}]"));

        Assert.Equal(Running with { Background = true }, Assert.Single(content.SubAgents));
        Assert.NotNull(Assert.IsType<ToolCall>(Assert.Single(content.Items)).Result);
    }

    [Fact]
    public void A_notification_without_timestamp_finishes_at_the_latest_event_time_seen()
    {
        var content = Parse(
            Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt)),
            SubAssistant("msg_s1_1", Time3, Text("Looking.")),
            Assistant("msg_2", Time2, Text("Waiting.")),
            TaskNotification("a1b2c3", Sub1, "completed", Report));

        Assert.Equal(At3, Assert.Single(content.SubAgents).FinishedAt);
    }

    [Theory]
    [InlineData("completed", SessionState.Succeeded)]
    [InlineData("failed", SessionState.Failed)]
    public void A_notification_finishes_a_background_sub_agent_by_its_tool_use_id(string status, SessionState state)
    {
        var content = Parse(
            Assistant("msg_1", Time1, AgentUse(Sub2, "Survey CLI flags", "List the CLI flags.", background: true)),
            TaskStarted("d4e5f6", Sub2, background: true),
            User(Time2, $"[{ToolResultBlock(Sub2, "\"Async agent launched successfully.\"")}]"),
            Result("Waiting for the sub-agent."),
            TaskNotification("d4e5f6", Sub2, status, "There are 4 flags."));

        var sub = Assert.Single(content.SubAgents);
        Assert.Equal(state, sub.State);
        Assert.Equal("There are 4 flags.", sub.Report);
        Assert.Equal(At2, sub.FinishedAt);
        Assert.Equal(0, content.UnparsedLines);
    }

    [Fact]
    public void A_notification_without_tool_use_id_finds_the_sub_agent_through_task_started()
    {
        var content = Parse(
            Assistant("msg_1", Time1, AgentUse(Sub2, "Survey CLI flags", "List the CLI flags.", background: true)),
            TaskStarted("d4e5f6", Sub2, background: true),
            TaskStarted("other", "toolu_bash", background: false, taskType: "local_bash"),
            TaskNotification("d4e5f6", null, "completed", "There are 4 flags."));

        var sub = Assert.Single(content.SubAgents);
        Assert.Equal(SessionState.Succeeded, sub.State);
        Assert.Equal("There are 4 flags.", sub.Report);
        Assert.Equal(At1, sub.FinishedAt);
    }

    [Theory]
    [InlineData("""{"type":"system","subtype":"task_notification","task_id":"a1b2c3","tool_use_id":"toolu_s1","status":"failed","summary":"Again.","session_id":"sess-1"}""")]
    [InlineData("""{"type":"system","subtype":"task_notification","task_id":"zz","tool_use_id":"toolu_unknown","status":"completed","summary":"x","session_id":"sess-1"}""")]
    [InlineData("""{"type":"system","subtype":"task_notification","task_id":"zz","status":"completed","summary":"x","session_id":"sess-1"}""")]
    [InlineData("""{"type":"system","subtype":"task_notification","session_id":"sess-1"}""")]
    [InlineData("""{"type":"system","subtype":"task_started","task_id":"b1","tool_use_id":"toolu_b1","task_type":"local_bash","session_id":"sess-1"}""")]
    public void A_finish_for_an_unknown_or_finished_sub_agent_changes_nothing(string line)
    {
        var parser = ParserOf(
            Assistant("msg_1", Time1, AgentUse(Sub1, Survey, Prompt)),
            TaskNotification("a1b2c3", Sub1, "completed", Report));
        var before = parser.Build();

        parser.AddLine(line);

        Assert.Same(before, parser.Build());
        Assert.Equal(SessionState.Succeeded, Assert.Single(before.SubAgents).State);
        Assert.Equal(0, before.UnparsedLines);
    }

    [Fact]
    public void A_local_bash_task_adds_no_sub_agent()
    {
        var content = Parse(
            Assistant("msg_1", Time1, ToolUse("toolu_b1", "Bash", """{"command":"dotnet test"}""")),
            TaskStarted("b1", "toolu_b1", background: false, taskType: "local_bash"),
            TaskNotification("b1", "toolu_b1", "completed", "Build and run tests"),
            User(Time2, $"[{ToolResultBlock("toolu_b1", "\"Passed!\"")}]"));

        Assert.Empty(content.SubAgents);
        Assert.Equal(new ToolResult(At2, false, "Passed!", null, null), Assert.IsType<ToolCall>(Assert.Single(content.Items)).Result);
        Assert.Equal(0, content.UnparsedLines);
    }

    public static TheoryData<string> TurnStarts => new()
    {
        Init(),
        Assistant("msg_2", Time2, Text("more")),
        User(Time2, "\"next\""),
        SubAssistant("msg_s1_1", Time2, Text("sub-agent work")),
    };

    [Theory]
    [MemberData(nameof(TurnStarts))]
    public void An_init_assistant_or_user_event_after_a_result_clears_it_until_the_next_result(string line)
    {
        var parser = ParserOf(Assistant("msg_1", Time1, Text("done")), Result("first"));
        Assert.Equal("first", parser.Build().Result?.Text);

        parser.AddLine(line);
        Assert.Null(parser.Build().Result);

        parser.AddLine(Result("second"));
        Assert.Equal("second", parser.Build().Result?.Text);
    }

    public static TheoryData<string> NotTurnStarts => new()
    {
        RateLimitEvent(RateLimitInfo()),
        ThinkingTokens(50),
        """{"type":"system","subtype":"permission_denied","tool_name":"Bash","message":"Command not allowed","session_id":"sess-1"}""",
        TaskStarted("d4e5f6", Sub2, background: true),
        TaskNotification("d4e5f6", Sub2, "completed", "There are 4 flags."),
        """{"type":"system","subtype":"session_title_changed","title":"orch","session_id":"sess-1"}""",
        """{"type":"stream_event","event":{"type":"ping"},"session_id":"sess-1"}""",
        "not json",
    };

    [Theory]
    [MemberData(nameof(NotTurnStarts))]
    public void Other_events_after_a_result_keep_it(string line)
    {
        var content = Parse(Assistant("msg_1", Time1, Text("done")), Result("first"), line);

        Assert.Equal("first", content.Result?.Text);
    }

    [Fact]
    public void A_result_with_nothing_after_it_stays_set()
    {
        var content = Parse(Init(), Assistant("msg_1", Time1, Text("done")), Result("first"));

        Assert.Equal("first", content.Result?.Text);
    }
}

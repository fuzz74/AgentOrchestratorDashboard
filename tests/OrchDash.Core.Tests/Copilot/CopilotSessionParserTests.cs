using OrchDash.Core.Copilot;
using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Copilot.CopilotEvents;

namespace OrchDash.Core.Tests.Copilot;

public sealed class CopilotSessionParserTests
{
    [Fact]
    public void Build_without_lines_is_empty()
    {
        var content = new CopilotSessionParser(WorkDir).Build();

        Assert.False(content.Calls.IsDefault);
        Assert.False(content.Items.IsDefault);
        Assert.Empty(content.Calls);
        Assert.Empty(content.Items);
        Assert.Null(content.Result);
        Assert.Null(content.FirstEventAt);
        Assert.Equal(0, content.UnparsedLines);
    }

    [Fact]
    public void Every_kept_line_updates_the_event_times()
    {
        var content = Parse(
            Event("user.message", """{"content":"prompt"}""", second: 1),
            TurnStart("0", second: 2),
            Event("assistant.turn_end", """{"turnId":"0"}""", second: 3));

        Assert.Equal(At(1), content.FirstEventAt);
        Assert.Equal(At(3), content.LastEventAt);
    }

    [Fact]
    public void Ephemeral_line_changes_nothing()
    {
        var parser = new CopilotSessionParser(WorkDir);
        parser.AddLine(TurnStart("0", second: 1));
        var before = parser.Build();

        parser.AddLine(Event("assistant.turn_start", """{"turnId":"1"}""", second: 9, ephemeral: true));
        parser.AddLine(Event("tool.execution_complete", """{"toolCallId":"none"}""", second: 9, ephemeral: true));
        var after = parser.Build();

        Assert.Same(before, after);
        Assert.Single(after.Calls);
        Assert.Equal(At(1), after.LastEventAt);
        Assert.Equal(0, after.UnparsedLines);
    }

    [Fact]
    public void Build_returns_the_same_instance_until_a_line_is_added()
    {
        var parser = new CopilotSessionParser(WorkDir);
        parser.AddLine(TurnStart("0"));
        var first = parser.Build();

        Assert.Same(first, parser.Build());

        parser.AddLine(Event("assistant.turn_end", """{"turnId":"0"}""", second: 5));
        var second = parser.Build();

        Assert.NotSame(first, second);
        Assert.Equal(At(5), second.LastEventAt);
    }

    [Fact]
    public void Turn_start_adds_a_model_call()
    {
        var content = Parse(TurnStart("0", second: 1), TurnStart("1", second: 2));

        Assert.Equal(new[] { new ModelCall("0", null, At(1), null), new ModelCall("1", null, At(2), null) }, content.Calls);
    }

    [Fact]
    public void Message_sets_the_model_and_adds_thinking_text_and_tool_calls_in_order()
    {
        var content = Parse(
            TurnStart("0", second: 1),
            TurnStart("1", second: 2),
            Message("1", content: "Looking at the code.", reasoning: "**Plan**\nRead first.", second: 3,
                toolRequests:
                [
                    ToolRequest("call_a", "view", """{"path":"C:\\Work\\Repo.worktrees\\core\\README.md"}"""),
                    ToolRequest("call_b", "powershell", """{ "command" : "dotnet build", "description": "Build" }"""),
                ]));

        Assert.Equal("gpt-6-sol", content.Model);
        Assert.Equal(new[] { new ModelCall("0", null, At(1), null), new ModelCall("1", "gpt-6-sol", At(2), null) }, content.Calls);
        Assert.Collection(content.Items,
            item => Assert.Equal(new Thinking("1", At(3), "**Plan**\nRead first.", null), item),
            item => Assert.Equal(new AssistantText("1", At(3), "Looking at the code."), item),
            item => Assert.Equal(new ToolCall("1", At(3), "call_a", "view",
                """{"path":"C:\\Work\\Repo.worktrees\\core\\README.md"}""", "view README.md", null), item),
            item => Assert.Equal(new ToolCall("1", At(3), "call_b", "powershell",
                """{"command":"dotnet build","description":"Build"}""", "powershell dotnet build", null), item));
    }

    [Fact]
    public void Message_without_reasoning_or_content_adds_only_tool_calls()
    {
        var content = Parse(
            TurnStart("0"),
            Message("0", toolRequests: ToolRequest("call_a", "apply_patch", "\"*** Begin Patch\\n*** End Patch\"")));

        var call = Assert.IsType<ToolCall>(Assert.Single(content.Items));
        Assert.Equal("apply_patch", call.Name);
        Assert.Equal("\"*** Begin Patch\\n*** End Patch\"", call.InputJson);
        Assert.Equal("apply_patch", call.Summary);
    }

    [Fact]
    public void Message_for_an_unknown_turn_still_sets_the_session_model()
    {
        var content = Parse(TurnStart("0"), Message("7", content: "Hi", model: "claude-sonnet-4.5"));

        Assert.Equal("claude-sonnet-4.5", content.Model);
        Assert.Null(Assert.Single(content.Calls).Model);
        Assert.Equal("7", Assert.Single(content.Items).CallId);
    }

    [Fact]
    public void Execution_start_sets_the_time_of_a_known_call()
    {
        var content = Parse(
            TurnStart("0"),
            Message("0", second: 1, toolRequests:
            [
                ToolRequest("call_a", "view", """{"path":"a.cs"}"""),
                ToolRequest("call_b", "view", """{"path":"b.cs"}"""),
            ]),
            ToolStart("call_a", "view", """{"path":"a.cs"}""", second: 4));

        Assert.Collection(content.Items,
            item => Assert.Equal(new ToolCall("0", At(4), "call_a", "view", """{"path":"a.cs"}""", "view a.cs", null), item),
            item => Assert.Equal(At(1), item.Time));
    }

    [Fact]
    public void Execution_start_of_an_unknown_call_adds_it()
    {
        var content = Parse(
            ToolStart("call_x", "rg", """{"pattern":"TODO","path":"C:/Work/Repo.worktrees/core/src"}""", second: 2),
            ToolComplete("call_x", """ "success":true,"result":{"content":"none"} """, second: 3));

        var call = Assert.IsType<ToolCall>(Assert.Single(content.Items));
        Assert.Null(call.CallId);
        Assert.Equal(At(2), call.Time);
        Assert.Equal("call_x", call.ToolId);
        Assert.Equal("rg", call.Name);
        Assert.Equal("""{"pattern":"TODO","path":"C:/Work/Repo.worktrees/core/src"}""", call.InputJson);
        Assert.Equal("rg TODO in src", call.Summary);
        Assert.Equal("none", call.Result?.Content);
    }

    [Fact]
    public void Successful_completion_carries_content_diff_and_exit_code()
    {
        const string diff = "\ndiff --git a/README.md b/README.md\n--- a/README.md\n+++ b/README.md\n@@ -1 +1 @@\n-old\n+new\n";
        var content = Parse(
            TurnStart("0"),
            Message("0", toolRequests: ToolRequest("call_a", "powershell", """{"command":"git diff"}""")),
            ToolComplete("call_a",
                $$""" "success":true,"shellExecution":{"exitCode":0},"result":{"content":"done","detailedContent":{{Quote(diff)}}} """,
                second: 6));

        var call = Assert.IsType<ToolCall>(Assert.Single(content.Items));
        Assert.Equal(new ToolResult(At(6), false, "done", diff, 0), call.Result);
    }

    [Fact]
    public void Failed_completion_uses_the_error_and_exit_code()
    {
        var content = Parse(
            TurnStart("0"),
            Message("0", toolRequests: ToolRequest("call_a", "powershell", """{"command":"dotnet test"}""")),
            ToolComplete("call_a",
                """ "success":false,"shellExecution":{"exitCode":1},"error":{ "message":"Tests failed", "code":"failure" },"result":{"detailedContent":"1 test failed"} """,
                second: 7));

        var call = Assert.IsType<ToolCall>(Assert.Single(content.Items));
        Assert.Equal(new ToolResult(At(7), true, """{"message":"Tests failed","code":"failure"}""", null, 1), call.Result);
    }

    [Fact]
    public void Completion_with_neither_content_nor_error_has_empty_content()
    {
        var content = Parse(
            ToolStart("call_a", "view", """{"path":"a.cs"}"""),
            ToolComplete("call_a", """ "success":false """));

        var call = Assert.IsType<ToolCall>(Assert.Single(content.Items));
        Assert.Equal(new ToolResult(At(0), true, "", null, null), call.Result);
    }

    [Fact]
    public void Completion_replaces_the_call_at_the_same_position()
    {
        var content = Parse(
            TurnStart("0"),
            Message("0", content: "Two reads.", toolRequests:
            [
                ToolRequest("call_a", "view", """{"path":"a.cs"}"""),
                ToolRequest("call_b", "view", """{"path":"b.cs"}"""),
            ]),
            ToolComplete("call_a", """ "success":true,"result":{"content":"A"} """, second: 2));

        Assert.Equal(3, content.Items.Length);
        var first = Assert.IsType<ToolCall>(content.Items[1]);
        var second = Assert.IsType<ToolCall>(content.Items[2]);
        Assert.Equal(("call_a", "A"), (first.ToolId, first.Result?.Content));
        Assert.Equal("call_b", second.ToolId);
        Assert.Null(second.Result);
    }

    [Fact]
    public void Completion_without_a_matching_call_is_counted()
    {
        var content = Parse(
            TurnStart("0"),
            ToolComplete("call_missing", """ "success":true,"result":{"content":"A"} """),
            Event("tool.execution_complete", """{"success":true}"""));

        Assert.Empty(content.Items);
        Assert.Equal(2, content.UnparsedLines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"type\":\"assistant.turn_start\"")]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    [InlineData("{\"type\":\"result\"} trailing")]
    public void Line_that_is_not_a_JSON_object_is_counted(string line)
    {
        var content = Parse(line);

        Assert.Equal(1, content.UnparsedLines);
        Assert.Empty(content.Items);
        Assert.Null(content.FirstEventAt);
    }

    [Theory]
    [InlineData("""{"type":"user.message","data":{"content":"Do the task"},"id":"1","timestamp":"2026-10-03T09:00:00Z"}""")]
    [InlineData("""{"type":"assistant.turn_end","data":{"turnId":"0"}}""")]
    [InlineData("""{"type":"session.usage_checkpoint","data":{"totalPremiumRequests":1}}""")]
    [InlineData("""{"data":{"turnId":"0"}}""")]
    [InlineData("""{"type":7,"data":{"turnId":"0"}}""")]
    [InlineData("""{"type":"assistant.turn_start","data":"not an object"}""")]
    [InlineData("""{"type":"assistant.turn_start","data":{"turnId":5}}""")]
    [InlineData("""{"type":"assistant.message","data":{"content":5,"reasoningText":[],"toolRequests":{"a":1},"model":false}}""")]
    [InlineData("""{"type":"assistant.message","data":{"toolRequests":[1,"x",null]}}""")]
    [InlineData("""{"type":"tool.execution_start","data":null,"timestamp":12}""")]
    [InlineData("""{"type":"tool.execution_start","data":{"toolCallId":"a"},"timestamp":"yesterday"}""")]
    [InlineData("""{"type":"result","exitCode":"0","usage":{"premiumRequests":"1","sessionDurationMs":1e300}}""")]
    public void Unknown_or_malformed_event_is_skipped_without_counting(string line)
    {
        var parser = new CopilotSessionParser(WorkDir);

        parser.AddLine(line);
        var content = parser.Build();

        Assert.Equal(0, content.UnparsedLines);
        Assert.Empty(content.Calls);
    }

    [Fact]
    public void Message_with_fields_of_the_wrong_type_adds_what_it_can()
    {
        var content = Parse(
            """{"type":"assistant.message","data":{"turnId":"0","content":"Hi","toolRequests":[1,{"toolCallId":"t","name":5,"arguments":"x"}]}}""");

        Assert.Null(content.Model);
        Assert.Collection(content.Items,
            item => Assert.Equal(new AssistantText("0", null, "Hi"), item),
            item => Assert.Equal(new ToolCall("0", null, "t", "", "\"x\"", "", null), item));
    }

    [Fact]
    public void Line_that_is_not_valid_UTF16_is_counted()
    {
        var content = Parse("{\"text\":\"\uD800\"}");

        Assert.Equal(1, content.UnparsedLines);
    }

    [Fact]
    public void Result_with_exit_code_0_and_a_fenced_worker_report()
    {
        const string answer = """
            ```json
            {"status":"done","summary":"Added the parser.","notes_for_dependents":"Use ParseAll."}
            ```
            """;
        var content = Parse(
            TurnStart("0", second: 1),
            Message("0", content: "Starting.", phase: "commentary", second: 2),
            TurnStart("1", second: 3),
            Message("1", content: answer, phase: "final_answer", second: 4),
            Result(exitCode: 0, second: 5));

        Assert.Equal("s-1", content.SessionId);
        Assert.Equal(At(5), content.LastEventAt);
        var result = Assert.IsType<SessionResult>(content.Result);
        Assert.False(result.IsError);
        Assert.Equal("success", result.Subtype);
        Assert.Equal(answer, result.Text);
        Assert.Equal(
            "{\n  \"status\": \"done\",\n  \"summary\": \"Added the parser.\",\n  \"notes_for_dependents\": \"Use ParseAll.\"\n}",
            result.StructuredJson);
        Assert.Equal(new WorkerReport("done", "Added the parser.", "Use ParseAll.", null), result.Worker);
        Assert.Null(result.Review);
        Assert.Equal(2, result.Turns);
        Assert.Equal(1.5, result.PremiumRequests);
        Assert.Equal(TimeSpan.FromMilliseconds(41112), result.ApiDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(52974), result.Duration);
        Assert.Equal(445, result.LinesAdded);
        Assert.Equal(23, result.LinesRemoved);
        Assert.Null(result.CostUsd);
        Assert.Null(result.Usage);
        Assert.Null(result.ContextWindow);
    }

    [Fact]
    public void Result_with_an_unfenced_review()
    {
        const string answer =
            """{"spec_verdict":"pass","quality_verdict":"fail","issues":[{"severity":"minor","file":"a.cs","description":"Typo."}],"summary":"Fine."}""";
        var content = Parse(
            TurnStart("0"),
            Message("0", content: answer, phase: "final_answer"),
            Result(exitCode: 0));

        var review = Assert.IsType<ReviewVerdict>(content.Result?.Review);
        Assert.Equal(("pass", "fail", "Fine."), (review.SpecVerdict, review.QualityVerdict, review.Summary));
        Assert.Equal(new[] { new ReviewIssue("minor", "a.cs", "Typo.") }, review.Issues);
        Assert.Null(content.Result?.Worker);
        Assert.StartsWith("{\n  \"spec_verdict\": \"pass\",", content.Result?.StructuredJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_with_exit_code_not_0_and_plain_text()
    {
        var content = Parse(
            TurnStart("0"),
            Message("0", content: "Resolved both conflicts.", phase: "final_answer"),
            Result(exitCode: 3));

        var result = Assert.IsType<SessionResult>(content.Result);
        Assert.True(result.IsError);
        Assert.Equal("exit 3", result.Subtype);
        Assert.Equal("Resolved both conflicts.", result.Text);
        Assert.Null(result.StructuredJson);
        Assert.Null(result.Worker);
        Assert.Null(result.Review);
        Assert.Equal(1, result.Turns);
    }

    [Fact]
    public void Result_text_is_the_last_final_answer()
    {
        var content = Parse(
            Message("0", content: "First answer.", phase: "final_answer"),
            Message("1", content: "Second answer.", phase: "final_answer"),
            Message("2", content: "A comment after it.", phase: "commentary"),
            Message("3", content: "No phase."),
            Result(exitCode: 0));

        Assert.Equal("Second answer.", content.Result?.Text);
    }

    [Fact]
    public void Result_without_a_final_answer_has_no_text()
    {
        var content = Parse(Message("0", content: "Only commentary.", phase: "commentary"), Result(exitCode: 0));

        var result = Assert.IsType<SessionResult>(content.Result);
        Assert.Null(result.Text);
        Assert.Null(result.StructuredJson);
        Assert.Equal(0, result.Turns);
    }

    [Fact]
    public void Result_without_usage_or_exit_code_is_an_error()
    {
        var content = Parse("""{"type":"result"}""");

        Assert.Equal(new SessionResult(true, "error", null, null, null, null, null, 0, null, null, null, null, null, null, null),
            content.Result);
    }

    [Theory]
    [InlineData("```\n{\"status\":\"done\"}\n```", true)]
    [InlineData("```json\r\n{\"status\":\"done\"}\r\n```", true)]
    [InlineData("  \n```json\n{\"status\":\"done\"}\n```\n\n", true)]
    [InlineData("{\"status\":\"done\"}", true)]
    [InlineData("```json\n{\"status\":\"done\"}", false)]
    [InlineData("````\n{\"status\":\"done\"}\n````", false)]
    [InlineData("```json\n```json\n{\"status\":\"done\"}\n```\n```", false)]
    [InlineData("Done:\n```json\n{\"status\":\"done\"}\n```", false)]
    [InlineData("```json\n[1,2]\n```", false)]
    [InlineData("```\n```", false)]
    public void Result_removes_one_surrounding_fence(string answer, bool isWorker)
    {
        var content = Parse(Message("0", content: answer, phase: "final_answer"), Result(exitCode: 0));

        var result = Assert.IsType<SessionResult>(content.Result);
        Assert.Equal(answer, result.Text);
        if (isWorker)
        {
            Assert.Equal("{\n  \"status\": \"done\"\n}", result.StructuredJson);
            Assert.Equal("done", result.Worker?.Status);
        }
        else
        {
            Assert.Null(result.StructuredJson);
            Assert.Null(result.Worker);
        }
    }
}

using OrchDash.Core.Claude;
using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Claude.ClaudeLines;

namespace OrchDash.Core.Tests.Claude;

public sealed class ClaudeSessionParserTests
{
    [Fact]
    public void Build_without_lines_is_empty_with_non_default_arrays()
    {
        var content = new ClaudeSessionParser(null).Build();

        Assert.False(content.Calls.IsDefault);
        Assert.False(content.Items.IsDefault);
        Assert.Empty(content.Calls);
        Assert.Empty(content.Items);
        Assert.Null(content.SessionId);
        Assert.Null(content.Result);
        Assert.Equal(0, content.UnparsedLines);
    }

    [Fact]
    public void Build_returns_the_same_instance_until_a_line_is_added()
    {
        var parser = new ClaudeSessionParser(null);
        parser.AddLine(Init());
        var first = parser.Build();

        Assert.Same(first, parser.Build());

        parser.AddLine(Assistant("msg_1", Time1, Text("hello")));
        var second = parser.Build();

        Assert.NotSame(first, second);
        Assert.Single(second.Items);
        Assert.Empty(first.Items);
    }

    [Fact]
    public void Any_line_sets_session_id_and_timestamps_set_first_and_last_event()
    {
        var parser = new ClaudeSessionParser(null);
        parser.AddLine("""{"type":"system","subtype":"thinking_tokens","estimated_tokens":5,"session_id":"from-system"}""");
        Assert.Equal("from-system", parser.Build().SessionId);

        parser.AddLine(Assistant("msg_1", Time2, Text("a")));
        parser.AddLine(User(Time3, "\"next\""));
        parser.AddLine(Assistant("msg_2", Time1, Text("b")));
        var content = parser.Build();

        Assert.Equal(SessionId, content.SessionId);
        Assert.Equal(At1, content.FirstEventAt);
        Assert.Equal(At3, content.LastEventAt);
    }

    [Fact]
    public void Lines_without_timestamp_leave_event_times_null()
    {
        var content = Parse(Init());

        Assert.Equal(SessionId, content.SessionId);
        Assert.Null(content.FirstEventAt);
        Assert.Null(content.LastEventAt);
    }

    [Fact]
    public void System_init_sets_model_and_init()
    {
        var content = Parse(Init());

        Assert.Equal(ModelName, content.Model);
        var init = Assert.IsType<SessionInit>(content.Init);
        Assert.Equal(@"C:\Work\Repo", init.Cwd);
        Assert.Equal("acceptEdits", init.PermissionMode);
        Assert.Equal("2.1.285", init.CliVersion);
        Assert.Equal(["Bash", "Read"], init.Tools);
        Assert.Equal(["docs (connected)", "sample (failed)"], init.McpServers);
        Assert.Empty(content.Items);
    }

    [Fact]
    public void Thinking_takes_the_remembered_token_estimate_once()
    {
        var content = Parse(
            ThinkingTokens(50),
            ThinkingTokens(350),
            Assistant("msg_1", Time1, ThinkingBlock("")),
            Assistant("msg_2", Time2, ThinkingBlock("Let me look at the parser.")));

        Assert.Collection(content.Items,
            item =>
            {
                var thinking = Assert.IsType<Thinking>(item);
                Assert.Equal("", thinking.Text);
                Assert.Equal(350, thinking.EstimatedTokens);
                Assert.Equal("msg_1", thinking.CallId);
            },
            item =>
            {
                var thinking = Assert.IsType<Thinking>(item);
                Assert.Equal("Let me look at the parser.", thinking.Text);
                Assert.Null(thinking.EstimatedTokens);
            });
    }

    [Fact]
    public void Permission_denied_adds_a_notice()
    {
        var content = Parse(
            """{"type":"system","subtype":"permission_denied","tool_name":"Bash","message":"Command not allowed","session_id":"sess-1","timestamp":"2026-10-01T08:46:40.513Z"}""");

        var notice = Assert.IsType<Notice>(Assert.Single(content.Items));
        Assert.Null(notice.CallId);
        Assert.Equal(At1, notice.Time);
        Assert.Equal("permission denied", notice.Kind);
        Assert.Equal("Bash: Command not allowed", notice.Text);
    }

    [Fact]
    public void Assistant_lines_with_one_message_id_give_one_model_call_from_the_first_line()
    {
        var content = Parse(
            Assistant("msg_1", Time1, ThinkingBlock(""), input: 2, cacheRead: 100, cacheWrite: 30),
            Assistant("msg_1", Time2, ToolUse("toolu_1", "Glob", """{"pattern":"**/*.cs"}"""), input: 9, cacheRead: 999, cacheWrite: 999),
            Assistant("msg_2", Time3, Text("done"), input: 1, cacheRead: 200, cacheWrite: 5));

        Assert.Equal(ModelName, content.Model);
        Assert.Collection(content.Calls,
            call =>
            {
                Assert.Equal("msg_1", call.Id);
                Assert.Equal(ModelName, call.Model);
                Assert.Equal(At1, call.StartedAt);
                Assert.Equal(new TokenUsage(2, 100, 30, null), call.Usage);
            },
            call =>
            {
                Assert.Equal("msg_2", call.Id);
                Assert.Equal(At3, call.StartedAt);
                Assert.Equal(new TokenUsage(1, 200, 5, null), call.Usage);
            });
    }

    [Fact]
    public void Assistant_blocks_become_items_in_line_and_block_order()
    {
        var content = Parse(
            Assistant("msg_1", Time1, ThinkingBlock("plan")),
            Assistant("msg_1", Time2, $"{Text("first")},{ToolUse("toolu_1", "Read", """{"file_path":"a.cs"}""")},{Text("last")}"));

        Assert.Collection(content.Items,
            item => Assert.Equal(new Thinking("msg_1", At1, "plan", null), item),
            item => Assert.Equal(new AssistantText("msg_1", At2, "first"), item),
            item =>
            {
                var call = Assert.IsType<ToolCall>(item);
                Assert.Equal("msg_1", call.CallId);
                Assert.Equal(At2, call.Time);
                Assert.Equal("toolu_1", call.ToolId);
                Assert.Equal("Read", call.Name);
                Assert.Null(call.Result);
            },
            item => Assert.Equal(new AssistantText("msg_1", At2, "last"), item));
    }

    [Fact]
    public void Tool_use_input_is_compact_json()
    {
        var content = Parse(Assistant("msg_1", Time1, ToolUse("toolu_1", "Edit",
            """{ "file_path" : "a.cs",  "old_string": "x · y", "replace_all": false, "n": [1, 2] }""")));

        var call = Assert.IsType<ToolCall>(Assert.Single(content.Items));
        Assert.Equal("""{"file_path":"a.cs","old_string":"x · y","replace_all":false,"n":[1,2]}""", call.InputJson);
    }

    [Fact]
    public void User_content_as_a_string_is_user_text()
    {
        var content = Parse(
            User(Time1, "\"Build the parser.\""),
            User(Time2, "\"Continue.\"", extra: ""","isSynthetic":true"""));

        Assert.Collection(content.Items,
            item => Assert.Equal(new UserText(null, At1, "Build the parser.", false), item),
            item => Assert.Equal(new UserText(null, At2, "Continue.", true), item));
    }

    [Fact]
    public void User_text_blocks_are_user_text()
    {
        var content = Parse(User(Time1, """[{"type":"text","text":"one"},{"type":"text","text":"two"}]""", extra: ""","isSynthetic":true"""));

        Assert.Collection(content.Items,
            item => Assert.Equal(new UserText(null, At1, "one", true), item),
            item => Assert.Equal(new UserText(null, At1, "two", true), item));
    }

    [Fact]
    public void Tool_result_with_string_content_replaces_the_call_at_its_position()
    {
        var content = Parse(
            Assistant("msg_1", Time1, $"{ToolUse("toolu_1", "Bash", """{"command":"dotnet test"}""")},{Text("waiting")}"),
            User(Time2, $"[{ToolResultBlock("toolu_1", "\"Exit code 1\\nFailed!\"", isError: true)}]"));

        Assert.Equal(2, content.Items.Length);
        var call = Assert.IsType<ToolCall>(content.Items[0]);
        Assert.Equal("msg_1", call.CallId);
        Assert.Equal(At1, call.Time);
        Assert.Equal(new ToolResult(At2, true, "Exit code 1\nFailed!", null, null), call.Result);
        Assert.IsType<AssistantText>(content.Items[1]);
    }

    [Fact]
    public void Tool_result_with_text_blocks_joins_them_with_newlines()
    {
        const string blocks = """[{"type":"text","text":"first"},{"type":"image","source":{}},{"type":"text","text":"second"}]""";
        var content = Parse(
            Assistant("msg_1", Time1, ToolUse("toolu_1", "Task", """{"description":"find usages"}""")),
            User(Time2, $"[{ToolResultBlock("toolu_1", blocks)}]"));

        var call = Assert.IsType<ToolCall>(Assert.Single(content.Items));
        Assert.Equal(new ToolResult(At2, false, "first\nsecond", null, null), call.Result);
    }

    [Fact]
    public void Structured_patch_becomes_diff_text()
    {
        const string patch = """
            ,"tool_use_result":{"filePath":"a.cs","structuredPatch":[{"oldStart":215,"oldLines":3,"newStart":215,"newLines":2,"lines":["     }","-    old();"," "]},{"oldStart":300,"oldLines":0,"newStart":299,"newLines":1,"lines":["+    added();"]}]}
            """;
        var content = Parse(
            Assistant("msg_1", Time1, ToolUse("toolu_1", "Edit", """{"file_path":"a.cs"}""")),
            User(Time2, $"[{ToolResultBlock("toolu_1", "\"updated\"")}]", extra: patch));

        var call = Assert.IsType<ToolCall>(Assert.Single(content.Items));
        Assert.Equal(
            "@@ -215,3 +215,2 @@\n     }\n-    old();\n \n@@ -300,0 +299,1 @@\n+    added();",
            call.Result?.Diff);
    }

    [Fact]
    public void Empty_structured_patch_gives_no_diff()
    {
        var content = Parse(
            Assistant("msg_1", Time1, ToolUse("toolu_1", "Write", """{"file_path":"a.cs","content":"x"}""")),
            User(Time2, $"[{ToolResultBlock("toolu_1", "\"File created\"")}]", extra: ""","tool_use_result":{"type":"create","structuredPatch":[]}"""));

        var call = Assert.IsType<ToolCall>(Assert.Single(content.Items));
        Assert.NotNull(call.Result);
        Assert.Null(call.Result.Diff);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("")]
    [InlineData("""["type","assistant"]""")]
    [InlineData("42")]
    [InlineData("""{"type":"assistant","message":""")]
    public void A_line_that_is_not_a_json_object_counts_as_unparsed(string line)
    {
        var content = Parse(Init(), line);

        Assert.Equal(1, content.UnparsedLines);
        Assert.Equal(SessionId, content.SessionId);
    }

    [Fact]
    public void A_tool_result_without_matching_call_is_skipped_and_counted()
    {
        var content = Parse(
            Assistant("msg_1", Time1, ToolUse("toolu_1", "Read", """{"file_path":"a.cs"}""")),
            User(Time2, $"[{ToolResultBlock("toolu_other", "\"text\"")}]"));

        Assert.Equal(1, content.UnparsedLines);
        var call = Assert.IsType<ToolCall>(Assert.Single(content.Items));
        Assert.Null(call.Result);
    }

    [Theory]
    [InlineData("""{"type":"stream_event","event":{"type":"ping"},"session_id":"other"}""")]
    [InlineData("""{"type":"system","subtype":"session_title_changed","title":"orch","session_id":"other"}""")]
    [InlineData("""{"type":"system","subtype":"task_started","task_id":"b1","session_id":"other","timestamp":"2026-10-01T08:46:43.772Z"}""")]
    [InlineData("""{"session_id":"other"}""")]
    public void A_type_or_subtype_outside_the_table_is_skipped_without_counting(string line)
    {
        var parser = new ClaudeSessionParser(null);
        parser.AddLine(Init());
        var before = parser.Build();

        parser.AddLine(line);
        var after = parser.Build();

        Assert.Same(before, after);
        Assert.Equal(0, after.UnparsedLines);
        Assert.Equal(SessionId, after.SessionId);
        Assert.Null(after.LastEventAt);
    }

    [Theory]
    [InlineData("""{"type":7}""")]
    [InlineData("""{"type":"assistant","message":"text"}""")]
    [InlineData("""{"type":"assistant","message":{"id":5,"model":[],"content":{"type":"text"},"usage":"none"},"timestamp":12}""")]
    [InlineData("""{"type":"assistant","message":{"id":"m","content":[1,null,{"type":"tool_use","id":3,"name":{},"input":"x"},{"type":"text"}]}}""")]
    [InlineData("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":null}]},"timestamp":"yesterday"}""")]
    [InlineData("""{"type":"user","message":{"content":42},"isSynthetic":"yes"}""")]
    [InlineData("""{"type":"system","subtype":"init","tools":"Bash","mcp_servers":[1,{"name":2}],"cwd":{}}""")]
    [InlineData("""{"type":"result","is_error":"no","duration_ms":1e300,"num_turns":1.5,"usage":[],"modelUsage":{"a":1,"b":{"contextWindow":"big"}}}""")]
    [InlineData("""{"type":"assistant","message":{"id":"m","content":[{"type":"text","text":"bad \ud800 escape"},{"type":"tool_use","id":"t","name":"Read","input":{"file_path":"\udc00"}}]}}""")]
    [InlineData("""{"type":"result","result":"{\"status\":\"\\ud800\"}","structured_output":{"summary":"\ud800"}}""")]
    public void Odd_lines_never_throw(string line)
    {
        var parser = new ClaudeSessionParser(@"C:\Work");

        parser.AddLine(line);
        var content = parser.Build();

        Assert.False(content.Calls.IsDefault);
        Assert.False(content.Items.IsDefault);
    }

    [Fact]
    public void An_escaped_unpaired_surrogate_keeps_its_escaped_text()
    {
        var content = Parse(Assistant("msg_1", Time1,
            $"{Text(@"bad \ud800 escape")},{ToolUse("toolu_1", "Edit", """{"file_path":"x\udc00.cs"}""")}"));

        Assert.Equal(new AssistantText("msg_1", At1, @"bad \ud800 escape"), content.Items[0]);
        var call = Assert.IsType<ToolCall>(content.Items[1]);
        Assert.Equal("""{"file_path":"x\udc00.cs"}""", call.InputJson);
        Assert.Equal(@"Edit x\udc00.cs", call.Summary);
    }

    [Fact]
    public void A_line_with_an_unpaired_surrogate_character_counts_as_unparsed()
    {
        var parser = new ClaudeSessionParser(null);

        parser.AddLine("{\"type\":\"user\",\"message\":{\"content\":\"lone \ud800 surrogate\"}}");

        Assert.Equal(1, parser.Build().UnparsedLines);
    }

    [Fact]
    public void Result_with_structured_output_gives_a_worker_report()
    {
        const string line = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":164599,"duration_api_ms":158386,"num_turns":14,"result":"{\"status\":\"done\",\"summary\":\"short\"}","session_id":"sess-1","total_cost_usd":0.8224428,"usage":{"input_tokens":16,"cache_creation_input_tokens":49941,"cache_read_input_tokens":404954,"output_tokens":17093},"modelUsage":{"claude-opus-5-5":{"contextWindow":1000000},"claude-haiku-4-5":{"contextWindow":200000}},"structured_output":{"status":"done","summary":"Added the parser.","notes_for_dependents":"Use Build().","blocked_reason":null}}
            """;

        var result = Assert.IsType<SessionResult>(Parse(line).Result);

        Assert.False(result.IsError);
        Assert.Equal("success", result.Subtype);
        Assert.Equal("""{"status":"done","summary":"short"}""", result.Text);
        Assert.Equal(
            "{\n  \"status\": \"done\",\n  \"summary\": \"Added the parser.\",\n  \"notes_for_dependents\": \"Use Build().\",\n  \"blocked_reason\": null\n}",
            result.StructuredJson);
        Assert.Equal(new WorkerReport("done", "Added the parser.", "Use Build().", null), result.Worker);
        Assert.Null(result.Review);
        Assert.Equal(0.8224428, result.CostUsd);
        Assert.Equal(14, result.Turns);
        Assert.Equal(TimeSpan.FromMilliseconds(164599), result.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(158386), result.ApiDuration);
        Assert.Equal(new TokenUsage(16, 404954, 49941, 17093), result.Usage);
        Assert.Equal(1000000, result.ContextWindow);
        Assert.Null(result.PremiumRequests);
        Assert.Null(result.LinesAdded);
        Assert.Null(result.LinesRemoved);
    }

    [Fact]
    public void Result_text_that_is_a_json_object_gives_a_review()
    {
        const string line = """
            {"type":"result","subtype":"success","is_error":false,"num_turns":4,"result":"{\"spec_verdict\":\"pass\",\"quality_verdict\":\"fail\",\"issues\":[{\"severity\":\"major\",\"file\":\"a.cs\",\"description\":\"Errors are swallowed.\"}],\"summary\":\"Mostly fine.\"}","session_id":"sess-1"}
            """;

        var result = Assert.IsType<SessionResult>(Parse(line).Result);

        Assert.Equal(
            "{\n  \"spec_verdict\": \"pass\",\n  \"quality_verdict\": \"fail\",\n  \"issues\": [\n    {\n      \"severity\": \"major\",\n      \"file\": \"a.cs\",\n      \"description\": \"Errors are swallowed.\"\n    }\n  ],\n  \"summary\": \"Mostly fine.\"\n}",
            result.StructuredJson);
        Assert.Null(result.Worker);
        var review = Assert.IsType<ReviewVerdict>(result.Review);
        Assert.Equal("pass", review.SpecVerdict);
        Assert.Equal("fail", review.QualityVerdict);
        Assert.Equal("Mostly fine.", review.Summary);
        Assert.Equal([new ReviewIssue("major", "a.cs", "Errors are swallowed.")], review.Issues);
        Assert.Null(result.CostUsd);
        Assert.Null(result.Usage);
        Assert.Null(result.ContextWindow);
    }

    [Fact]
    public void Result_with_plain_text_has_no_structured_json()
    {
        const string line = """
            {"type":"result","subtype":"error_max_turns","is_error":true,"num_turns":30,"result":"I ran out of turns.","session_id":"sess-1","total_cost_usd":1.5}
            """;

        var result = Assert.IsType<SessionResult>(Parse(line).Result);

        Assert.True(result.IsError);
        Assert.Equal("error_max_turns", result.Subtype);
        Assert.Equal("I ran out of turns.", result.Text);
        Assert.Null(result.StructuredJson);
        Assert.Null(result.Worker);
        Assert.Null(result.Review);
        Assert.Equal(1.5, result.CostUsd);
        Assert.Equal(30, result.Turns);
    }
}

using OrchDash.Core.Model;
using OrchDash.Core.Transcript;
using Xunit;
using static OrchDash.Core.Tests.Transcript.TranscriptLines;

namespace OrchDash.Core.Tests.Transcript;

/// <summary>The rows of the transcript table of spec 4.3 (spec 11.3).</summary>
public sealed class ClaudeTranscriptMappingTests : IDisposable
{
    private readonly TempProjects _projects = new();

    public void Dispose() => _projects.Dispose();

    private StoreData Map(params string[] lines)
    {
        _projects.Write(Folder, SessionId, Lines(lines));
        var data = new ClaudeTranscriptStore(_projects.ProjectsDir).Read(SessionId, WorkDir);
        Assert.NotNull(data);
        return data;
    }

    [Fact]
    public void An_empty_file_gives_empty_data()
    {
        var data = Map();

        Assert.Equivalent(StoreData.Empty, data, strict: true);
    }

    [Fact]
    public void The_latest_top_level_version_wins_on_any_record()
    {
        var data = Map(
            """{"type":"user","version":"2.1.1"}""",
            """{"type":"queue-operation","version":"2.1.2"}""",
            """{"type":"user","message":{"version":"9.9.9"}}""",
            """{"type":"cost-state","totalCostUSD":0.1}""");

        Assert.Equal("2.1.2", data.CliVersion);
    }

    [Fact]
    public void Records_of_one_message_id_give_one_call_with_the_figures_of_the_last()
    {
        var data = Map(
            Assistant("msg_1", Time1, Usage(2, 17465, 29331, 10, 5), stopReason: "pause_turn"),
            Assistant("msg_1", Time2, Usage(2, 17465, 29331, 179, 24)),
            Assistant("msg_2", Time2, Usage(1, 46796, 300, 50, 0), stopReason: "end_turn"));

        Assert.Equal(
            [
                new CallFigures("msg_1", At1, new TokenUsage(2, 17465, 29331, 179), 24, null, null, "tool_use"),
                new CallFigures("msg_2", At2, new TokenUsage(1, 46796, 300, 50), 0, null, null, "end_turn"),
            ],
            data.Calls);
    }

    [Fact]
    public void Missing_counts_are_0_and_missing_thinking_and_stop_reason_are_null()
    {
        var data = Map(
            """{"type":"assistant","timestamp":"2026-10-01T08:46:40.513Z","message":{"id":"msg_1","usage":{"output_tokens":7}}}""",
            """{"type":"assistant","message":{"id":"msg_2","stop_reason":null}}""",
            """{"type":"assistant","message":{"stop_reason":"end_turn"}}""");

        Assert.Equal(
            [
                new CallFigures("msg_1", At1, new TokenUsage(0, 0, 0, 7), null, null, null, null),
                new CallFigures("msg_2", null, new TokenUsage(0, 0, 0, 0), null, null, null, null),
            ],
            data.Calls);
    }

    [Fact]
    public void Sidechain_records_are_skipped()
    {
        var data = Map(
            Assistant("msg_1", Time1, Usage(1, 2, 3, 4, 0)),
            """{"type":"assistant","isSidechain":true,"version":"9.9.9","message":{"id":"msg_side","usage":{"output_tokens":1}}}""",
            """{"type":"assistant","isSidechain":true,"message":{"id":"msg_1","usage":{"output_tokens":99}}}""",
            """{"type":"attachment","isSidechain":true,"rendered":[{"content":"side"}],"attachment":{"type":"date"}}""",
            """{"type":"cost-state","isSidechain":true,"totalCostUSD":9.9}""");

        var call = Assert.Single(data.Calls);
        Assert.Equal(new TokenUsage(1, 2, 3, 4), call.Usage);
        Assert.Equal("2.1.285", data.CliVersion);
        Assert.Empty(data.Injected);
        Assert.Null(data.CostUsd);
    }

    [Fact]
    public void Prompt_snapshot_gives_the_system_prompt_and_tools_with_indented_schemas()
    {
        var data = Map(
            """{"type":"attachment","timestamp":"2026-10-01T08:46:40.513Z","attachment":{"type":"prompt_snapshot","systemPrompt":["You are Claude.","Be brief.",3],"tools":[{"name":"Bash","description":"Runs a command.","schema":{"name":"Bash","description":"Runs a command.","input_schema":{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}}},{"name":"Plain","schema":{"type":"object","title":"<ä>"}},{"name":"Bare"},"not a tool"]}}""");

        Assert.Equal(["You are Claude.", "Be brief."], data.SystemPrompt);
        Assert.Equal(3, data.Tools.Length);
        Assert.Equal(new ToolDefinition("Bash", "Runs a command.", """
            {
              "type": "object",
              "properties": {
                "command": {
                  "type": "string"
                }
              },
              "required": [
                "command"
              ]
            }
            """.ReplaceLineEndings("\n")), data.Tools[0]);
        Assert.Equal(new ToolDefinition("Plain", null, """
            {
              "type": "object",
              "title": "<ä>"
            }
            """.ReplaceLineEndings("\n")), data.Tools[1]);
        Assert.Equal(new ToolDefinition("Bare", null, null), data.Tools[2]);
    }

    [Fact]
    public void A_later_snapshot_without_tools_replaces_the_system_prompt_and_keeps_the_tools()
    {
        var data = Map(
            """{"type":"attachment","attachment":{"type":"prompt_snapshot","systemPrompt":["old"],"tools":[{"name":"Old"}]}}""",
            """{"type":"attachment","attachment":{"type":"prompt_snapshot","systemPrompt":["a","b"],"tools":[{"name":"Read"},{"name":"Write"}]}}""",
            """{"type":"attachment","attachment":{"type":"prompt_snapshot","systemPrompt":["c"]}}""");

        Assert.Equal(["c"], data.SystemPrompt);
        Assert.Equal(["Read", "Write"], data.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void Rendered_attachments_are_injected_items_in_file_order()
    {
        var data = Map(
            Injected("date", Time1, "Today is 2026-10-01."),
            """{"type":"attachment","timestamp":"2026-10-01T08:47:00Z","rendered":[{"content":"<system-reminder>"},{"content":42},{"content":"</system-reminder>"}],"attachment":{"type":"skill_listing"}}""",
            """{"type":"attachment","timestamp":"2026-10-01T08:47:00Z","renderedRole":"user","rendered":[{"content":"ctx"}],"attachment":{"type":"session_context"}}""",
            """{"type":"attachment","rendered":[],"attachment":{"type":"empty"}}""",
            """{"type":"attachment","attachment":{"type":"credential_org"}}""",
            """{"type":"user","rendered":[{"content":"not an attachment"}]}""");

        Assert.Equal(
            [
                new InjectedItem("date", "system", At1, "Today is 2026-10-01."),
                new InjectedItem("skill_listing", "system", At2, "<system-reminder>\n</system-reminder>"),
                new InjectedItem("session_context", "user", At2, "ctx"),
            ],
            data.Injected);
    }

    [Fact]
    public void A_prompt_snapshot_with_rendered_text_is_not_injected()
    {
        var data = Map(
            """{"type":"attachment","rendered":[{"content":"x"}],"attachment":{"type":"prompt_snapshot","systemPrompt":["a"]}}""");

        Assert.Empty(data.Injected);
        Assert.Equal(["a"], data.SystemPrompt);
    }

    [Fact]
    public void The_latest_cost_state_wins()
    {
        var data = Map(
            CostState(0.25, 10, 2),
            Assistant("msg_1", Time1, Usage(1, 2, 3, 4, 0)),
            CostState(0.8224428, 602, 32));

        Assert.Equal(0.8224428, data.CostUsd);
        Assert.Equal(602, data.LinesAdded);
        Assert.Equal(32, data.LinesRemoved);
    }
}

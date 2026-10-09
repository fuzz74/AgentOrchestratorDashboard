using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Store.MergeData;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 36.3-36.5: figures for sub-agent calls from the sub-agent transcripts and the Copilot rows of each agent.
public sealed class ProviderMergeSubAgentTests
{
    private static readonly TokenUsage LogUsage = new(1, 2, 3, null);

    private static ProviderStores UsageOnly(params CallFigures[] rows) =>
        new(null, null, null, new FakeUsageReader(_ => Rows(("p-1", rows))));

    private static long?[] Inputs(Session session) => [.. session.Content.Calls.Select(call => call.Usage?.Input)];

    [Fact]
    public void A_Claude_call_takes_the_figures_with_its_id_from_a_sub_agent_transcript()
    {
        var calls = new[]
        {
            Call("m1", At(12, 0, 10), LogUsage),
            Call("s1", At(12, 0, 20), LogUsage, agentId: "toolu_s1"),
            Call("s2", At(12, 0, 30), LogUsage, agentId: "toolu_s2"),
            Call("m2", At(12, 0, 40), LogUsage),
        };
        var session = ClaudeSession(Files("alpha"), "c-1", calls, subAgents: [Sub("toolu_s1", "toolu_s1"), Sub("toolu_s2", "toolu_s2")]);
        var transcript = Stored(calls: [Figures("m1", input: 6, output: 40)], subAgents:
        [
            ("toolu_s1", Stored(calls: [Figures("s1", At(12, 0, 21), input: 7, output: 50, thinking: 3, stopReason: "tool_use")])),
            ("toolu_s2", Stored(calls: [Figures("s2", At(12, 0, 31), input: 8, output: 60)])),
        ]);

        var merged = Assert.Single(Merge(new ProviderStores(new FakeSessionStore((_, _) => transcript), null, null, null), session).Sessions);

        Assert.Same(transcript, merged.Stores);
        Assert.Empty(merged.Unavailable);
        Assert.Equal([new TokenUsage(6, 100, 20, 40), new TokenUsage(7, 100, 20, 50), new TokenUsage(8, 100, 20, 60)],
            merged.Content.Calls.Take(3).Select(call => call.Usage));
        var sub = merged.Content.Calls[1];
        Assert.Equal(3, sub.ThinkingTokens);
        Assert.Equal("tool_use", sub.StopReason);
        Assert.Equal(At(12, 0, 20), sub.StartedAt);
        Assert.Equal("toolu_s1", sub.AgentId);
        Assert.Equal("toolu_s2", merged.Content.Calls[2].AgentId);
        Assert.Same(calls[3], merged.Content.Calls[3]);
    }

    [Fact]
    public void The_transcript_comes_before_the_sub_agent_transcripts_and_the_first_figures_with_an_id_win()
    {
        var calls = new[] { Call("m1", At(12, 0, 10)), Call("s1", At(12, 0, 20), agentId: "toolu_s1") };
        var session = ClaudeSession(Files("alpha"), "c-1", calls, subAgents: [Sub("toolu_s1", "toolu_s1")]);
        var transcript = Stored(calls: [Figures("s1", input: 1)], subAgents:
        [
            ("toolu_s2", Stored(calls: [Figures("m1", input: 4)])),
            ("toolu_s1", Stored(calls: [Figures("s1", input: 2), Figures("m1", input: 3), Figures("m1", input: 5)])),
        ]);

        var merged = Assert.Single(Merge(new ProviderStores(new FakeSessionStore((_, _) => transcript), null, null, null), session).Sessions);

        Assert.Equal([3L, 1L], Inputs(merged));
    }

    [Fact]
    public void Copilot_rows_go_to_the_sub_agent_with_their_agent_id_and_match_its_calls_by_position()
    {
        var calls = new[]
        {
            Call("a1", At(12, 1, 0)),
            Call("p1", At(12, 1, 10), agentId: "agent-p1"),
            Call("a2", At(12, 1, 20)),
            Call("q1", At(12, 1, 30), agentId: "agent-p2"),
            Call("p2", At(12, 1, 40), agentId: "agent-p1"),
        };
        var session = CopilotSession(Files("alpha"), "p-1", calls,
            subAgents: [Sub("agent-p1", "call-1"), Sub("agent-p2", "call-2", parentId: "agent-p1")]);
        CallFigures[] rows =
        [
            Figures(time: At(12, 1, 1), input: 1),
            Figures(time: At(12, 1, 21), input: 2),
            Figures(time: At(12, 1, 11), input: 3, nanoAiu: 30, agentId: "agent-p1", parentToolCallId: "call-1"),
            Figures(time: At(12, 1, 41), input: 4, agentId: "agent-p1", parentToolCallId: "call-1"),
            Figures(time: At(12, 1, 31), input: 5, agentId: "agent-p2", parentToolCallId: "call-2"),
        ];

        var merged = Assert.Single(Merge(UsageOnly(rows), session).Sessions);

        Assert.Equal(rows, merged.Stores.Calls);
        Assert.Empty(merged.Unavailable);
        Assert.Equal([1L, 3L, 2L, 5L, 4L], Inputs(merged));
        Assert.Equal(30, merged.Content.Calls[1].NanoAiu);
        Assert.Equal(calls.Select(call => (call.Id, call.AgentId, call.StartedAt)),
            merged.Content.Calls.Select(call => (call.Id, call.AgentId, call.StartedAt)));
    }

    [Fact]
    public void A_row_without_a_known_agent_id_goes_to_the_sub_agent_its_parent_tool_call_started()
    {
        var calls = new[]
        {
            Call("a1", At(12, 1, 0)),
            Call("p1", At(12, 1, 10), agentId: "agent-p1"),
            Call("q1", At(12, 1, 20), agentId: "agent-p2"),
        };
        var session = CopilotSession(Files("alpha"), "p-1", calls, subAgents: [Sub("agent-p1", "call-1"), Sub("agent-p2", "call-2")]);
        CallFigures[] rows =
        [
            Figures(time: At(12, 1, 1), input: 1),
            Figures(time: At(12, 1, 11), input: 2, parentToolCallId: "call-1"),
            Figures(time: At(12, 1, 21), input: 3, agentId: "agent-gone", parentToolCallId: "call-2"),
        ];

        var merged = Assert.Single(Merge(UsageOnly(rows), session).Sessions);

        Assert.Equal(rows, merged.Stores.Calls);
        Assert.Equal([1L, 2L, 3L], Inputs(merged));
    }

    [Fact]
    public void A_row_with_neither_id_null_or_empty_goes_to_the_agent_itself()
    {
        var calls = new[] { Call("a1", At(12, 1, 0)), Call("a2", At(12, 1, 10)) };
        var session = CopilotSession(Files("alpha"), "p-1", calls, subAgents: [Sub("agent-p1", "")]);
        CallFigures[] rows =
        [
            Figures(time: At(12, 1, 1), input: 1),
            Figures(time: At(12, 1, 11), input: 2, agentId: "", parentToolCallId: ""),
        ];

        var merged = Assert.Single(Merge(UsageOnly(rows), session).Sessions);

        Assert.Equal(rows, merged.Stores.Calls);
        Assert.Equal([1L, 2L], Inputs(merged));
    }

    [Fact]
    public void A_row_of_no_known_sub_agent_is_left_out()
    {
        var calls = new[] { Call("a1", At(12, 1, 0)), Call("p1", At(12, 1, 10), agentId: "agent-p1"), Call("a2", At(12, 1, 20)) };
        var session = CopilotSession(Files("alpha"), "p-1", calls, subAgents: [Sub("agent-p1", "call-1")]);
        CallFigures[] rows =
        [
            Figures(time: At(12, 1, 1), input: 1),
            Figures(time: At(12, 1, 2), input: 90, agentId: "agent-x"),
            Figures(time: At(12, 1, 3), input: 91, parentToolCallId: "call-x"),
            Figures(time: At(12, 1, 4), input: 92, agentId: "agent-x", parentToolCallId: "call-y"),
            Figures(time: At(12, 1, 21), input: 2),
        ];

        var merged = Assert.Single(Merge(UsageOnly(rows), session).Sessions);

        Assert.Equal([rows[0], rows[4]], merged.Stores.Calls);
        Assert.Equal([1L, null, 2L], Inputs(merged));
        Assert.Same(calls[1], merged.Content.Calls[1]);
    }

    [Fact]
    public void Without_sub_agents_a_row_with_an_agent_id_is_left_out()
    {
        var calls = new[] { Call("a1", At(12, 1, 0)), Call("a2", At(12, 1, 10)) };
        CallFigures[] rows =
        [
            Figures(time: At(12, 1, 1), input: 1),
            Figures(time: At(12, 1, 5), input: 90, agentId: "agent-p1", parentToolCallId: "call-1"),
            Figures(time: At(12, 1, 11), input: 2),
        ];

        var merged = Assert.Single(Merge(UsageOnly(rows), CopilotSession(Files("alpha"), "p-1", calls)).Sessions);

        Assert.Equal([rows[0], rows[2]], merged.Stores.Calls);
        Assert.Equal([1L, 2L], Inputs(merged));
    }

    [Fact]
    public void Each_agent_keeps_its_rows_from_its_first_call_on_cut_to_its_number_of_calls_in_query_order()
    {
        var calls = new[]
        {
            Call("a1", At(12, 5, 0)),
            Call("a2", At(12, 5, 10)),
            Call("p1", At(12, 5, 30), agentId: "agent-p1"),
        };
        var session = CopilotSession(Files("alpha", attempt: 2), "p-1", calls, subAgents: [Sub("agent-p1", "call-1")]);
        CallFigures[] rows =
        [
            Figures(time: At(12, 1, 1), input: 90),                        // before the agent's first call
            Figures(time: At(12, 1, 2), input: 91, agentId: "agent-p1"),   // before the sub-agent's first call
            Figures(time: At(12, 5, 1), input: 1),
            Figures(time: At(12, 5, 20), input: 92, agentId: "agent-p1"),  // after the agent's start, before the sub-agent's
            Figures(time: At(12, 5, 11), input: 2),
            Figures(time: At(12, 5, 31), input: 3, agentId: "agent-p1"),
            Figures(time: At(12, 5, 21), input: 93),                       // past the agent's two calls
            Figures(time: At(12, 5, 41), input: 94, agentId: "agent-p1"),  // past the sub-agent's one call
        ];

        var merged = Assert.Single(Merge(UsageOnly(rows), session).Sessions);

        Assert.Equal([rows[2], rows[4], rows[5]], merged.Stores.Calls);
        Assert.Equal([1L, 2L, 3L], Inputs(merged));
        Assert.Empty(merged.Unavailable);
    }

    [Fact]
    public void A_sub_agent_without_a_first_call_start_keeps_all_its_rows_up_to_its_calls()
    {
        var calls = new[] { Call("a1", At(12, 5, 0)), Call("p1", null, agentId: "agent-p1"), Call("p2", At(12, 5, 20), agentId: "agent-p1") };
        var session = CopilotSession(Files("alpha"), "p-1", calls, subAgents: [Sub("agent-p1", "call-1")]);
        CallFigures[] rows =
        [
            Figures(time: At(12, 1, 1), input: 2, agentId: "agent-p1"),
            Figures(time: null, input: 3, agentId: "agent-p1"),
            Figures(time: At(12, 5, 1), input: 1),
            Figures(time: At(12, 5, 21), input: 90, agentId: "agent-p1"),
        ];

        var merged = Assert.Single(Merge(UsageOnly(rows), session).Sessions);

        Assert.Equal(rows[..3], merged.Stores.Calls);
        Assert.Equal([1L, 2L, 3L], Inputs(merged));
    }

    [Fact]
    public void Rows_kept_only_for_a_sub_agent_give_no_reason()
    {
        var calls = new[] { Call("a1", At(12, 1, 0)), Call("p1", At(12, 1, 10), agentId: "agent-p1") };
        var session = CopilotSession(Files("alpha"), "p-1", calls, subAgents: [Sub("agent-p1", "call-1")]);
        CallFigures[] rows = [Figures(time: At(12, 1, 11), input: 1, agentId: "agent-p1")];

        var merged = Assert.Single(Merge(UsageOnly(rows), session).Sessions);

        Assert.Empty(merged.Unavailable);
        Assert.Equal(rows, merged.Stores.Calls);
        Assert.Same(calls[0], merged.Content.Calls[0]);
        Assert.Equal(1L, merged.Content.Calls[1].Usage!.Input);
    }

    [Fact]
    public void No_row_kept_for_the_agent_or_any_sub_agent_gives_no_database_rows()
    {
        var calls = new[] { Call("a1", At(12, 1, 0)), Call("p1", At(12, 1, 10), agentId: "agent-p1") };
        var session = CopilotSession(Files("alpha"), "p-1", calls, subAgents: [Sub("agent-p1", "call-1")]);
        CallFigures[] rows =
        [
            Figures(time: At(12, 0, 59), input: 90),
            Figures(time: At(12, 1, 9), input: 91, agentId: "agent-p1"),
            Figures(time: At(12, 1, 11), input: 92, agentId: "agent-x"),
        ];

        var merged = Assert.Single(Merge(UsageOnly(rows), session).Sessions);

        Assert.Equal(["no database rows"], merged.Unavailable);
        Assert.Empty(merged.Stores.Calls);
        Assert.Same(session.Content, merged.Content);
    }

    [Fact]
    public void A_session_with_sub_agents_but_no_rows_for_them_still_gets_its_own_figures()
    {
        var calls = new[]
        {
            Call("a1", At(12, 1, 0)),
            Call("p1", At(12, 1, 10), LogUsage, agentId: "agent-p1"),
            Call("a2", At(12, 1, 20)),
            Call("q1", At(12, 1, 30), LogUsage, agentId: "agent-p2"),
        };
        var session = CopilotSession(Files("alpha"), "p-1", calls, subAgents: [Sub("agent-p1", "call-1"), Sub("agent-p2", "call-2")]);
        CallFigures[] rows = [Figures(time: At(12, 1, 1), input: 11), Figures(time: At(12, 1, 21), input: 12)];

        var merged = Assert.Single(Merge(UsageOnly(rows), session).Sessions);

        Assert.Empty(merged.Unavailable);
        Assert.Equal(rows, merged.Stores.Calls);
        Assert.Equal([11L, LogUsage.Input, 12L, LogUsage.Input], Inputs(merged));
        Assert.Same(calls[1], merged.Content.Calls[1]);
        Assert.Same(calls[3], merged.Content.Calls[3]);
    }

    [Fact]
    public void A_session_with_sub_agents_that_nothing_changes_is_returned_as_the_same_instance()
    {
        var transcript = Stored(calls: [Figures("other")], subAgents: [("toolu_s1", Stored(calls: [Figures("other-sub")]))]);
        var claude = ClaudeSession(Files("alpha"), "c-1",
            [Call("m1", At(12, 0, 10), LogUsage), Call("s1", At(12, 0, 20), LogUsage, agentId: "toolu_s1")],
            subAgents: [Sub("toolu_s1", "toolu_s1")]) with { Stores = transcript };
        var folder = Stored(TestedVersions.CopilotCli);
        var copilot = CopilotSession(Files("beta"), "p-1",
            [Call("x1", At(12, 1, 0), LogUsage), Call("y1", At(12, 1, 10), LogUsage, agentId: "agent-p1")],
            subAgents: [Sub("agent-p1", "call-1")]) with { Stores = folder };
        var stores = new ProviderStores(new FakeSessionStore((_, _) => transcript), new FakeSessionStore((_, _) => folder), null, null);

        var merged = Merge(stores, claude, copilot).Sessions;

        Assert.Same(claude, merged[0]);
        Assert.Same(copilot, merged[1]);
    }
}

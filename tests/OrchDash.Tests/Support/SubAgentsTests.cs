using System.Collections.Immutable;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

// The SubAgents helpers of spec 4.3.
public sealed class SubAgentsTests
{
    // The agent's own entries, those of sub-agent a and those of sub-agent b, interleaved.
    private static readonly SessionContent Mixed = SessionContent.Empty with
    {
        Calls =
        [
            new ModelCall("c1", "m", null, null),
            new ModelCall("c2", "m", null, null) { AgentId = "a" },
            new ModelCall("c3", "m", null, null),
            new ModelCall("c4", "m", null, null) { AgentId = "b" },
            new ModelCall("c5", "m", null, null) { AgentId = "a" },
        ],
        Items =
        [
            new UserText(null, null, "own 1", false),
            new AssistantText("c2", null, "a 1") { AgentId = "a" },
            new AssistantText("c3", null, "own 2"),
            new AssistantText("c4", null, "b 1") { AgentId = "b" },
            new AssistantText("c5", null, "a 2") { AgentId = "a" },
        ],
        SubAgents = [Sub("a"), Sub("b")],
    };

    private static SubAgent Sub(string id, string? parentId = null, string toolCallId = "",
        SessionState state = SessionState.Running) =>
        new(id, parentId, toolCallId, "Name " + id, "Description " + id, "Explore", null, false, "",
            SampleRun.At(12, 0, 0), null, state, null);

    private static SessionContent With(params SubAgent[] subAgents) =>
        SessionContent.Empty with { SubAgents = [.. subAgents] };

    private static ToolCall Tool(string toolId, string? agentId = null) =>
        new("msg_1", SampleRun.At(12, 0, 1), toolId, "Agent", "{}", "summary", null) { AgentId = agentId };

    private static string[] Ids(ImmutableArray<SubAgent> subAgents) => [.. subAgents.Select(s => s.Id)];

    private static string[] Texts(ImmutableArray<ConversationItem> items) =>
        [.. items.Select(i => i is UserText user ? user.Text : ((AssistantText)i).Text)];

    private static string Repeat(char c, int count) => new(c, count);

    [Theory]
    [InlineData("Survey the parser module", "Survey the parser module")]
    [InlineData("  Survey\tthe \r\n parser    module \n", "Survey the parser module")]
    [InlineData(null, "sub-agent")]
    [InlineData("", "sub-agent")]
    [InlineData(" \t\n ", "sub-agent")]
    public void Name_collapses_whitespace_and_trims(string? description, string expected)
    {
        // (35.1)
        Assert.Equal(expected, SubAgents.Name(description));
    }

    [Fact]
    public void Name_keeps_32_characters_unchanged()
    {
        var description = "abcdefghij abcdefghij abcdefghij";

        Assert.Equal(SubAgents.NameLength, description.Length);
        Assert.Equal(description, SubAgents.Name(description));
    }

    [Fact]
    public void Name_measures_after_collapsing_whitespace()
    {
        Assert.Equal("abcdefghij abcdefghij abcdefghij", SubAgents.Name("abcdefghij     abcdefghij\t\tabcdefghij"));
    }

    [Fact]
    public void Name_cuts_33_characters_at_a_space_at_index_25()
    {
        var description = Repeat('a', 25) + " " + Repeat('b', 7);

        Assert.Equal(33, description.Length);
        Assert.Equal(Repeat('a', 25) + "…", SubAgents.Name(description));
    }

    [Fact]
    public void Name_cuts_at_the_last_space_at_index_20_to_31()
    {
        Assert.Equal(Repeat('a', 21) + " " + Repeat('b', 4) + "…",
            SubAgents.Name(Repeat('a', 21) + " " + Repeat('b', 4) + " " + Repeat('c', 6)));
        Assert.Equal(Repeat('a', 20) + "…", SubAgents.Name(Repeat('a', 20) + " " + Repeat('b', 12)));
    }

    [Fact]
    public void Name_cuts_33_characters_without_a_space_at_index_20_to_31_at_index_31()
    {
        var description = Repeat('a', 10) + " " + Repeat('b', 22);

        Assert.Equal(33, description.Length);
        Assert.Equal(Repeat('a', 10) + " " + Repeat('b', 20) + "…", SubAgents.Name(description));
        Assert.Equal(Repeat('c', 31) + "…", SubAgents.Name(Repeat('c', 40)));
    }

    [Fact]
    public void Name_ignores_a_space_at_index_19()
    {
        var description = Repeat('a', 19) + " " + Repeat('b', 13);

        Assert.Equal(33, description.Length);
        Assert.Equal(Repeat('a', 19) + " " + Repeat('b', 11) + "…", SubAgents.Name(description));
    }

    [Theory]
    [InlineData("Check the public API surface of the alpha parser", "Check the public API surface of…")]
    [InlineData("A description that is a good deal longer than thirty-two characters", "A description that is a good…")]
    public void A_cut_name_ends_with_an_ellipsis_and_has_at_most_32_characters(string description, string expected)
    {
        var name = SubAgents.Name(description);

        Assert.Equal(expected, name);
        Assert.EndsWith("…", name, StringComparison.Ordinal);
        Assert.True(name.Length <= SubAgents.NameLength);
    }

    [Fact]
    public void Items_and_calls_of_null_are_the_agents_own()
    {
        Assert.Equal(["own 1", "own 2"], Texts(SubAgents.Items(Mixed, null)));
        Assert.Equal(["c1", "c3"], SubAgents.Calls(Mixed, null).Select(c => c.Id));
    }

    [Fact]
    public void Items_and_calls_of_an_id_are_that_sub_agents_only_in_order()
    {
        Assert.Equal(["a 1", "a 2"], Texts(SubAgents.Items(Mixed, "a")));
        Assert.Equal(["c2", "c5"], SubAgents.Calls(Mixed, "a").Select(c => c.Id));
        Assert.Equal(["b 1"], Texts(SubAgents.Items(Mixed, "b")));
        Assert.Equal(["c4"], SubAgents.Calls(Mixed, "b").Select(c => c.Id));
    }

    [Fact]
    public void Items_and_calls_of_an_unknown_id_are_empty()
    {
        Assert.Empty(SubAgents.Items(Mixed, "zzz"));
        Assert.Empty(SubAgents.Calls(Mixed, "zzz"));
        Assert.Empty(SubAgents.Calls(Mixed, "A"));   // ordinal
    }

    [Fact]
    public void Items_and_calls_of_default_arrays_are_empty()
    {
        var content = SessionContent.Empty with { Items = default, Calls = default };

        var items = SubAgents.Items(content, null);
        var calls = SubAgents.Calls(content, "a");

        Assert.False(items.IsDefault);
        Assert.Empty(items);
        Assert.False(calls.IsDefault);
        Assert.Empty(calls);
    }

    [Fact]
    public void Find_gives_the_sub_agent_with_the_id()
    {
        var first = Sub("a", toolCallId: "t1");
        var content = With(first, Sub("b"), Sub("a", toolCallId: "t2"));

        Assert.Same(first, SubAgents.Find(content, "a"));
        Assert.Equal("b", SubAgents.Find(content, "b")?.Id);
        Assert.Null(SubAgents.Find(content, "c"));
        Assert.Null(SubAgents.Find(content, null));
        Assert.Null(SubAgents.Find(SessionContent.Empty with { SubAgents = default }, "a"));
    }

    [Fact]
    public void Tree_puts_each_sub_agent_before_its_children_and_keeps_siblings_in_order()
    {
        // (35.4) Copilot sub-agents nest.
        var content = With(Sub("p1"), Sub("p2", "p1"), Sub("p3"), Sub("p4", "p2"), Sub("p5", "p1"));

        Assert.Equal(["p1", "p2", "p4", "p5", "p3"], Ids(SubAgents.Tree(content)));
    }

    [Fact]
    public void Tree_puts_a_sub_agent_whose_parent_is_unknown_at_the_top_level()
    {
        var content = With(Sub("x", "gone"), Sub("y"), Sub("z", "x"));

        Assert.Equal(["x", "z", "y"], Ids(SubAgents.Tree(content)));
        Assert.Equal(["x"], Ids(SubAgents.Lineage(content, "x")));
    }

    [Fact]
    public void Tree_ends_on_a_parent_cycle_with_each_sub_agent_once()
    {
        var content = With(Sub("a", "b"), Sub("b", "a"), Sub("c"), Sub("d", "a"));

        Assert.Equal(["c", "a", "b", "d"], Ids(SubAgents.Tree(content)));
        Assert.Equal(["a", "b"], Ids(SubAgents.Lineage(content, "b")));
        Assert.Equal(["s"], Ids(SubAgents.Tree(With(Sub("s", "s")))));
        Assert.Equal(["s"], Ids(SubAgents.Lineage(With(Sub("s", "s")), "s")));
    }

    [Fact]
    public void Tree_of_no_sub_agents_is_empty()
    {
        Assert.Empty(SubAgents.Tree(SessionContent.Empty));
        Assert.Empty(SubAgents.Tree(SessionContent.Empty with { SubAgents = default }));
    }

    [Fact]
    public void Lineage_runs_from_the_top_level_ancestor_down_to_the_id()
    {
        var content = With(Sub("p1"), Sub("q"), Sub("p2", "p1"), Sub("p3", "p2"));

        Assert.Equal(["p1", "p2", "p3"], Ids(SubAgents.Lineage(content, "p3")));
        Assert.Equal(["p1", "p2"], Ids(SubAgents.Lineage(content, "p2")));
        Assert.Equal(["q"], Ids(SubAgents.Lineage(content, "q")));
    }

    [Fact]
    public void Lineage_of_a_null_or_unknown_id_is_empty()
    {
        var content = With(Sub("p1"));

        Assert.Empty(SubAgents.Lineage(content, "zzz"));
        Assert.Empty(SubAgents.Lineage(content, null!));
        Assert.Empty(SubAgents.Lineage(SessionContent.Empty with { SubAgents = default }, "p1"));
    }

    [Fact]
    public void StartedBy_gives_the_sub_agent_whose_tool_call_id_is_the_calls_tool_id()
    {
        var content = With(Sub("agent-p1", toolCallId: "call-sub"), Sub("agent-p2", toolCallId: ""));

        Assert.Equal("agent-p1", SubAgents.StartedBy(content, Tool("call-sub"))?.Id);
        Assert.Null(SubAgents.StartedBy(content, Tool("call-other")));
    }

    [Fact]
    public void StartedBy_a_call_with_an_empty_tool_id_is_null()
    {
        // ToolCallId "" means unknown (35.4), so it must not match a call without a tool id.
        var content = With(Sub("agent-p1", toolCallId: ""));

        Assert.Null(SubAgents.StartedBy(content, Tool("")));
    }

    [Theory]
    [InlineData(SessionState.Running, SessionState.Running, SessionState.Running)]
    [InlineData(SessionState.Running, SessionState.Succeeded, SessionState.Aborted)]
    [InlineData(SessionState.Running, SessionState.Failed, SessionState.Aborted)]
    [InlineData(SessionState.Running, SessionState.Aborted, SessionState.Aborted)]
    [InlineData(SessionState.Succeeded, SessionState.Running, SessionState.Succeeded)]
    [InlineData(SessionState.Succeeded, SessionState.Aborted, SessionState.Succeeded)]
    [InlineData(SessionState.Failed, SessionState.Succeeded, SessionState.Failed)]
    public void StateOf_aborts_a_running_sub_agent_of_a_session_that_no_longer_runs(SessionState sub,
        SessionState session, SessionState expected)
    {
        var owner = SampleRun.Create().Sessions[0] with { State = session };

        Assert.Equal(expected, SubAgents.StateOf(owner, Sub("a", state: sub)));
    }
}

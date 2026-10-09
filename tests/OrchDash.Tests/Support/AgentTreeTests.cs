using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

// AgentTree of spec 4.3: the child rows of one or more sessions, with their depth and tree prefix.
public sealed class AgentTreeTests
{
    private static readonly ImmutableArray<Session> Sessions = SampleRun.Create().Sessions;

    private static Session With(int index, params SubAgent[] subAgents) =>
        Sessions[index] with { Content = SessionContent.Empty with { SubAgents = [.. subAgents] } };

    private static SubAgent Sub(string id, string? parentId = null) =>
        new(id, parentId, "", "Name " + id, "Description " + id, "Explore", null, false, "",
            SampleRun.At(12, 0, 0), null, SessionState.Running, null);

    private static (string Id, int Depth, string Prefix)[] Shape(ImmutableArray<AgentRow> rows) =>
        [.. rows.Select(r => (r.SubAgent.Id, r.Depth, r.Prefix))];

    [Fact]
    public void Prefixes_draw_the_tree()
    {
        var session = With(0, Sub("a"), Sub("b", "a"), Sub("c", "b"), Sub("d", "a"), Sub("e"), Sub("f", "e"), Sub("g", "e"));

        Assert.Equal(
        [
            ("a", 1, "├"),
            ("b", 2, "│ ├"),
            ("c", 3, "│ │ └"),
            ("d", 2, "│ └"),
            ("e", 1, "└"),
            ("f", 2, "  ├"),
            ("g", 2, "  └"),
        ],
        Shape(AgentTree.Rows([session])));
    }

    [Fact]
    public void Rows_follow_the_Tree_of_the_session()
    {
        var session = With(0, Sub("p1"), Sub("p2", "p1"), Sub("p3"), Sub("p4", "p2"), Sub("p5", "p1"));

        var rows = AgentTree.Rows([session]);

        Assert.Equal(SubAgents.Tree(session.Content), rows.Select(r => r.SubAgent));
        Assert.All(rows, r => Assert.Same(session, r.Session));
        Assert.All(rows, r => Assert.Equal(SubAgents.Lineage(session.Content, r.SubAgent.Id).Length, r.Depth));
        Assert.Equal(["├", "│ ├", "│ │ └", "│ └", "└"], rows.Select(r => r.Prefix));
    }

    [Fact]
    public void A_single_sub_agent_is_the_last_row()
    {
        Assert.Equal([("a", 1, "└")], Shape(AgentTree.Rows([With(0, Sub("a"))])));
    }

    [Fact]
    public void The_top_level_sub_agents_of_all_the_sessions_are_siblings()
    {
        var first = With(0, Sub("x"));
        var empty = With(1);
        var last = With(2, Sub("y"), Sub("z", "y"));

        var rows = AgentTree.Rows([first, empty, last]);

        Assert.Equal([("x", 1, "├"), ("y", 1, "└"), ("z", 2, "  └")], Shape(rows));
        Assert.Equal([first, last, last], rows.Select(r => r.Session));
    }

    [Fact]
    public void Sessions_come_in_the_given_order()
    {
        var first = With(0, Sub("x"));
        var last = With(2, Sub("y"), Sub("z", "y"));

        Assert.Equal([("y", 1, "├"), ("z", 2, "│ └"), ("x", 1, "└")], Shape(AgentTree.Rows([last, first])));
    }

    [Fact]
    public void Sessions_without_sub_agents_give_no_rows()
    {
        Assert.Empty(AgentTree.Rows([]));
        Assert.Empty(AgentTree.Rows(Sessions));
        Assert.Empty(AgentTree.Rows([Sessions[0] with { Content = SessionContent.Empty with { SubAgents = default } }]));
    }

    [Fact]
    public void A_sub_agent_whose_parent_is_unknown_is_top_level()
    {
        var session = With(0, Sub("x", "gone"), Sub("y", "x"));

        Assert.Equal([("x", 1, "└"), ("y", 2, "  └")], Shape(AgentTree.Rows([session])));
    }

    [Fact]
    public void A_parent_cycle_gives_each_sub_agent_one_row_after_the_others()
    {
        var session = With(0, Sub("a", "b"), Sub("b", "a"), Sub("c"), Sub("d", "a"));

        Assert.Equal([("c", 1, "├"), ("a", 1, "└"), ("b", 2, "  ├"), ("d", 2, "  └")], Shape(AgentTree.Rows([session])));
    }

    [Fact]
    public void Rows_of_the_sample_run_follow_snapshot_order()
    {
        var run = SampleRun.CreateSubAgents();

        Assert.Equal(
        [
            (SampleRun.PlannerKey, SampleRun.PlannerSub1Id, 1, "├"),
            (SampleRun.PlannerKey, SampleRun.PlannerSub2Id, 2, "│ └"),
            (SampleRun.PlannerKey, SampleRun.PlannerSub3Id, 1, "├"),
            (SampleRun.AlphaWorkerKey, SampleRun.AlphaSub1Id, 1, "├"),
            (SampleRun.AlphaWorkerKey, SampleRun.AlphaSub2Id, 1, "├"),
            (SampleRun.BetaWorkerKey, SampleRun.BetaSub1Id, 1, "└"),
        ],
        AgentTree.Rows(run.Sessions).Select(r => (r.Session.Files.Key, r.SubAgent.Id, r.Depth, r.Prefix)));
    }

    [Fact]
    public void A_planner_path_runs_through_its_tree()
    {
        var planner = SampleRun.CreateSubAgents().Sessions[0];

        Assert.Equal(
            ["planner #1 › Map the repo", "planner #1 › Map the repo › Read the spec", "planner #1 › Survey the tests"],
            AgentTree.Rows([planner]).Select(r => AgentPath.Of(r.Session, r.SubAgent.Id)));
    }
}

using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Timeline.Format;
using Xunit;

namespace OrchDash.Tests.Support;

// AgentPath of spec 4.3: the session's Timeline source, then the names of the sub-agent's lineage.
public sealed class AgentPathTests
{
    private readonly RunSnapshot _run = SampleRun.CreateSubAgents();

    private Session ByKey(string key) => _run.Sessions.Single(s => s.Files.Key == key);

    private Session Planner => ByKey(SampleRun.PlannerKey);

    [Fact]
    public void The_separator_is_a_spaced_angle_quote()
    {
        Assert.Equal(" › ", AgentPath.Separator);
    }

    [Fact]
    public void A_session_path_is_task_role_and_attempt()
    {
        Assert.Equal(
        [
            "planner #1",
            "alpha worker #1",
            "gamma worker #1",
            "alpha reviewer #1.1",
            "beta worker #1",
            "gamma worker #2",
        ],
        _run.Sessions.Select(AgentPath.Of));
    }

    [Fact]
    public void A_session_path_is_the_Timeline_source_of_its_events()
    {
        Assert.All(_run.Sessions, session =>
        {
            var prompt = new TimelineEvent(session.Files.Key + ":prompt", session.StartedAt!.Value, TimelineKind.Prompt,
                session.Files.TaskId ?? "planner", 0, null, session, null, null, null);

            Assert.Equal(TimelineText.Source(prompt), AgentPath.Of(session));
        });
    }

    [Fact]
    public void A_bootstrap_session_has_no_task_and_a_nudge_says_so()
    {
        var alpha = ByKey(SampleRun.AlphaWorkerKey);
        var bootstrap = alpha with { Files = alpha.Files with { TaskId = null, Role = AgentRole.Bootstrap, Attempt = 2 } };
        var nudge = alpha with { Files = alpha.Files with { IsNudge = true } };

        Assert.Equal("bootstrap #2", AgentPath.Of(bootstrap));
        Assert.Equal("alpha worker #1 nudge", AgentPath.Of(nudge));
    }

    [Fact]
    public void A_null_or_unknown_agent_id_gives_the_session_path()
    {
        var alpha = ByKey(SampleRun.AlphaWorkerKey);

        Assert.Equal("alpha worker #1", AgentPath.Of(alpha, null));
        Assert.Equal("alpha worker #1", AgentPath.Of(alpha, "toolu_unknown"));
        Assert.Equal("alpha worker #1", AgentPath.Of(alpha, SampleRun.PlannerSub1Id));   // another session's sub-agent
        Assert.Equal("gamma worker #1", AgentPath.Of(ByKey(SampleRun.GammaWorker1Key), SampleRun.AlphaSub1Id));
    }

    [Fact]
    public void A_top_level_sub_agent_follows_its_session()
    {
        Assert.Equal("alpha worker #1 › Survey the parser module", AgentPath.Of(ByKey(SampleRun.AlphaWorkerKey), SampleRun.AlphaSub1Id));
        Assert.Equal("beta worker #1 › Survey CLI flags", AgentPath.Of(ByKey(SampleRun.BetaWorkerKey), SampleRun.BetaSub1Id));
        Assert.Equal("planner #1 › Map the repo", AgentPath.Of(Planner, SampleRun.PlannerSub1Id));
        Assert.Equal("planner #1 › Survey the tests", AgentPath.Of(Planner, SampleRun.PlannerSub3Id));
    }

    [Fact]
    public void A_path_uses_the_cut_name()
    {
        Assert.Equal("alpha worker #1 › Check the public API surface of…",
            AgentPath.Of(ByKey(SampleRun.AlphaWorkerKey), SampleRun.AlphaSub2Id));
    }

    [Fact]
    public void A_nested_sub_agent_follows_its_lineage()
    {
        Assert.Equal("planner #1 › Map the repo › Read the spec", AgentPath.Of(Planner, SampleRun.PlannerSub2Id));
    }

    [Fact]
    public void A_deep_sub_agent_names_every_ancestor()
    {
        var planner = Planner with
        {
            Content = SessionContent.Empty with
            {
                SubAgents = [Sub("c", "b", "Third"), Sub("a", null, "First"), Sub("b", "a", "Second")],
            },
        };

        Assert.Equal("planner #1 › First › Second › Third", AgentPath.Of(planner, "c"));
    }

    private static SubAgent Sub(string id, string? parentId, string name) =>
        new(id, parentId, "", name, name, "explore", null, false, "", null, null, SessionState.Running, null);
}

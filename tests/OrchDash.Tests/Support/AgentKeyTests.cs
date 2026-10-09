using OrchDash.Contracts;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

// AgentKey of spec 4.3: the selected session key, with or without a sub-agent.
public sealed class AgentKeyTests
{
    private readonly Session _alpha = SampleRun.CreateSubAgents().Sessions.Single(s => s.Files.Key == SampleRun.AlphaWorkerKey);

    [Fact]
    public void The_separator_is_a_bar()
    {
        Assert.Equal('|', AgentKey.Separator);
    }

    [Fact]
    public void Of_null_is_the_session_key()
    {
        Assert.Equal(SampleRun.AlphaWorkerKey, AgentKey.Of(_alpha, null));
    }

    [Fact]
    public void Of_an_id_is_the_session_key_a_bar_and_the_id()
    {
        Assert.Equal("alpha/20261003-120005/attempt-1-worker.json|toolu_alpha_sub1", AgentKey.Of(_alpha, SampleRun.AlphaSub1Id));
        Assert.Equal("planner-20261003-115900-1.json|agent-p2",
            AgentKey.Of(SampleRun.CreateSubAgents().Sessions[0], SampleRun.PlannerSub2Id));
    }

    [Fact]
    public void Parse_null_is_null_and_null()
    {
        Assert.Equal((null, null), AgentKey.Parse(null));
    }

    [Theory]
    [InlineData("alpha/20261003-120005/attempt-1-worker.json")]
    [InlineData("planner-20261003-115900-1.json")]
    [InlineData("")]
    public void Parse_a_key_without_a_bar_is_the_session_key_alone(string key)
    {
        Assert.Equal((key, null), AgentKey.Parse(key));
    }

    [Theory]
    [InlineData("alpha/x.json|toolu_alpha_sub1", "alpha/x.json", "toolu_alpha_sub1")]
    [InlineData("alpha/x.json|a|b", "alpha/x.json", "a|b")]
    [InlineData("alpha/x.json|", "alpha/x.json", "")]
    [InlineData("|agent-p1", "", "agent-p1")]
    public void Parse_splits_at_the_first_bar(string key, string sessionKey, string agentId)
    {
        Assert.Equal((sessionKey, agentId), AgentKey.Parse(key));
    }

    [Fact]
    public void Parse_gives_back_what_Of_put_together()
    {
        Assert.Equal((SampleRun.AlphaWorkerKey, null), AgentKey.Parse(AgentKey.Of(_alpha, null)));
        Assert.Equal((SampleRun.AlphaWorkerKey, SampleRun.AlphaSub2Id), AgentKey.Parse(AgentKey.Of(_alpha, SampleRun.AlphaSub2Id)));
    }
}

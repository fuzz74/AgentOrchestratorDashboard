using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.MergeData;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 14.2 and the session id rules in section 4.3.
public sealed class ProviderMergeIdTests
{
    private readonly FakeSessionStore _folders = new();
    private readonly FakeSessionIdFinder _finder = new((_, _, _, _) => "found");

    private ProviderStores Stores => new(null, _folders, _finder, null);

    private static string?[] IdsOf(MergeResult result) => [.. result.Sessions.Select(s => s.Content.SessionId)];

    [Fact]
    public void A_nudge_takes_the_id_of_the_session_it_nudged()
    {
        var result = Merge(Stores,
            CopilotSession(Files("alpha", AgentRole.Reviewer, reviewTry: 1), "r-1"),
            CopilotSession(Files("alpha", AgentRole.Reviewer, reviewTry: 2), "r-2"),
            CopilotSession(Files("alpha", AgentRole.Reviewer, reviewTry: 2, isNudge: true), null, startedAt: At(12, 3, 0)));

        Assert.Equal(["r-1", "r-2", "r-2"], IdsOf(result));
        Assert.Empty(_finder.Calls);
        Assert.Equal(["r-1", "r-2", "r-2"], _folders.Calls.Select(c => c.SessionId));
    }

    [Fact]
    public void A_nudge_needs_the_same_start_folder_and_attempt()
    {
        var result = Merge(new ProviderStores(null, _folders, null, null),
            CopilotSession(Files("alpha", AgentRole.Reviewer, startFolder: "20261003-110000"), "r-old"),
            CopilotSession(Files("alpha", AgentRole.Reviewer, attempt: 2), "r-2"),
            CopilotSession(Files("alpha", AgentRole.Reviewer, isNudge: true), null));

        Assert.Null(IdsOf(result)[2]);
    }

    [Fact]
    public void A_later_attempt_takes_the_id_of_the_attempt_before_it()
    {
        var result = Merge(Stores,
            CopilotSession(Files("alpha", attempt: 1, isNudge: true), "p-nudge"),
            CopilotSession(Files("alpha", attempt: 1, startFolder: "20261003-110000"), "p-old"),
            CopilotSession(Files("alpha", attempt: 1), "p-1"),
            CopilotSession(Files("alpha", attempt: 2), null, startedAt: At(12, 5, 0)));

        Assert.Equal(["p-nudge", "p-old", "p-1", "p-1"], IdsOf(result));
        Assert.Empty(_finder.Calls);
    }

    [Fact]
    public void Only_a_worker_takes_the_id_of_the_attempt_before_it()
    {
        var result = Merge(new ProviderStores(null, _folders, null, null),
            CopilotSession(Files("alpha", AgentRole.Resolver, attempt: 1), "s-1"),
            CopilotSession(Files("alpha", AgentRole.Resolver, attempt: 2), null));

        Assert.Null(IdsOf(result)[1]);
    }

    [Fact]
    public void Attempt_three_takes_the_id_from_two_from_one_in_any_order()
    {
        var result = Merge(Stores,
            CopilotSession(Files("alpha", attempt: 3), null, startedAt: At(12, 9, 0)),
            CopilotSession(Files("alpha", attempt: 3, isNudge: true), null, startedAt: At(12, 9, 30)),
            CopilotSession(Files("alpha", attempt: 2), null, startedAt: At(12, 5, 0)),
            CopilotSession(Files("alpha", attempt: 1), "p-1", startedAt: At(12, 1, 0)));

        Assert.Equal(["p-1", "p-1", "p-1", "p-1"], IdsOf(result));
        Assert.Empty(_finder.Calls);
    }

    [Fact]
    public void A_chain_ends_in_one_find_for_the_first_attempt()
    {
        var result = Merge(Stores,
            CopilotSession(Files("alpha", attempt: 3), null, startedAt: At(12, 9, 0)),
            CopilotSession(Files("alpha", attempt: 2), null, startedAt: At(12, 5, 0)),
            CopilotSession(Files("alpha", attempt: 1), null, startedAt: At(12, 1, 0)));

        Assert.Equal(["found", "found", "found"], IdsOf(result));
        var call = Assert.Single(_finder.Calls);
        Assert.Equal(At(12, 1, 0), call.StartedAt);
    }

    [Fact]
    public void A_session_finds_its_own_id_when_its_rule_gives_none()
    {
        var result = Merge(Stores,
            CopilotSession(Files("alpha", attempt: 1), null),
            CopilotSession(Files("alpha", attempt: 2), null, startedAt: At(12, 5, 0)));

        Assert.Equal([null, "found"], IdsOf(result));
        Assert.Equal(At(12, 5, 0), Assert.Single(_finder.Calls).StartedAt);
    }

    [Theory]
    [InlineData("alpha", AgentRole.Worker, "orch:alpha", @"C:\work\alpha")]
    [InlineData("alpha", AgentRole.Reviewer, "orch:alpha:review", @"C:\work\alpha")]
    [InlineData("alpha", AgentRole.Resolver, "orch:alpha:resolve", @"C:\work\alpha")]
    [InlineData(null, AgentRole.Planner, "orch:planner", @"C:\work\repo")]
    [InlineData(null, AgentRole.Bootstrap, "orch:bootstrap", @"C:\work\repo")]
    public void Find_gets_the_name_work_dir_start_and_the_parsers_ids(string? taskId, AgentRole role, string name, string workDir)
    {
        var result = Merge(Stores,
            ClaudeSession(Files("beta"), "c-1"),
            CopilotSession(Files("gamma"), "p-1"),
            UnknownSession(Files("delta")),
            CopilotSession(Files(taskId, role), null, startedAt: At(12, 3, 0)),
            CopilotSession(Files("epsilon"), null));

        var call = Assert.Single(_finder.Calls);
        Assert.Equal(name, call.Name);
        Assert.Equal(workDir, call.WorkDir);
        Assert.Equal(At(12, 3, 0), call.StartedAt);
        Assert.Equal(["c-1", "p-1"], call.KnownIds);
        Assert.Equal("found", result.Sessions[3].Content.SessionId);
    }

    [Fact]
    public void A_found_id_is_set_on_the_content_alone()
    {
        var session = CopilotSession(Files("alpha"), null, [Call("x1", At(12, 3, 1))], startedAt: At(12, 3, 0));

        var merged = Assert.Single(Merge(Stores, session).Sessions);

        Assert.Equal(session.Content with { SessionId = "found" }, merged.Content);
        Assert.Equal([("found", @"C:\work\alpha")], _folders.Calls);
    }

    [Fact]
    public void There_is_no_find_without_a_start()
    {
        var merged = Assert.Single(Merge(Stores, CopilotSession(Files("alpha"), null)).Sessions);

        Assert.Empty(_finder.Calls);
        Assert.Null(merged.Content.SessionId);
    }

    [Fact]
    public void There_is_no_find_without_CopilotIds_but_the_other_rules_apply()
    {
        var result = Merge(new ProviderStores(null, null, null, new FakeUsageReader()),
            CopilotSession(Files("alpha", attempt: 1), "p-1"),
            CopilotSession(Files("alpha", attempt: 2), null),
            CopilotSession(Files("beta"), null, startedAt: At(12, 3, 0)));

        Assert.Equal(["p-1", "p-1", null], IdsOf(result));
    }

    [Fact]
    public void A_Claude_session_without_an_id_gets_none()
    {
        var transcripts = new FakeSessionStore();

        var result = Merge(new ProviderStores(transcripts, _folders, _finder, null),
            ClaudeSession(Files("alpha", attempt: 1), "c-1"),
            ClaudeSession(Files("alpha", attempt: 2), null, startedAt: At(12, 5, 0)));

        Assert.Equal(["c-1", null], IdsOf(result));
        Assert.Empty(_finder.Calls);
        Assert.Equal(["c-1"], transcripts.Calls.Select(c => c.SessionId));
    }
}

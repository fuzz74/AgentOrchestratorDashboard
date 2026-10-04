using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Store.MergeData;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 14.1 and 14.9: no sources, and each provider's sources called for its own sessions only.
public sealed class ProviderMergeSourceTests
{
    private readonly FakeSessionStore _transcripts = new((_, _) => Stored("2.1.3"));
    private readonly FakeSessionStore _folders = new((_, _) => Stored("1.0.80"));
    private readonly FakeSessionIdFinder _finder = new((_, _, _, _) => "found");
    private readonly FakeUsageReader _usage = new(_ => Rows(9, "Copilot database: locked"));

    private ProviderStores All => new(_transcripts, _folders, _finder, _usage);

    private static Session[] Sample() =>
    [
        ClaudeSession(Files("alpha"), "c-1", [Call("m1", At(12, 0, 10))], init: Init(cliVersion: "2.1.3")),
        CopilotSession(Files("beta"), null, [Call("x1", At(12, 1, 0))], startedAt: At(12, 1, 0)),
        UnknownSession(Files("gamma")),
    ];

    public static TheoryData<string> NoSources => ["null", "None", "four nulls"];

    [Theory]
    [MemberData(nameof(NoSources))]
    public void Without_a_source_the_sessions_are_the_same_instances(string which)
    {
        var stores = which switch
        {
            "null" => null,
            "None" => ProviderStores.None,
            _ => new ProviderStores(null, null, null, null),
        };
        var sessions = Sample();

        var result = Merge(stores, sessions);

        Assert.Equal(sessions.Length, result.Sessions.Length);
        for (int i = 0; i < sessions.Length; i++)
            Assert.Same(sessions[i], result.Sessions[i]);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void A_run_without_Copilot_sessions_calls_no_Copilot_source()
    {
        Merge(All,
            ClaudeSession(Files("alpha"), "c-1"),
            ClaudeSession(Files("alpha", attempt: 2), null, startedAt: At(12, 5, 0)),
            UnknownSession(Files("gamma")));

        Assert.Equal(["c-1"], _transcripts.Calls.Select(c => c.SessionId));
        Assert.Empty(_folders.Calls);
        Assert.Empty(_finder.Calls);
        Assert.Empty(_usage.Calls);
    }

    [Fact]
    public void A_run_without_Claude_sessions_calls_no_Claude_source()
    {
        Merge(All, CopilotSession(Files("beta"), "p-1"), UnknownSession(Files("gamma")));

        Assert.Empty(_transcripts.Calls);
        Assert.Equal(["p-1"], _folders.Calls.Select(c => c.SessionId));
        Assert.Equal(["p-1"], Assert.Single(_usage.Calls));
    }

    [Fact]
    public void Each_source_is_called_for_its_own_provider_only()
    {
        var result = Merge(All, Sample());

        Assert.Equal([("c-1", @"C:\work\alpha")], _transcripts.Calls);
        Assert.Equal([("found", @"C:\work\beta")], _folders.Calls);
        Assert.Equal("orch:beta", Assert.Single(_finder.Calls).Name);
        Assert.Equal(["found"], Assert.Single(_usage.Calls));
        Assert.Equal(
        [
            "Copilot database: locked",
            "Claude Code 2.1.3: OrchDash was made for 2.1.285",
            "Copilot CLI 1.0.80: OrchDash was made for 1.0.91",
            "Copilot database schema 9: OrchDash was made for 8",
        ], result.Problems);
    }

    [Fact]
    public void The_sessions_keep_their_order()
    {
        var sessions = Sample();

        var result = Merge(All, sessions);

        Assert.Equal(sessions.Select(s => s.Files.Key), result.Sessions.Select(s => s.Files.Key));
        Assert.Same(sessions[2], result.Sessions[2]);
    }
}

using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Store.MergeData;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 14.6 and the reasons table in section 4.3.
public sealed class ProviderMergeReasonTests
{
    private static Session CopilotWithCall() => CopilotSession(Files("alpha"), "p-1", [Call("x1", At(12, 1, 0))]);

    [Fact]
    public void A_Claude_session_without_a_transcript()
    {
        var merged = Assert.Single(Merge(new ProviderStores(new FakeSessionStore(), null, null, null), ClaudeSession(Files("alpha"))).Sessions);

        Assert.Equal(["no transcript"], merged.Unavailable);
        Assert.Same(StoreData.Empty, merged.Stores);
    }

    [Fact]
    public void A_Copilot_session_without_a_session_folder()
    {
        var merged = Assert.Single(Merge(new ProviderStores(null, new FakeSessionStore(), null, null), CopilotWithCall()).Sessions);

        Assert.Equal(["no session folder"], merged.Unavailable);
        Assert.Same(StoreData.Empty, merged.Stores);
    }

    [Fact]
    public void A_Copilot_session_without_database_rows()
    {
        var stores = new ProviderStores(null, new FakeSessionStore((_, _) => Stored()), null,
            new FakeUsageReader(_ => Rows(("p-other", [Figures(time: At(12, 1, 1))]))));

        var merged = Assert.Single(Merge(stores, CopilotWithCall()).Sessions);

        Assert.Equal(["no database rows"], merged.Unavailable);
    }

    [Fact]
    public void A_Copilot_session_without_rows_from_its_first_call_on_has_no_database_rows()
    {
        var stores = new ProviderStores(null, null, null, new FakeUsageReader(_ => Rows(("p-1", [Figures(time: At(12, 0, 59))]))));

        var merged = Assert.Single(Merge(stores, CopilotWithCall()).Sessions);

        Assert.Equal(["no database rows"], merged.Unavailable);
        Assert.Empty(merged.Stores.Calls);
    }

    [Fact]
    public void The_reasons_come_in_the_order_of_the_table()
    {
        var stores = new ProviderStores(new FakeSessionStore(), new FakeSessionStore(), new FakeSessionIdFinder(), new FakeUsageReader());

        var merged = Merge(stores, CopilotWithCall(), ClaudeSession(Files("beta"))).Sessions;

        Assert.Equal(["no session folder", "no database rows"], merged[0].Unavailable);
        Assert.Equal(["no transcript"], merged[1].Unavailable);
    }

    [Fact]
    public void An_unknown_session_id_is_the_only_reason()
    {
        var folders = new FakeSessionStore();
        var transcripts = new FakeSessionStore();
        var usage = new FakeUsageReader();
        var stores = new ProviderStores(transcripts, folders, new FakeSessionIdFinder(), usage);

        var merged = Merge(stores, CopilotSession(Files("alpha"), null), ClaudeSession(Files("beta"), null)).Sessions;

        Assert.Equal(["session id not known yet"], merged[0].Unavailable);
        Assert.Equal(["session id not known yet"], merged[1].Unavailable);
        Assert.Same(StoreData.Empty, merged[0].Stores);
        Assert.Empty(folders.Calls);
        Assert.Empty(transcripts.Calls);
        Assert.Empty(usage.Calls);
    }

    [Fact]
    public void An_unknown_provider_has_no_reason()
    {
        var session = UnknownSession(Files("alpha"));
        var stores = new ProviderStores(new FakeSessionStore(), new FakeSessionStore(), new FakeSessionIdFinder(), new FakeUsageReader());

        Assert.Same(session, Assert.Single(Merge(stores, session).Sessions));
    }

    [Fact]
    public void A_provider_without_its_sources_has_no_reason()
    {
        var claude = ClaudeSession(Files("alpha"), null);
        var copilot = CopilotSession(Files("beta"), null, startedAt: At(12, 1, 0));

        var forCopilot = Merge(new ProviderStores(null, new FakeSessionStore(), new FakeSessionIdFinder(), new FakeUsageReader()), claude);
        var forClaude = Merge(new ProviderStores(new FakeSessionStore(), null, null, null), copilot);

        Assert.Same(claude, Assert.Single(forCopilot.Sessions));
        Assert.Same(copilot, Assert.Single(forClaude.Sessions));
    }

    [Fact]
    public void A_source_that_is_not_set_gives_no_reason_of_its_own()
    {
        var copilot = CopilotSession(Files("alpha"), "p-1");

        var merged = Merge(new ProviderStores(new FakeSessionStore(), null, new FakeSessionIdFinder(), null), copilot);

        Assert.Same(copilot, Assert.Single(merged.Sessions));
    }
}

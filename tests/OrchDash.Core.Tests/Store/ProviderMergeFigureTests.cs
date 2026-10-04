using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Store.MergeData;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 14.3-14.5: store data, usage rows and the figures copied into the model calls.
public sealed class ProviderMergeFigureTests
{
    private static readonly TokenUsage LogUsage = new(1, 2, 3, null);

    [Fact]
    public void A_Claude_call_takes_the_figures_with_its_id()
    {
        var calls = new[] { Call("m1", At(12, 0, 10), LogUsage), Call("m2", At(12, 0, 20), LogUsage), Call("m3", At(12, 0, 30), LogUsage) };
        var session = ClaudeSession(Files("alpha"), "c-1", calls, init: Init(cwd: @"C:\cwd\alpha"));
        var transcript = Stored(calls:
        [
            Figures("m2", At(12, 0, 21), input: 7, output: 50, thinking: 7, stopReason: "end_turn"),
            Figures("m1", At(12, 0, 11), input: 6, output: 40, thinking: 3, stopReason: "tool_use"),
        ]);
        var transcripts = new FakeSessionStore((_, _) => transcript);

        var merged = Assert.Single(Merge(new ProviderStores(transcripts, null, null, null), session).Sessions);

        Assert.Equal([("c-1", @"C:\cwd\alpha")], transcripts.Calls);
        Assert.Same(transcript, merged.Stores);
        Assert.Empty(merged.Unavailable);
        var first = merged.Content.Calls[0];
        Assert.Equal(new TokenUsage(6, 100, 20, 40), first.Usage);
        Assert.Equal(3, first.ThinkingTokens);
        Assert.Equal("tool_use", first.StopReason);
        Assert.Equal(At(12, 0, 10), first.StartedAt);
        Assert.Null(first.NanoAiu);
        Assert.Null(first.Duration);
        var second = merged.Content.Calls[1];
        Assert.Equal(new TokenUsage(7, 100, 20, 50), second.Usage);
        Assert.Equal(7, second.ThinkingTokens);
        Assert.Equal("end_turn", second.StopReason);
        Assert.Same(calls[2], merged.Content.Calls[2]);
        Assert.Equal(session.Content with { Calls = merged.Content.Calls }, merged.Content);
    }

    [Fact]
    public void A_Claude_transcript_is_read_in_the_work_dir_without_an_init_cwd()
    {
        var transcripts = new FakeSessionStore();

        Merge(new ProviderStores(transcripts, null, null, null),
            ClaudeSession(Files("alpha"), "c-1"),
            ClaudeSession(Files("beta"), "c-2", init: Init(cwd: null)));

        Assert.Equal([("c-1", @"C:\work\alpha"), ("c-2", @"C:\work\beta")], transcripts.Calls);
    }

    [Fact]
    public void A_session_without_any_figure_keeps_its_content()
    {
        var claude = ClaudeSession(Files("alpha"), "c-1", [Call("m1", At(12, 0, 10), LogUsage)]);
        var copilot = CopilotSession(Files("beta"), "p-1", [Call("x1", At(12, 0, 10), LogUsage)]);
        var transcript = Stored(calls: [Figures("other")]);
        var folder = Stored(TestedVersions.CopilotCli);

        var merged = Merge(new ProviderStores(new FakeSessionStore((_, _) => transcript), new FakeSessionStore((_, _) => folder), null, null),
            claude, copilot).Sessions;

        Assert.Same(claude.Content, merged[0].Content);
        Assert.Same(transcript, merged[0].Stores);
        Assert.Same(copilot.Content, merged[1].Content);
        Assert.Same(folder, merged[1].Stores);
    }

    [Fact]
    public void A_Copilot_call_takes_the_row_at_its_position_and_a_null_figure_keeps_its_own_value()
    {
        var own = Call("x1", At(12, 1, 0), new TokenUsage(1, 2, 3, 9)) with { ThinkingTokens = 4, StopReason = "own" };
        var session = CopilotSession(Files("alpha"), "p-1", [own, Call("x2", At(12, 1, 10))]);
        var folder = Stored(TestedVersions.CopilotCli);
        var folders = new FakeSessionStore((_, _) => folder);
        CallFigures[] rows =
        [
            Figures(time: At(12, 1, 1), input: 11, output: null, nanoAiu: 100, duration: TimeSpan.FromSeconds(2)),
            Figures(time: At(12, 1, 11), input: 12, output: 7, thinking: 2, nanoAiu: 200, duration: TimeSpan.FromSeconds(3), stopReason: "stop"),
        ];
        var usage = new FakeUsageReader(_ => Rows(("p-1", rows)));

        var merged = Assert.Single(Merge(new ProviderStores(null, folders, null, usage), session).Sessions);

        Assert.Equal([("p-1", @"C:\work\alpha")], folders.Calls);
        Assert.Equal(rows, merged.Stores.Calls);
        Assert.Equal(folder.SystemPrompt, merged.Stores.SystemPrompt);
        Assert.Equal(TestedVersions.CopilotCli, merged.Stores.CliVersion);
        Assert.Empty(merged.Unavailable);
        var first = merged.Content.Calls[0];
        Assert.Equal(new TokenUsage(11, 100, 20, 9), first.Usage);
        Assert.Equal(4, first.ThinkingTokens);
        Assert.Equal("own", first.StopReason);
        Assert.Equal(100, first.NanoAiu);
        Assert.Equal(TimeSpan.FromSeconds(2), first.Duration);
        var second = merged.Content.Calls[1];
        Assert.Equal(new TokenUsage(12, 100, 20, 7), second.Usage);
        Assert.Equal(2, second.ThinkingTokens);
        Assert.Equal("stop", second.StopReason);
        Assert.Equal(200, second.NanoAiu);
        Assert.Equal(TimeSpan.FromSeconds(3), second.Duration);
    }

    [Fact]
    public void A_Copilot_session_gets_the_rows_without_a_session_folder()
    {
        CallFigures[] rows = [Figures(time: At(12, 1, 1))];
        var session = CopilotSession(Files("alpha"), "p-1", [Call("x1", At(12, 1, 0))]);

        var merged = Assert.Single(Merge(new ProviderStores(null, null, null, new FakeUsageReader(_ => Rows(("p-1", rows)))), session).Sessions);

        Assert.Equal(rows, merged.Stores.Calls);
        Assert.Equal(StoreData.Empty with { Calls = merged.Stores.Calls }, merged.Stores);
        Assert.Equal(rows[0].Usage, merged.Content.Calls[0].Usage);
    }

    [Fact]
    public void A_resumed_worker_gets_the_rows_from_its_first_call_on_cut_to_its_calls()
    {
        CallFigures[] rows =
        [
            Figures(time: At(12, 1, 1), input: 1), Figures(time: At(12, 1, 11), input: 2), Figures(time: At(12, 1, 30), input: 3),
            Figures(time: At(12, 5, 1), input: 4), Figures(time: At(12, 5, 11), input: 5), Figures(time: At(12, 5, 21), input: 6),
        ];
        var attempt1 = CopilotSession(Files("alpha", attempt: 1), "p-1", [Call("a", At(12, 1, 0)), Call("b", At(12, 1, 10))]);
        var attempt2 = CopilotSession(Files("alpha", attempt: 2), null,
            [Call("c", At(12, 5, 0)), Call("d", At(12, 5, 10)), Call("e", At(12, 5, 20))]);
        var usage = new FakeUsageReader(_ => Rows(("p-1", rows)));

        var merged = Merge(new ProviderStores(null, null, null, usage), attempt1, attempt2).Sessions;

        Assert.Equal(["p-1"], Assert.Single(usage.Calls));
        Assert.Equal(rows[..2], merged[0].Stores.Calls);
        Assert.Equal("p-1", merged[1].Content.SessionId);
        Assert.Equal(rows[3..], merged[1].Stores.Calls);
        Assert.Equal([4L, 5L, 6L], merged[1].Content.Calls.Select(c => c.Usage!.Input));
    }

    [Fact]
    public void All_rows_count_when_the_first_call_has_no_start()
    {
        CallFigures[] rows = [Figures(time: At(12, 1, 1), input: 1), Figures(time: At(12, 1, 11), input: 2), Figures(time: At(12, 5, 1), input: 3)];
        var attempt1 = CopilotSession(Files("alpha", attempt: 1), "p-1", [Call("a", At(12, 1, 0))]);
        var attempt2 = CopilotSession(Files("alpha", attempt: 2), null, [Call("c"), Call("d", At(12, 5, 10))]);

        var merged = Merge(new ProviderStores(null, null, null, new FakeUsageReader(_ => Rows(("p-1", rows)))), attempt1, attempt2).Sessions;

        Assert.Equal(rows[..1], merged[0].Stores.Calls);
        Assert.Equal(rows[..2], merged[1].Stores.Calls);
    }

    [Fact]
    public void The_database_is_read_once_per_apply_with_the_distinct_Copilot_ids()
    {
        var usage = new FakeUsageReader();

        Merge(new ProviderStores(new FakeSessionStore(), new FakeSessionStore(), new FakeSessionIdFinder(), usage),
            CopilotSession(Files("alpha", attempt: 1), "p-1"),
            ClaudeSession(Files("beta"), "c-1"),
            CopilotSession(Files("alpha", attempt: 2), null),
            CopilotSession(Files("gamma"), "p-2"),
            CopilotSession(Files("gamma", AgentRole.Reviewer), "p-1"));

        Assert.Equal(["p-1", "p-2"], Assert.Single(usage.Calls));
    }

    [Fact]
    public void The_database_is_not_read_while_no_Copilot_session_has_an_id()
    {
        var usage = new FakeUsageReader();

        var merged = Assert.Single(Merge(new ProviderStores(null, null, null, usage), CopilotSession(Files("alpha"), null)).Sessions);

        Assert.Empty(usage.Calls);
        Assert.Equal(["session id not known yet"], merged.Unavailable);
    }
}

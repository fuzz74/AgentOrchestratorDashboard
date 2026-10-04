using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.App;

// End to end through AppRunner.CreateStore on the fixture store folders: the 'claude-run session' and 'copilot-run
// session' tables of spec 4.3, row by row, on the sessions of one Poll(). The provider data reach the sessions through
// the real readers and the store's merge (14.3-14.5).
public sealed class FixtureProviderFactsTests
{
    private const string CoreWorker = "core/20261003-113444/attempt-1-worker.json";

    [Theory]
    [InlineData("bootstrap-20261001-100433/attempt-1.json", 8, 44589, 51999, 5361, 11, 15, 20, 0.5906472, 1, 1, 0.15, 0.26)]
    [InlineData("audio-synth/20261001-104634/attempt-1-worker.json", 8, 46798, 67408, 17093, 11, 15, 18, 0.8224428, 602, 32, 0.2, 0.27)]
    [InlineData("audio-synth/20261001-104634/attempt-1-review-1.json", 2, 43206, 45873, 3333, 11, 28, 7, 0.3348476, 0, 0, 0.21, 0.27)]
    public void A_claude_run_session_has_the_facts_of_its_row(string key, int calls, long firstContext, long lastContext,
        long outputSum, int blocks, int tools, int injected, double cost, int added, int removed, double fiveHour, double sevenDay)
    {
        var session = SessionOf(PollTwice(FixtureRuns.ClaudeRepo), key);

        Assert.Equal(Provider.Claude, session.Provider);
        var modelCalls = session.Content.Calls;
        Assert.Equal(calls, modelCalls.Length);
        Assert.Equal(firstContext, modelCalls[0].Usage?.Context);
        Assert.Equal(lastContext, modelCalls[^1].Usage?.Context);
        // The log's own usage has no exact output for every call; the transcript's figures give each call one.
        Assert.All(modelCalls, call => Assert.NotNull(call.Usage?.Output));
        Assert.Equal(outputSum, modelCalls.Sum(call => call.Usage!.Output!.Value));

        var stores = session.Stores;
        Assert.Equal(blocks, stores.SystemPrompt.Length);
        Assert.Equal(tools, stores.Tools.Length);
        Assert.Equal(injected, stores.Injected.Length);
        Assert.Equal(cost, stores.CostUsd);
        Assert.Equal(added, stores.LinesAdded);
        Assert.Equal(removed, stores.LinesRemoved);
        Assert.Equal(TestedVersions.ClaudeCode, stores.CliVersion);

        var rateLimit = session.Content.RateLimit;
        Assert.NotNull(rateLimit);
        Assert.Equal(fiveHour, rateLimit.FiveHourUsed);
        Assert.Equal(sevenDay, rateLimit.SevenDayUsed);
    }

    [Theory]
    [InlineData("bootstrap-20261003-110028/attempt-1.json", 7, 11200, 13490, 1619, 6476050000, 1094, 3, 19, 4254)]
    [InlineData("planner-20261003-110111-1.json", 4, 9432, 10478, 2666, 5862990000, 203, 1, 19, 3210)]
    [InlineData(CoreWorker, 10, 12069, 19104, 5267, 12930990000, 1996, 8, 19, 4321)]
    [InlineData("core/20261003-113444/attempt-1-review-1.json", 1, 14097, 14097, 208, 3732100000, 680, 3, 19, 3282)]
    [InlineData("count/20261003-114955/attempt-1-worker.json", 9, 12164, 16607, 2366, 8824860000, 1996, 8, 19, 4325)]
    [InlineData("count/20261003-114955/attempt-1-review-1.json", 2, 11097, 13048, 458, 3942330000, 680, 3, 19, 3279)]
    [InlineData("count/20261003-115046/attempt-1-resolver.json", 5, 6423, 9925, 975, 4100460000, 1996, 8, 19, 4324)]
    [InlineData("count/20261003-115046/attempt-1-review-1.json", 3, 11114, 13120, 503, 4239890000, 680, 3, 19, 3284)]
    [InlineData("count/20261003-115126/attempt-1-resolver.json", 7, 6419, 10396, 1105, 4708110000, 1996, 8, 19, 4320)]
    [InlineData("count/20261003-115126/attempt-1-review-1.json", 2, 11134, 13129, 474, 3979320000, 680, 3, 19, 3286)]
    public void A_copilot_run_session_has_the_facts_of_its_row(string key, int rows, long firstContext, long lastContext,
        long outputSum, long nanoAiu, long toolTokens, int toolCount, int segments, long segmentSum)
    {
        var session = SessionOf(PollTwice(FixtureRuns.CopilotRepo), key);

        Assert.Equal(Provider.Copilot, session.Provider);
        var modelCalls = session.Content.Calls;
        Assert.Equal(rows, session.Stores.Calls.Length);
        Assert.Equal(rows, modelCalls.Length);
        Assert.Equal(firstContext, modelCalls[0].Usage?.Context);
        Assert.Equal(lastContext, modelCalls[^1].Usage?.Context);
        Assert.Equal(outputSum, modelCalls.Sum(call => call.Usage?.Output ?? 0));
        Assert.Equal(nanoAiu, modelCalls.Sum(call => call.NanoAiu ?? 0));
        Assert.Equal(2, session.Stores.SystemPrompt.Length);
        Assert.Equal(TestedVersions.CopilotCli, session.Stores.CliVersion);
        Assert.NotNull(session.Content.SentPrompt);

        var checkpoint = session.Content.Checkpoint;
        Assert.NotNull(checkpoint);
        Assert.Equal(toolTokens, checkpoint.ToolTokens);
        Assert.Equal(toolCount, checkpoint.ToolNames.Length);
        Assert.Equal(segments, checkpoint.SystemSegments.Length);
        Assert.Equal(segmentSum, checkpoint.SystemSegments.Sum(part => part.Tokens));
        Assert.Equal(lastContext, checkpoint.PromptTokens);
        Assert.Equal(nanoAiu, checkpoint.NanoAiu);
    }

    [Fact]
    public void The_first_call_of_the_core_worker_has_the_usage_of_its_database_row()
    {
        var session = SessionOf(PollTwice(FixtureRuns.CopilotRepo), CoreWorker);

        // Copilot's input_tokens 12069 hold the cached part, so the input left is 3.
        Assert.Equal(new TokenUsage(3, 0, 12066, 191), session.Content.Calls[0].Usage);
    }

    /// <summary>
    /// The snapshot of a first Poll() of a store on the fixture store folders, after the checks that hold for every
    /// session of both runs, and after a second Poll() that must publish nothing new.
    /// </summary>
    private static RunSnapshot PollTwice(string repo)
    {
        using var store = FixtureRuns.CreateStore(repo);
        store.Poll();
        var first = store.Current;

        Assert.Equal(1, first.Version);
        Assert.Empty(first.Problems);
        Assert.All(first.Sessions, x => Assert.Empty(x.Unavailable));
        Assert.All(first.Sessions, x => Assert.Equal(0, x.Stores.UnparsedLines));

        store.Poll();

        Assert.Equal(1, store.Current.Version);
        Assert.Same(first, store.Current);
        return first;
    }

    private static Session SessionOf(RunSnapshot s, string key) => Assert.Single(s.Sessions, x => x.Files.Key == key);
}

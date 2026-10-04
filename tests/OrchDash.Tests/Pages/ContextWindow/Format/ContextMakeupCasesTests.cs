using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Pages.ContextWindow.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.ContextWindow.Format;

// The make-up rules on small constructed sessions. The sessions are not in the snapshot they are built with.
public sealed class ContextMakeupCasesTests
{
    private static readonly RunSnapshot Empty = RunSnapshot.Empty(SampleRun.RepoPath);
    private static readonly Session Beta = SampleRun.Create().Sessions.Single(s => s.Files.Key == SampleRun.BetaWorkerKey);

    [Fact]
    public void The_rounding_remainder_goes_to_the_part_with_the_most_characters()
    {
        var makeup = Build(Session([Call("c1", 10)], blocks: ["a", "bb"]));

        Assert.Equal<long?>([3, 7, 0], makeup.Parts.Select(p => p.Tokens));
        Assert.All(makeup.Parts, p => Assert.True(p.IsEstimate));
    }

    [Fact]
    public void On_a_tie_the_remainder_goes_to_the_first_part()
    {
        var makeup = Build(Session([Call("c1", 7)], blocks: ["aa", "bb", "c"]));

        Assert.Equal<long?>([4, 2, 1, 0], makeup.Parts.Select(p => p.Tokens));
    }

    [Fact]
    public void Parts_without_characters_get_zero_and_the_rest_is_other()
    {
        var makeup = Build(Session([Call("c1", 50)], blocks: [""]));

        Assert.Equal<long?>([0, 0], makeup.Parts.Select(p => p.Tokens));
        Assert.All(makeup.Parts, p => Assert.True(p.IsEstimate));
        Assert.Equal(
        [
            new CategoryTotal(PartCategory.SystemPrompt, 0, 0, 0, 1, true),
            new CategoryTotal(PartCategory.ToolDefinitions, 0, 0, 0, 0, false),
            new CategoryTotal(PartCategory.Prompt, 0, 0, 0, 1, true),
            new CategoryTotal(PartCategory.Other, 50, 1, 0, 0, false),
        ], makeup.TotalsAt(0));
    }

    [Fact]
    public void Exact_tokens_larger_than_the_step_leave_nothing_to_share()
    {
        var checkpoint = new ContextCheckpoint(100, 40, ["powershell"], [new TokenPart("identity", 300)], null, null);
        var session = Session([Call("c1", 100)], prompt: "abcd");
        session = session with { Content = session.Content with { Checkpoint = checkpoint } };

        var makeup = Build(session);

        Assert.Equal(
        [
            ("identity", (long?)300, false),
            ("1 tools", 40, false),
            ("prompt #1", 0, true),
        ], makeup.Parts.Select(p => (p.Label, p.Tokens, p.IsEstimate)));
        var totals = makeup.TotalsAt(0);
        Assert.Equal([PartCategory.SystemPrompt, PartCategory.ToolDefinitions, PartCategory.Prompt],
            totals.Select(t => t.Category));
        Assert.Equal(3.0, totals[0].Share);
    }

    [Fact]
    public void A_negative_step_gives_the_new_parts_zero()
    {
        var makeup = Build(Session([Call("c1", 100), Call("c2", 80)], prompt: "abc",
            items: [new AssistantText("c1", null, "hello")]));

        Assert.Equal<long?>([100, 0], makeup.Parts.Select(p => p.Tokens));
        Assert.Equal(-20, makeup.StepAt(1));
        Assert.DoesNotContain(makeup.TotalsAt(1), t => t.Category == PartCategory.Other);
    }

    [Fact]
    public void A_session_without_calls_has_no_parts()
    {
        var makeup = Build(Session([], blocks: ["abc"], prompt: "abc",
            items: [new UserText(null, null, "hello", false)]));

        Assert.Empty(makeup.Calls);
        Assert.Empty(makeup.Parts);
        Assert.Null(makeup.Peak);
        Assert.Null(makeup.StepAt(0));
        Assert.Equal(
        [
            new CategoryTotal(PartCategory.SystemPrompt, null, null, 0, 0, false),
            new CategoryTotal(PartCategory.ToolDefinitions, null, null, 0, 0, false),
        ], makeup.TotalsAt(0));
    }

    [Fact]
    public void Items_without_a_call_id_follow_the_nearest_earlier_item_with_one()
    {
        var makeup = Build(Session([Call("c1", 10), Call("c2", 20), Call("c3", 30)],
            items:
            [
                new UserText(null, null, "start", false),
                new AssistantText("c1", null, "one"),
                new Notice(null, null, "compact", "notice"),
                new ToolCall("unknown", null, "t1", "Bash", "{}", "Bash ls", null),
                new AssistantText("c2", null, "two"),
                new UserText(null, null, "more", false),
            ]));

        Assert.Equal(
        [
            ("prompt #1", 0), ("user text", 0),
            ("text", 1), ("notice", 1), ("Bash ls", 1),
            ("text", 2), ("user text", 2),
        ], makeup.Parts.Select(p => (p.Label, p.Birth)));
    }

    [Fact]
    public void Items_without_a_call_id_in_a_later_session_are_born_at_its_first_call()
    {
        var run = SampleRun.CreateEnriched();
        var second = run.Sessions.Single(s => s.Files.Key == SampleRun.GammaWorker2Key);
        second = second with
        {
            Content = second.Content with
            {
                Items = [new UserText(null, null, "feedback", false), .. second.Content.Items],
            },
        };

        var makeup = ContextMakeup.Build(run, second);

        Assert.Equal(2, makeup.Parts.Single(p => p.Label == "user text").Birth);
    }

    [Fact]
    public void Injected_items_are_born_at_the_first_call_after_their_time()
    {
        var session = Session([Call("c1", 10, SampleRun.At(12, 0, 0)), Call("c2", 20, SampleRun.At(12, 30, 0))]);
        session = session with
        {
            Stores = StoreData.Empty with
            {
                Injected =
                [
                    new InjectedItem("middle", "system", SampleRun.At(12, 10, 0), "a"),
                    new InjectedItem("no_time", "system", null, "b"),
                    new InjectedItem("early", "system", SampleRun.At(11, 0, 0), "c"),
                    new InjectedItem("same_time", "system", SampleRun.At(12, 30, 0), "d"),
                    new InjectedItem("late", "system", SampleRun.At(13, 0, 0), "e"),
                ],
            },
        };

        var makeup = Build(session);

        Assert.Equal([("no_time", 0), ("early", 0), ("prompt #1", 0), ("middle", 1)],
            makeup.Parts.Select(p => (p.Label, p.Birth)));
    }

    [Fact]
    public void Default_arrays_give_an_empty_make_up()
    {
        var session = Beta with
        {
            Content = Beta.Content with { Calls = default, Items = default },
            Stores = new StoreData(null, default, default, default, default, null, null, null, 0),
        };

        var makeup = ContextMakeup.Build(Empty with { Sessions = default }, session);

        Assert.Equal([session], makeup.Chain);
        Assert.Empty(makeup.Calls);
        Assert.Empty(makeup.Parts);
        Assert.Equal(2, makeup.TotalsAt(0).Length);
    }

    [Fact]
    public void A_checkpoint_with_default_arrays_still_gives_its_tool_tokens()
    {
        var session = Session([Call("c1", 10)]);
        session = session with
        {
            Content = session.Content with { Checkpoint = new ContextCheckpoint(null, 5, default, default, null, null) },
            Stores = new StoreData(null, default, default, default, default, null, null, null, 0),
        };

        var makeup = Build(session);

        Assert.Equal(
        [
            ("0 tools", (long?)5, false),
            ("prompt #1", 0, true),
        ], makeup.Parts.Select(p => (p.Label, p.Tokens, p.IsEstimate)));
    }

    private static ContextMakeup Build(Session session) => ContextMakeup.Build(Empty, session);

    private static ModelCall Call(string id, long context, DateTimeOffset? startedAt = null) =>
        new(id, "m", startedAt, new TokenUsage(context, 0, 0, null));

    // The beta worker with only the given calls, items, system prompt blocks and prompt.
    private static Session Session(ImmutableArray<ModelCall> calls, ImmutableArray<string> blocks = default,
        string prompt = "", ImmutableArray<ConversationItem> items = default) => Beta with
    {
        Prompt = prompt,
        Content = Beta.Content with { Calls = calls, Items = items.IsDefault ? [] : items },
        Stores = StoreData.Empty with { SystemPrompt = blocks.IsDefault ? [] : blocks },
    };
}

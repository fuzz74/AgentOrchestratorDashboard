using OrchDash.Core.Model;
using OrchDash.Pages.ContextWindow.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.ContextWindow.Format;

// The make-up rules on the sessions of SampleRun.CreateEnriched(), and on the sub-agents of
// SampleRun.CreateSubAgents() (42.2, 42.3).
public sealed class ContextMakeupTests
{
    private readonly RunSnapshot _run = SampleRun.CreateEnriched();
    private readonly RunSnapshot _subRun = SampleRun.CreateSubAgents();

    private Session Get(string key) => _run.Sessions.Single(s => s.Files.Key == key);

    private Session GetSub(string key) => _subRun.Sessions.Single(s => s.Files.Key == key);

    [Fact]
    public void Alpha_worker_has_blocks_tools_injected_items_prompt_and_conversation()
    {
        var worker = Get(SampleRun.AlphaWorkerKey);
        var read = worker.Stores.Tools[0];
        var bash = worker.Stores.Tools[1];

        var makeup = ContextMakeup.Build(_run, worker);

        Assert.Equal([worker], makeup.Chain);
        Assert.Null(makeup.LastCheckpoint);
        Assert.Equal(
        [
            (PartCategory.SystemPrompt, PartKind.SystemBlock, "block 1", 57L, 0),
            (PartCategory.SystemPrompt, PartKind.SystemBlock, "block 2", 156L, 0),
            (PartCategory.ToolDefinitions, PartKind.ToolDefinition, "Read",
                (long)(read.Description!.Length + read.SchemaJson!.Length), 0),
            (PartCategory.ToolDefinitions, PartKind.ToolDefinition, "Bash",
                (long)(bash.Description!.Length + bash.SchemaJson!.Length), 0),
            (PartCategory.Injected, PartKind.Injected, "skill_listing", 94L, 0),
            (PartCategory.Injected, PartKind.Injected, "nested_memory", 129L, 0),
            (PartCategory.Prompt, PartKind.Prompt, "prompt #1", 223L, 0),
            (PartCategory.Injected, PartKind.Injected, "total_tokens_reminder", 80L, 1),
            (PartCategory.Conversation, PartKind.Item, "thinking", 149L, 1),
            (PartCategory.Conversation, PartKind.Item, "text", 54L, 1),
            (PartCategory.Conversation, PartKind.ToolCall, "Edit src/Alpha/Parser.cs", 202L, 1),
        ], makeup.Parts.Select(p => (p.Category, p.Kind, p.Label, p.Characters, p.Birth)));

        Assert.Same(worker.Stores.SystemPrompt[0], makeup.Parts[0].Source);
        Assert.Same(read, makeup.Parts[2].Source);
        Assert.Same(worker.Stores.Injected[2], makeup.Parts[7].Source);
        Assert.Same(worker, makeup.Parts[6].Source);
        Assert.Same(worker.Content.Items[2], makeup.Parts[10].Source);
        Assert.All(makeup.Parts, p => Assert.Same(worker, p.Session));
    }

    [Fact]
    public void Alpha_worker_tokens_of_each_group_add_up_to_its_step_as_estimates()
    {
        var makeup = ContextMakeup.Build(_run, Get(SampleRun.AlphaWorkerKey));

        Assert.Equal(19_200, makeup.Parts.Where(p => p.Birth == 0).Sum(p => p.Tokens));
        Assert.Equal(24_100, makeup.Parts.Where(p => p.Birth == 1).Sum(p => p.Tokens));
        Assert.All(makeup.Parts, p => Assert.True(p.IsEstimate));
        Assert.Equal(43_300, makeup.Peak);
        Assert.Equal(19_200, makeup.StepAt(0));
        Assert.Equal(24_100, makeup.StepAt(1));
    }

    [Fact]
    public void Alpha_worker_totals_give_each_category_with_its_share()
    {
        var makeup = ContextMakeup.Build(_run, Get(SampleRun.AlphaWorkerKey));

        var first = makeup.TotalsAt(0);
        Assert.Equal(
            [PartCategory.SystemPrompt, PartCategory.ToolDefinitions, PartCategory.Injected, PartCategory.Prompt],
            first.Select(t => t.Category));
        Assert.Equal(19_200, first.Sum(t => t.Tokens));
        Assert.Equal(1.0, first.Sum(t => t.Share!.Value), 9);
        Assert.Equal([2, 2, 2, 1], first.Select(t => t.Parts));
        Assert.Equal(57 + 156, first[0].Characters);
        Assert.All(first, t => Assert.True(t.HasEstimates));

        var second = makeup.TotalsAt(1);
        Assert.Equal(
        [
            PartCategory.SystemPrompt, PartCategory.ToolDefinitions, PartCategory.Injected, PartCategory.Prompt,
            PartCategory.Conversation,
        ], second.Select(t => t.Category));
        Assert.Equal(43_300, second.Sum(t => t.Tokens));
        Assert.Equal(3, second[2].Parts);
        Assert.Equal(149 + 54 + 202, second[4].Characters);
        Assert.Equal(11, makeup.PartsAt(1).Length);
        Assert.Equal(7, makeup.PartsAt(0).Length);
    }

    [Fact]
    public void Alpha_review_takes_segments_and_tools_from_the_checkpoint_with_exact_tokens()
    {
        var review = Get(SampleRun.AlphaReviewKey);

        var makeup = ContextMakeup.Build(_run, review);

        Assert.Same(review.Content.Checkpoint, makeup.LastCheckpoint);
        Assert.Equal(
        [
            (PartKind.SystemSegment, "identity", 0L, (long?)310, false, 0),
            (PartKind.SystemSegment, "tone_and_style", 0L, 227, false, 0),
            (PartKind.SystemSegment, "tool_instructions", 0L, 1_540, false, 0),
            (PartKind.ToolSummary, "2 tools", 0L, 680, false, 0),
            (PartKind.Prompt, "prompt #1.1", 166L, 14_200 - 2_077 - 680, true, 0),
        ], makeup.Parts.Take(5).Select(p => (p.Kind, p.Label, p.Characters, p.Tokens, p.IsEstimate, p.Birth)));
        Assert.Equal(review.Content.SentPrompt!.Length, makeup.Parts[4].Characters);
        Assert.Same(review.Content.Checkpoint, makeup.Parts[3].Source);
        Assert.Equal(review.Content.Checkpoint!.SystemSegments[0], makeup.Parts[0].Source);

        // The thinking and the view call of turn 0; turn 1's answer is in no context.
        Assert.Equal(["thinking", "view src/Alpha/Parser.cs"], makeup.Parts.Skip(5).Select(p => p.Label));
        Assert.Equal(2_750, makeup.Parts.Skip(5).Sum(p => p.Tokens));
        Assert.DoesNotContain(makeup.TotalsAt(1), t => t.Category == PartCategory.Other);
    }

    [Fact]
    public void Alpha_review_shows_other_when_the_known_tokens_leave_a_rest()
    {
        var review = Get(SampleRun.AlphaReviewKey);
        review = review with { Content = review.Content with { Items = [] } };

        var totals = ContextMakeup.Build(_run, review).TotalsAt(1);

        var other = Assert.Single(totals, t => t.Category == PartCategory.Other);
        Assert.Equal(new CategoryTotal(PartCategory.Other, 2_750, 2_750.0 / 16_950, 0, 0, false), other);
        var system = totals[0];
        Assert.Equal(new CategoryTotal(PartCategory.SystemPrompt, 2_077, 2_077.0 / 16_950, 0, 3, false), system);
        Assert.Equal(16_950, totals.Sum(t => t.Tokens));
    }

    [Fact]
    public void Beta_worker_without_store_data_has_only_prompt_and_conversation()
    {
        var beta = Get(SampleRun.BetaWorkerKey);

        var makeup = ContextMakeup.Build(_run, beta);

        Assert.Equal(
        [
            (PartCategory.Prompt, "prompt #1", 0),
            (PartCategory.Conversation, "thinking", 1),
            (PartCategory.Conversation, "text", 1),
            (PartCategory.Conversation, "Read src/Alpha/Parser.cs", 1),
            (PartCategory.Conversation, "Grep Parse\\( in src", 1),
        ], makeup.Parts.Select(p => (p.Category, p.Label, p.Birth)));
        Assert.Equal(18_800, makeup.Parts[0].Tokens);

        Assert.Equal(
        [
            new CategoryTotal(PartCategory.SystemPrompt, 0, 0, 0, 0, false),
            new CategoryTotal(PartCategory.ToolDefinitions, 0, 0, 0, 0, false),
            new CategoryTotal(PartCategory.Prompt, 18_800, 1, beta.Prompt.Length, 1, true),
        ], makeup.TotalsAt(0));
    }

    [Theory]
    [InlineData(SampleRun.GammaWorker1Key)]
    [InlineData(SampleRun.GammaWorker2Key)]
    public void Gamma_attempts_form_one_chain_from_either_session(string key)
    {
        var first = Get(SampleRun.GammaWorker1Key);
        var second = Get(SampleRun.GammaWorker2Key);

        var makeup = ContextMakeup.Build(_run, Get(key));

        Assert.Equal([first, second], makeup.Chain);
        Assert.Equal(
        [
            (0, first, "msg_01E1gamma"),
            (1, first, "msg_02F2gamma"),
            (2, second, "msg_03G3gamma"),
            (3, second, "msg_04H4gamma"),
        ], makeup.Calls.Select(c => (c.Index, c.Session, c.Call.Id)));
        Assert.Equal(
        [
            ("block 1", 0), ("Bash", 0), ("prompt #1", 0),
            ("text", 1), ("Write src/Gamma/Formatter.cs", 1),
            ("prompt #2", 2), ("Bash dotnet test tests/Gamma", 2), ("text", 2),
            ("thinking", 3), ("Edit src/Gamma/Formatter.cs", 3),
        ], makeup.Parts.Select(p => (p.Label, p.Birth)));
        Assert.Same(second, makeup.Parts.Single(p => p.Label == "prompt #2").Session);
        Assert.Equal(30_703 - 26_502, makeup.StepAt(2));
        Assert.Equal(30_703 - 26_502, makeup.Parts.Where(p => p.Birth == 2).Sum(p => p.Tokens));
        Assert.Equal(33_501, makeup.Peak);
    }

    [Fact]
    public void A_middle_call_without_usage_joins_its_parts_to_the_next_call_with_usage()
    {
        var run = WithoutUsage(_run, SampleRun.GammaWorker1Key, 1);

        var makeup = ContextMakeup.Build(run, run.Sessions.Single(s => s.Files.Key == SampleRun.GammaWorker2Key));

        Assert.Null(makeup.StepAt(1));
        Assert.Equal(30_703 - 23_004, makeup.StepAt(2));
        Assert.Equal(30_703 - 23_004, makeup.Parts.Where(p => p.Birth is 1 or 2).Sum(p => p.Tokens));
        Assert.All(makeup.Parts, p => Assert.True(p.Tokens is not null && p.IsEstimate));

        var totals = makeup.TotalsAt(1);
        Assert.Equal(
        [
            PartCategory.SystemPrompt, PartCategory.ToolDefinitions, PartCategory.Prompt, PartCategory.Conversation,
        ], totals.Select(t => t.Category));
        Assert.All(totals, t => Assert.True(t.Tokens is null && t.Share is null && !t.HasEstimates));
        Assert.Equal(2, totals[3].Parts);
        Assert.Equal(makeup.PartsAt(1).Where(p => p.Category == PartCategory.Conversation).Sum(p => p.Characters),
            totals[3].Characters);
        Assert.True(totals[3].Characters > 0);
    }

    [Fact]
    public void Parts_born_after_the_last_call_with_usage_have_unknown_tokens()
    {
        var run = WithoutUsage(_run, SampleRun.GammaWorker2Key, 1);

        var makeup = ContextMakeup.Build(run, run.Sessions.Single(s => s.Files.Key == SampleRun.GammaWorker1Key));

        Assert.All(makeup.Parts.Where(p => p.Birth == 3), p => Assert.True(p.Tokens is null && !p.IsEstimate));
        Assert.All(makeup.Parts.Where(p => p.Birth < 3), p => Assert.NotNull(p.Tokens));
        Assert.Equal(30_703, makeup.Peak);
        Assert.Null(makeup.StepAt(3));
        var totals = makeup.TotalsAt(3);
        Assert.DoesNotContain(totals, t => t.Category == PartCategory.Other);
        Assert.All(totals, t => Assert.True(t.Tokens is null && t.Share is null));
        Assert.Equal(makeup.Parts.Sum(p => p.Characters), totals.Sum(t => t.Characters));
    }

    [Fact]
    public void Alpha_worker_with_sub_agents_keeps_only_its_own_calls_and_items()
    {
        var worker = GetSub(SampleRun.AlphaWorkerKey);

        var makeup = ContextMakeup.Build(_subRun, worker);

        Assert.Equal([worker], makeup.Chain);
        Assert.Equal(["msg_01A7alpha", "msg_02B8alpha"], makeup.Calls.Select(c => c.Call.Id));
        // The sub-agents' calls and items stay out; the Agent call that starts the first one is the worker's own.
        Assert.Equal(
        [
            ("block 1", 0), ("block 2", 0), ("Read", 0), ("Bash", 0), ("skill_listing", 0), ("nested_memory", 0),
            ("prompt #1", 0),
            ("total_tokens_reminder", 1), ("thinking", 1), ("text", 1), ("Edit src/Alpha/Parser.cs", 1),
            ("Agent Survey the parser module", 1),
        ], makeup.Parts.Select(p => (p.Label, p.Birth)));
        Assert.Equal(43_300, makeup.Peak);
        Assert.Equal(24_100, makeup.StepAt(1));
        Assert.Equal(24_100, makeup.Parts.Where(p => p.Birth == 1).Sum(p => p.Tokens));
    }

    [Fact]
    public void Planner_items_find_their_turns_among_its_own_calls_although_its_sub_agents_repeat_the_turn_ids()
    {
        var planner = GetSub(SampleRun.PlannerKey);

        var makeup = ContextMakeup.Build(_subRun, planner);

        Assert.Equal([(0, "0", (string?)null), (1, "1", null)], makeup.Calls.Select(c => (c.Index, c.Call.Id, c.Call.AgentId)));
        Assert.Equal(
        [
            ("block 1", 0), ("prompt #1", 0), ("text", 1), ("task Map the repo", 1), ("task Survey the tests", 1),
        ], makeup.Parts.Select(p => (p.Label, p.Birth)));
        Assert.Equal(15_775, makeup.Peak);
        Assert.Equal(15_775 - 12_430, makeup.StepAt(1));
    }

    [Fact]
    public void Alpha_sub_agent_with_store_data_has_its_blocks_tools_prompt_and_items_and_no_chain()
    {
        var worker = GetSub(SampleRun.AlphaWorkerKey);
        var sub = worker.Content.SubAgents[0];
        var stores = worker.Stores.SubAgents[SampleRun.AlphaSub1Id];

        var makeup = ContextMakeup.Build(_subRun, worker, SampleRun.AlphaSub1Id);

        Assert.Equal([worker], makeup.Chain);
        Assert.Null(makeup.LastCheckpoint);
        Assert.Equal([(0, "msg_01S1alpha"), (1, "msg_02S1alpha")], makeup.Calls.Select(c => (c.Index, c.Call.Id)));
        // The final text of its second call is in no context.
        Assert.Equal(
        [
            (PartCategory.SystemPrompt, PartKind.SystemBlock, "block 1", 0),
            (PartCategory.SystemPrompt, PartKind.SystemBlock, "block 2", 0),
            (PartCategory.ToolDefinitions, PartKind.ToolDefinition, "Glob", 0),
            (PartCategory.ToolDefinitions, PartKind.ToolDefinition, "Read", 0),
            (PartCategory.Prompt, PartKind.Prompt, "prompt", 0),
            (PartCategory.Conversation, PartKind.ToolCall, "Glob src/Alpha/**", 1),
            (PartCategory.Conversation, PartKind.ToolCall, "Read src/Alpha/Parser.cs", 1),
        ], makeup.Parts.Select(p => (p.Category, p.Kind, p.Label, p.Birth)));
        Assert.Same(stores.SystemPrompt[1], makeup.Parts[1].Source);
        Assert.Same(stores.Tools[0], makeup.Parts[2].Source);
        Assert.Same(sub, makeup.Parts[4].Source);
        Assert.Equal(sub.Prompt.Length, makeup.Parts[4].Characters);
        Assert.All(makeup.Parts, p => Assert.Same(worker, p.Session));
        Assert.Equal(5_352, makeup.Peak);
        Assert.Equal(4_003, makeup.Parts.Where(p => p.Birth == 0).Sum(p => p.Tokens));
        Assert.Equal(5_352 - 4_003, makeup.Parts.Where(p => p.Birth == 1).Sum(p => p.Tokens));
    }

    [Fact]
    public void Alpha_sub_agent_without_store_data_has_no_system_prompt_tools_or_injected_items()
    {
        var worker = GetSub(SampleRun.AlphaWorkerKey);
        var sub = worker.Content.SubAgents[1];

        var makeup = ContextMakeup.Build(_subRun, worker, SampleRun.AlphaSub2Id);

        Assert.Equal(["msg_01S2alpha"], makeup.Calls.Select(c => c.Call.Id));
        // Its Grep call is born after its only call.
        Assert.Equal([(PartCategory.Prompt, "prompt", 0)], makeup.Parts.Select(p => (p.Category, p.Label, p.Birth)));
        Assert.Equal(
        [
            new CategoryTotal(PartCategory.SystemPrompt, 0, 0, 0, 0, false),
            new CategoryTotal(PartCategory.ToolDefinitions, 0, 0, 0, 0, false),
            new CategoryTotal(PartCategory.Prompt, 9_803, 1, sub.Prompt.Length, 1, true),
        ], makeup.TotalsAt(0));
    }

    [Fact]
    public void Beta_sub_agent_prompt_is_born_at_its_own_first_call_not_at_the_session_s()
    {
        var beta = GetSub(SampleRun.BetaWorkerKey);

        var makeup = ContextMakeup.Build(_subRun, beta, SampleRun.BetaSub1Id);

        Assert.Equal([(0, "msg_01S1beta")], makeup.Calls.Select(c => (c.Index, c.Call.Id)));
        Assert.Equal([("prompt", 0, (long?)3_603)], makeup.Parts.Select(p => (p.Label, p.Birth, p.Tokens)));
    }

    [Fact]
    public void Planner_sub_agent_leaves_out_the_calls_and_items_of_the_sub_agent_it_starts()
    {
        var planner = GetSub(SampleRun.PlannerKey);

        var makeup = ContextMakeup.Build(_subRun, planner, SampleRun.PlannerSub1Id);

        Assert.Equal([SampleRun.PlannerSub1Id], makeup.Calls.Select(c => c.Call.AgentId));
        Assert.Equal(6_812, makeup.Peak);
        // Its task call, glob and text follow its only call; "Read the spec"'s view and text are not its own.
        Assert.Equal([("prompt", 0)], makeup.Parts.Select(p => (p.Label, p.Birth)));
    }

    [Fact]
    public void An_agent_id_the_session_does_not_have_gives_the_session_make_up()
    {
        var worker = GetSub(SampleRun.AlphaWorkerKey);

        var own = ContextMakeup.Build(_subRun, worker);
        var unknown = ContextMakeup.Build(_subRun, worker, "toolu_unknown");

        Assert.Equal(own.Chain, unknown.Chain);
        Assert.Equal(own.Calls, unknown.Calls);
        Assert.Equal(own.Parts, unknown.Parts);
    }

    private static RunSnapshot WithoutUsage(RunSnapshot run, string key, int call) => run with
    {
        Sessions =
        [
            .. run.Sessions.Select(s => s.Files.Key != key ? s : s with
            {
                Content = s.Content with
                {
                    Calls = s.Content.Calls.SetItem(call, s.Content.Calls[call] with { Usage = null }),
                },
            }),
        ],
    };
}

using OrchDash.Core.Copilot;
using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Copilot.CopilotEvents;

namespace OrchDash.Core.Tests.Copilot;

/// <summary>The user.message and session.usage_checkpoint lines (spec 10.2, 10.3 and the checkpoint table of 4.3).</summary>
public sealed class CopilotUsageLineTests
{
    /// <summary>The data of the spec 4.3 checkpoint example.</summary>
    private const string ExampleCheckpoint =
        """{"totalNanoAiu":12930990000,"totalPremiumRequests":1,"promptCacheBreakState":[{"conversation":"main","lastActiveModel":"gpt-6-sol","models":{"gpt-6-sol":{"prompt_tokens":19104,"tool_tokens":1996,"tools":[{"name":"powershell","schema_hash":"283c39c42528"}],"system_segments":[{"segment":"tone_and_style","hash":"866a6130c416","tokens":227}]}}}]}""";

    private static string UserMessage(string transformedContent, int second = 0) =>
        Event("user.message", $$"""{"content":"prompt","transformedContent":{{Quote(transformedContent)}},"turnId":"0"}""", second);

    private static string Checkpoint(string data, int second = 0) => Event("session.usage_checkpoint", data, second);

    [Fact]
    public void User_message_sets_the_sent_prompt()
    {
        var content = Parse(UserMessage("<current_datetime>2026-10-03T11:34:49+02:00</current_datetime>\n\nprompt", second: 1));

        Assert.Equal("<current_datetime>2026-10-03T11:34:49+02:00</current_datetime>\n\nprompt", content.SentPrompt);
        Assert.Equal(At(1), content.FirstEventAt);
    }

    [Fact]
    public void A_second_user_message_does_not_replace_the_sent_prompt()
    {
        var content = Parse(UserMessage("first"), UserMessage("second"));

        Assert.Equal("first", content.SentPrompt);
    }

    [Fact]
    public void An_empty_or_missing_transformed_content_leaves_the_sent_prompt_null()
    {
        var parser = new CopilotSessionParser(WorkDir);
        parser.AddLine(UserMessage(""));
        parser.AddLine(Event("user.message", """{"content":"prompt"}"""));
        parser.AddLine(Event("user.message", """{"transformedContent":42}"""));
        Assert.Null(parser.Build().SentPrompt);

        parser.AddLine(UserMessage("later"));
        Assert.Equal("later", parser.Build().SentPrompt);
    }

    [Fact]
    public void Spec_example_checkpoint_gives_every_member()
    {
        var checkpoint = Assert.IsType<ContextCheckpoint>(Parse(Checkpoint(ExampleCheckpoint)).Checkpoint);

        Assert.Equal(19104, checkpoint.PromptTokens);
        Assert.Equal(1996, checkpoint.ToolTokens);
        Assert.Equal(["powershell"], checkpoint.ToolNames);
        Assert.Equal([new TokenPart("tone_and_style", 227)], checkpoint.SystemSegments);
        Assert.Equal(12930990000, checkpoint.NanoAiu);
        Assert.Equal(1, checkpoint.PremiumRequests);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""{"promptCacheBreakState":[]}""")]
    [InlineData("""{"promptCacheBreakState":[{"conversation":"main"}]}""")]
    [InlineData("""{"promptCacheBreakState":[{"conversation":"main","models":{"gpt-6-sol":{"tools":"none","system_segments":{}}}}]}""")]
    public void Missing_parts_give_null_or_empty_arrays(string data)
    {
        var checkpoint = Assert.IsType<ContextCheckpoint>(Parse(Checkpoint(data)).Checkpoint);

        Assert.Null(checkpoint.PromptTokens);
        Assert.Null(checkpoint.ToolTokens);
        Assert.False(checkpoint.ToolNames.IsDefault);
        Assert.Empty(checkpoint.ToolNames);
        Assert.False(checkpoint.SystemSegments.IsDefault);
        Assert.Empty(checkpoint.SystemSegments);
        Assert.Null(checkpoint.NanoAiu);
        Assert.Null(checkpoint.PremiumRequests);
    }

    [Fact]
    public void Tools_without_a_name_and_segments_without_a_name_or_tokens_are_left_out()
    {
        const string data =
            """{"promptCacheBreakState":[{"models":{"m":{"tools":[{"name":"view"},{"schema_hash":"x"},"rg"],"system_segments":[{"segment":"a","tokens":5},{"segment":"b"},{"tokens":7},{"segment":"c","tokens":"9"}]}}}]}""";

        var checkpoint = Parse(Checkpoint(data)).Checkpoint;

        Assert.Equal(["view"], checkpoint?.ToolNames);
        Assert.Equal([new TokenPart("a", 5)], checkpoint?.SystemSegments);
    }

    [Fact]
    public void Without_a_main_conversation_the_first_entry_is_taken()
    {
        const string data =
            """{"promptCacheBreakState":[{"conversation":"sub-1","lastActiveModel":"a","models":{"a":{"prompt_tokens":100}}},{"conversation":"sub-2","lastActiveModel":"a","models":{"a":{"prompt_tokens":200}}}]}""";

        Assert.Equal(100, Parse(Checkpoint(data)).Checkpoint?.PromptTokens);
    }

    [Fact]
    public void The_main_conversation_is_taken_wherever_it_is()
    {
        const string data =
            """{"promptCacheBreakState":[{"conversation":"sub-1","lastActiveModel":"a","models":{"a":{"prompt_tokens":100}}},{"conversation":"main","lastActiveModel":"a","models":{"a":{"prompt_tokens":300}}}]}""";

        Assert.Equal(300, Parse(Checkpoint(data)).Checkpoint?.PromptTokens);
    }

    [Fact]
    public void The_last_active_model_is_taken()
    {
        const string data =
            """{"promptCacheBreakState":[{"conversation":"main","lastActiveModel":"b","models":{"a":{"prompt_tokens":100},"b":{"prompt_tokens":200}}}]}""";

        Assert.Equal(200, Parse(Checkpoint(data)).Checkpoint?.PromptTokens);
    }

    [Theory]
    [InlineData("\"c\"")]
    [InlineData("null")]
    public void A_last_active_model_that_is_not_among_the_models_takes_the_first_model(string lastActiveModel)
    {
        var data = """{"promptCacheBreakState":[{"conversation":"main","lastActiveModel":""" + lastActiveModel +
            ""","models":{"a":{"prompt_tokens":100},"b":{"prompt_tokens":200}}}]}""";

        Assert.Equal(100, Parse(Checkpoint(data)).Checkpoint?.PromptTokens);
    }

    [Fact]
    public void A_total_nano_aiu_above_the_int_range_is_read()
    {
        var checkpoint = Parse(Checkpoint("""{"totalNanoAiu":9000000000000,"totalPremiumRequests":2.5}""")).Checkpoint;

        Assert.Equal(9_000_000_000_000L, checkpoint?.NanoAiu);
        Assert.Equal(2.5, checkpoint?.PremiumRequests);
    }

    [Fact]
    public void A_later_checkpoint_replaces_the_first()
    {
        var content = Parse(
            Checkpoint(ExampleCheckpoint, second: 1),
            Checkpoint("""{"totalNanoAiu":5}""", second: 2));

        var checkpoint = Assert.IsType<ContextCheckpoint>(content.Checkpoint);
        Assert.Equal(5, checkpoint.NanoAiu);
        Assert.Null(checkpoint.PromptTokens);
        Assert.Empty(checkpoint.ToolNames);
        Assert.Null(checkpoint.PremiumRequests);
    }

    [Fact]
    public void Ephemeral_user_message_and_checkpoint_are_skipped()
    {
        var content = Parse(
            Event("user.message", """{"transformedContent":"prompt"}""", ephemeral: true),
            Event("session.usage_checkpoint", ExampleCheckpoint, ephemeral: true));

        Assert.Null(content.SentPrompt);
        Assert.Null(content.Checkpoint);
    }

    [Fact]
    public void The_lines_invalidate_the_content()
    {
        var parser = new CopilotSessionParser(WorkDir);
        parser.AddLine(TurnStart("0", second: 1));
        var first = parser.Build();

        parser.AddLine(UserMessage("prompt", second: 2));
        var second = parser.Build();
        parser.AddLine(Checkpoint(ExampleCheckpoint, second: 3));
        var third = parser.Build();

        Assert.Null(first.SentPrompt);
        Assert.Equal("prompt", second.SentPrompt);
        Assert.Null(second.Checkpoint);
        Assert.NotNull(third.Checkpoint);
        Assert.Equal("prompt", third.SentPrompt);
        Assert.Equal(At(3), third.LastEventAt);
    }

    [Fact]
    public void A_log_without_these_lines_has_no_sent_prompt_or_checkpoint()
    {
        var content = Parse(TurnStart("0", second: 1), Result(0, second: 2));

        Assert.Null(content.SentPrompt);
        Assert.Null(content.Checkpoint);
        Assert.Null(content.RateLimit);
    }
}

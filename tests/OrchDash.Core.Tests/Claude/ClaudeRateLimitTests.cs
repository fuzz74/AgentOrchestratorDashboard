using OrchDash.Core.Claude;
using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Claude.ClaudeLines;

namespace OrchDash.Core.Tests.Claude;

/// <summary>The rate_limit_event line (spec 10.1 and the rate limit table of 4.3).</summary>
public sealed class ClaudeRateLimitTests
{
    private static readonly DateTimeOffset FiveHourReset = DateTimeOffset.FromUnixTimeSeconds(1790848200);
    private static readonly DateTimeOffset SevenDayReset = DateTimeOffset.FromUnixTimeSeconds(1790956800);

    [Fact]
    public void Spec_example_gives_every_member()
    {
        const string line =
            """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.2,"resetsAt":1790848200},"seven_day":{"utilization":0.27,"resetsAt":1790956800}}},"session_id":"..."}""";

        var content = Parse(Assistant("msg_1", Time1, Text("a")), line);

        Assert.Equal(new RateLimit("allowed", "five_hour", 0.2, FiveHourReset, 0.27, SevenDayReset, At1), content.RateLimit);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 50, 0, TimeSpan.Zero), content.RateLimit?.FiveHourResetsAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 16, 0, 0, TimeSpan.Zero), content.RateLimit?.SevenDayResetsAt);
    }

    [Fact]
    public void Missing_or_wrongly_typed_fields_give_null()
    {
        var content = Parse(RateLimitEvent(
            """{"status":1,"rateLimitType":"seven_day","unifiedWindows":{"five_hour":{"utilization":"high","resetsAt":1.5},"seven_day":{"utilization":0.5}}}"""));

        Assert.Equal(new RateLimit(null, "seven_day", null, null, 0.5, null, null), content.RateLimit);
    }

    [Theory]
    [InlineData("""{"type":"rate_limit_event"}""")]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":"allowed"}""")]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"unifiedWindows":[]}}""")]
    public void A_line_without_info_gives_a_rate_limit_of_nulls(string line)
    {
        var content = Parse(line);

        Assert.Equal(new RateLimit(null, null, null, null, null, null, null), content.RateLimit);
    }

    [Fact]
    public void Reset_time_out_of_range_gives_null()
    {
        var content = Parse(RateLimitEvent(
            """{"unifiedWindows":{"five_hour":{"resetsAt":999999999999999},"seven_day":{"resetsAt":-999999999999999}}}"""));

        Assert.Null(content.RateLimit?.FiveHourResetsAt);
        Assert.Null(content.RateLimit?.SevenDayResetsAt);
    }

    [Fact]
    public void A_later_line_replaces_the_whole_rate_limit()
    {
        var content = Parse(
            RateLimitEvent(RateLimitInfo(0.2, 0.27)),
            RateLimitEvent("""{"status":"allowed_warning","unifiedWindows":{"five_hour":{"utilization":0.9}}}"""));

        Assert.Equal(new RateLimit("allowed_warning", null, 0.9, null, null, null, null), content.RateLimit);
    }

    [Fact]
    public void Seen_at_is_the_latest_event_time_when_the_line_arrives()
    {
        var parser = new ClaudeSessionParser(null);
        parser.AddLine(Init());
        parser.AddLine(RateLimitEvent(RateLimitInfo()));
        Assert.Null(parser.Build().RateLimit?.SeenAt);
        Assert.NotNull(parser.Build().RateLimit);

        parser.AddLine(Assistant("msg_1", Time2, Text("a")));
        parser.AddLine(Assistant("msg_2", Time1, Text("b")));
        Assert.Null(parser.Build().RateLimit?.SeenAt);

        parser.AddLine(RateLimitEvent(RateLimitInfo()));
        Assert.Equal(At2, parser.Build().RateLimit?.SeenAt);

        parser.AddLine(Assistant("msg_3", Time3, Text("c")));
        Assert.Equal(At2, parser.Build().RateLimit?.SeenAt);
    }

    [Fact]
    public void The_line_changes_nothing_else_and_invalidates_the_content()
    {
        var parser = new ClaudeSessionParser(null);
        parser.AddLine(Init());
        parser.AddLine(Assistant("msg_1", Time1, Text("a")));
        var before = parser.Build();

        parser.AddLine(RateLimitEvent(RateLimitInfo(), sessionId: "other-session"));
        var after = parser.Build();

        Assert.NotSame(before, after);
        Assert.Null(before.RateLimit);
        Assert.NotNull(after.RateLimit);
        Assert.Equal(SessionId, after.SessionId);
        Assert.Equal(before.Model, after.Model);
        Assert.Same(before.Init, after.Init);
        Assert.Equal(before.Calls, after.Calls);
        Assert.Equal(before.Items, after.Items);
        Assert.Equal(before.Result, after.Result);
        Assert.Equal(At1, after.FirstEventAt);
        Assert.Equal(At1, after.LastEventAt);
        Assert.Equal(0, after.UnparsedLines);
    }

    [Fact]
    public void A_log_without_rate_limit_lines_has_no_rate_limit()
    {
        var content = Parse(Init(), Assistant("msg_1", Time1, Text("a")));

        Assert.Null(content.RateLimit);
    }
}

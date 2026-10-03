using System.Globalization;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class LookTests
{
    [Theory]
    [InlineData(TaskState.Done, "✔", "success")]
    [InlineData(TaskState.Running, "▶", "primary")]
    [InlineData(TaskState.Pending, "·", "muted")]
    [InlineData(TaskState.Failed, "✖", "error")]
    [InlineData(TaskState.Blocked, "⊘", "warning")]
    public void Task_state_icon_and_color(TaskState state, string icon, string color)
    {
        Assert.Equal(icon, Look.Icon(state));
        Assert.Equal(color, Look.Color(state));
    }

    [Theory]
    [InlineData(SessionState.Succeeded, "✔", "success")]
    [InlineData(SessionState.Running, "▶", "primary")]
    [InlineData(SessionState.Failed, "✖", "error")]
    [InlineData(SessionState.Aborted, "◌", "muted")]
    public void Session_state_icon_and_color(SessionState state, string icon, string color)
    {
        Assert.Equal(icon, Look.Icon(state));
        Assert.Equal(color, Look.Color(state));
    }

    [Theory]
    [InlineData(AgentRole.Worker, "cyan")]
    [InlineData(AgentRole.Reviewer, "yellow")]
    [InlineData(AgentRole.Resolver, "magenta")]
    [InlineData(AgentRole.Planner, "blue")]
    [InlineData(AgentRole.Bootstrap, "green")]
    public void Role_color(AgentRole role, string color) => Assert.Equal(color, Look.Color(role));

    [Theory]
    [InlineData(ProgressKind.Info, "")]
    [InlineData(ProgressKind.Activity, "muted")]
    [InlineData(ProgressKind.Success, "success")]
    [InlineData(ProgressKind.Warning, "warning")]
    [InlineData(ProgressKind.Failure, "error")]
    public void Progress_kind_color(ProgressKind kind, string color) => Assert.Equal(color, Look.Color(kind));

    [Fact]
    public void Tag_wraps_escaped_text_in_the_color()
    {
        Assert.Equal("[error]see [[log]] now[/]", Look.Tag("error", "see [log] now"));
    }

    [Fact]
    public void Tag_without_color_is_the_escaped_text()
    {
        Assert.Equal("[[bold]] text", Look.Tag("", "[bold] text"));
    }

    [Theory]
    [InlineData(0, 0, 12, 0, "12s")]
    [InlineData(0, 0, 0, 0, "0s")]
    [InlineData(0, 0, 59, 999, "59s")]
    [InlineData(0, 1, 0, 0, "1m00s")]
    [InlineData(0, 3, 7, 0, "3m07s")]
    [InlineData(0, 59, 59, 900, "59m59s")]
    [InlineData(1, 0, 0, 0, "1h00m")]
    [InlineData(1, 5, 59, 0, "1h05m")]
    [InlineData(27, 3, 0, 0, "27h03m")]
    public void Span_formats(int hours, int minutes, int seconds, int milliseconds, string expected)
    {
        Assert.Equal(expected, Look.Span(new TimeSpan(0, hours, minutes, seconds, milliseconds)));
    }

    [Fact]
    public void Span_of_a_negative_time_is_zero()
    {
        Assert.Equal("0s", Look.Span(TimeSpan.FromMinutes(-3)));
    }

    [Fact]
    public void Clock_shows_local_time()
    {
        var local = new DateTimeOffset(new DateTime(2026, 10, 3, 7, 5, 9, DateTimeKind.Local));

        Assert.Equal("07:05:09", Look.Clock(local));
        Assert.Equal("07:05:09", Look.Clock(local.ToUniversalTime()));
    }

    [Theory]
    [InlineData(0.25, "0.25 USD")]
    [InlineData(0, "0.00 USD")]
    [InlineData(1.1573, "1.16 USD")]
    [InlineData(12.5, "12.50 USD")]
    public void Usd_formats(double value, string expected) => Assert.Equal(expected, Look.Usd(value));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(950, "950")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1.0k")]
    [InlineData(47_400, "47.4k")]
    [InlineData(47_420, "47.4k")]
    [InlineData(1_200_000, "1.2M")]
    [InlineData(15_000_000, "15.0M")]
    public void Tokens_formats(long value, string expected) => Assert.Equal(expected, Look.Tokens(value));

    [Fact]
    public void Formats_do_not_follow_the_current_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nb-NO");

            Assert.Equal("0.25 USD", Look.Usd(0.25));
            Assert.Equal("47.4k", Look.Tokens(47_400));
            Assert.Equal("3m07s", Look.Span(TimeSpan.FromSeconds(187)));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

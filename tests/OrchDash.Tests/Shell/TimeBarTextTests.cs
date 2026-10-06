using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Core.Timeline;
using OrchDash.Shell;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Shell;

public sealed class TimeBarTextTests
{
    private static readonly RunSnapshot Sample = SampleRun.CreateTimeline();
    private static readonly ImmutableArray<TimelineEvent> Events = TimelineBuilder.Build(Sample);
    private static readonly DateTimeOffset Start = SampleRun.At(12, 0, 0);
    private static readonly DateTimeOffset End = SampleRun.At(12, 30, 0);

    private static string Bar(string done, int muted) =>
        (done.Length == 0 ? "" : $"[accent]{done}[/]") + "[bold]●[/]" + (muted == 0 ? "" : $"[muted]{new string('─', muted)}[/]");

    [Fact]
    public void Live_shows_the_clocks_a_full_bar_and_live_in_success()
    {
        var text = TimeBarText.Build(Events, Sample.Run, End, null, null, null);

        Assert.Equal($"12:00:00 {Bar(new string('━', 49), 0)} 12:30:00  [success]live[/]", text);
    }

    [Fact]
    public void Replay_shows_the_cursor_cell_the_time_in_accent_and_the_events_up_to_it()
    {
        var text = TimeBarText.Build(Events, Sample.Run, End, SampleRun.At(12, 5, 0), null, null);

        // 49 × 5 / 30 = 8.17: cell 8.
        Assert.Equal(
            $"12:00:00 {Bar(new string('━', 8), 41)} 12:30:00  [accent]replay 12:05:00[/] · 24 of 60 events · git live",
            text);
    }

    [Fact]
    public void Replay_at_the_start_puts_the_cursor_on_the_first_cell()
    {
        var text = TimeBarText.Build(Events, Sample.Run, End, Start, null, null);

        Assert.Equal($"12:00:00 {Bar("", 49)} 12:30:00  [accent]replay 12:00:00[/] · 2 of 60 events · git live", text);
    }

    [Fact]
    public void The_host_loading_text_follows_in_muted_and_its_problem_in_warning()
    {
        var live = TimeBarText.Build(Events, Sample.Run, End, null, "20261003-110000", "Access [denied]\nagain");

        Assert.EndsWith(
            "  [success]live[/][muted] · loading 20261003-110000[/][warning] · Access [[denied]] again[/]",
            live,
            StringComparison.Ordinal);
        Assert.EndsWith(
            " · git live[warning] · boom[/]",
            TimeBarText.Build(Events, Sample.Run, End, Start, null, "boom"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Without_events_the_bar_says_so_in_muted()
    {
        var empty = RunSnapshot.Empty(SampleRun.RepoPath);

        Assert.Equal("[muted]no events yet[/]", TimeBarText.Build([], empty.Run, End, null, null, null));
        Assert.Equal("[muted]no events yet[/]", TimeBarText.Build(default, empty.Run, End, null, null, null));
        Assert.Equal(
            "[muted]no events yet[/][muted] · loading x[/]",
            TimeBarText.Build([], empty.Run, End, null, "x", null));
        Assert.Null(TimeBarText.Span([], empty.Run, End));
    }

    [Fact]
    public void The_span_ends_at_the_later_of_the_last_event_and_the_finish_or_the_clock()
    {
        var lastEvent = SampleRun.At(12, 29, 30);
        var finished = Sample.Run with { FinishedAt = SampleRun.At(12, 40, 0) };

        Assert.Equal((Start, End), TimeBarText.Span(Events, Sample.Run, End));
        Assert.Equal((Start, lastEvent), TimeBarText.Span(Events, Sample.Run, SampleRun.At(12, 10, 0)));
        Assert.Equal((Start, SampleRun.At(12, 40, 0)), TimeBarText.Span(Events, finished, SampleRun.At(13, 0, 0)));
        Assert.Equal((Start, lastEvent), TimeBarText.Span(Events, Sample.Run with { FinishedAt = Start }, End));
    }

    [Fact]
    public void The_cursor_is_the_last_cell_while_live_and_rounds_half_away_from_zero()
    {
        var end = Start.AddSeconds(98);

        Assert.Equal(49, TimeBarText.Cursor(Start, end, null));
        Assert.Equal(0, TimeBarText.Cursor(Start, end, Start));
        // 49 × 1 / 98 = 0.5 and 49 × 3 / 98 = 1.5.
        Assert.Equal(1, TimeBarText.Cursor(Start, end, Start.AddSeconds(1)));
        Assert.Equal(2, TimeBarText.Cursor(Start, end, Start.AddSeconds(3)));
        Assert.Equal(49, TimeBarText.Cursor(Start, end, end));
        Assert.Equal(0, TimeBarText.Cursor(Start, Start, Start));
    }

    [Fact]
    public void A_cell_time_divides_the_span_in_49_steps()
    {
        Assert.Equal(Start, TimeBarText.CellTime(Start, End, 0));
        Assert.Equal(End, TimeBarText.CellTime(Start, End, 49));
        Assert.Equal(Start + TimeSpan.FromTicks(TimeSpan.FromMinutes(30).Ticks * 10 / 49), TimeBarText.CellTime(Start, End, 10));
    }
}

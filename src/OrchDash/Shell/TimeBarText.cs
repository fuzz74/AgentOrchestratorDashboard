using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Shell;

/// <summary>
/// Builds the markup of the time bar (32.1): <c>&lt;start&gt; &lt;50 cells&gt; &lt;end&gt;  &lt;state&gt;</c>, and the
/// times of its span and cells. Pure.
/// </summary>
public static class TimeBarText
{
    public const string NoEvents = "no events yet";

    /// <summary>The number of bar cells; the last one means live.</summary>
    public const int CellCount = 50;

    /// <summary>The column of the first bar cell: after the start clock (<c>HH:mm:ss</c>) and a space.</summary>
    public const int BarColumn = 9;

    /// <summary>The column where the state text starts: after the cells, a space, the end clock and two spaces.</summary>
    public const int StateColumn = BarColumn + CellCount + 1 + 8 + 2;

    private const int LastCell = CellCount - 1;

    /// <summary>
    /// The span of the bar: the first event's time, and the later of the last event's time and
    /// <c>run.FinishedAt ?? now</c>; null without events. The events are in time order, as the timeline builder returns them.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End)? Span(ImmutableArray<TimelineEvent> events, RunInfo run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (events.IsDefaultOrEmpty)
        {
            return null;
        }
        var last = events[^1].Time;
        var finished = run.FinishedAt ?? now;
        return (events[0].Time, finished > last ? finished : last);
    }

    /// <summary>The cursor cell: the last cell while live, else the cell nearest to <paramref name="at"/> (0 for an empty span).</summary>
    public static int Cursor(DateTimeOffset start, DateTimeOffset end, DateTimeOffset? at)
    {
        if (at is not { } time)
        {
            return LastCell;
        }
        var span = (end - start).Ticks;
        if (span <= 0)
        {
            return 0;
        }
        var cell = Math.Round((double)LastCell * (time - start).Ticks / span, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(cell, 0, LastCell);
    }

    /// <summary>The time of a bar cell: <c>start + (end - start) × cell / 49</c>.</summary>
    public static DateTimeOffset CellTime(DateTimeOffset start, DateTimeOffset end, int cell) =>
        start + TimeSpan.FromTicks((end - start).Ticks * cell / LastCell);

    /// <summary>
    /// The whole bar line: <c>no events yet</c> without events, else the clocks, the cells and <c>live</c> or
    /// <c>replay &lt;clock&gt; · &lt;k&gt; of &lt;n&gt; events · git live</c>; then the host's loading and problem texts.
    /// </summary>
    /// <param name="events">The live timeline, in time order.</param>
    /// <param name="run">The live run.</param>
    /// <param name="now">The shell's clock.</param>
    /// <param name="at">The replay time; null while live.</param>
    /// <param name="loading">The run being switched to; null when none.</param>
    /// <param name="problem">The host's problem; null when none.</param>
    public static string Build(
        ImmutableArray<TimelineEvent> events, RunInfo run, DateTimeOffset now, DateTimeOffset? at, string? loading, string? problem)
    {
        var host = (loading is null ? "" : Look.Tag("muted", $" · loading {OneLine(loading)}"))
            + (problem is null ? "" : Look.Tag("warning", $" · {OneLine(problem)}"));
        if (Span(events, run, now) is not { } span)
        {
            return Look.Tag("muted", NoEvents) + host;
        }
        var (start, end) = span;
        var state = at is { } time
            ? Look.Tag("accent", $"replay {Look.Clock(time)}")
                + Look.Tag("", $" · {Words.Number(events.Count(e => e.Time <= time))} of {Words.Number(events.Length)} events · git live")
            : Look.Tag("success", "live");
        return $"{Look.Clock(start)} {Cells(Cursor(start, end, at))} {Look.Clock(end)}  {state}{host}";
    }

    /// <summary>The 50 cells: <c>━</c> before the cursor, <c>●</c> at it and <c>─</c> after it.</summary>
    private static string Cells(int cursor)
    {
        var before = cursor == 0 ? "" : Look.Tag("accent", new string('━', cursor));
        var after = cursor == LastCell ? "" : Look.Tag("muted", new string('─', LastCell - cursor));
        return before + Look.Tag("bold", "●") + after;
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ");
}

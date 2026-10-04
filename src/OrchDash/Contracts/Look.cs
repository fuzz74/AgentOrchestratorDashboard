using System.Globalization;
using OrchDash.Core.Model;
using XenoAtom.Ansi;

namespace OrchDash.Contracts;

public static class Look
{
    public static string Icon(TaskState s) => s switch
    {
        TaskState.Done => "✔",
        TaskState.Running => "▶",
        TaskState.Pending => "·",
        TaskState.Failed => "✖",
        TaskState.Blocked => "⊘",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    public static string Color(TaskState s) => s switch
    {
        TaskState.Done => "success",
        TaskState.Running => "primary",
        TaskState.Pending => "muted",
        TaskState.Failed => "error",
        TaskState.Blocked => "warning",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    public static string Icon(SessionState s) => s switch
    {
        SessionState.Succeeded => "✔",
        SessionState.Running => "▶",
        SessionState.Failed => "✖",
        SessionState.Aborted => "◌",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    public static string Color(SessionState s) => s switch
    {
        SessionState.Succeeded => "success",
        SessionState.Running => "primary",
        SessionState.Failed => "error",
        SessionState.Aborted => "muted",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    public static string Color(AgentRole r) => r switch
    {
        AgentRole.Worker => "cyan",
        AgentRole.Reviewer => "yellow",
        AgentRole.Resolver => "magenta",
        AgentRole.Planner => "blue",
        AgentRole.Bootstrap => "green",
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, null),
    };

    public static string Color(ProgressKind k) => k switch
    {
        ProgressKind.Info => "",
        ProgressKind.Activity => "muted",
        ProgressKind.Success => "success",
        ProgressKind.Warning => "warning",
        ProgressKind.Failure => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, null),
    };

    // "[color]escaped text[/]"; escaped text alone when color is ""
    public static string Tag(string color, string text)
    {
        var escaped = AnsiMarkup.Escape(text);
        return color.Length == 0 ? escaped : $"[{color}]{escaped}[/]";
    }

    // "1h05m", "3m07s", "12s"; whole seconds, rounded down; a negative span is "0s"
    public static string Span(TimeSpan t)
    {
        var seconds = t < TimeSpan.Zero ? 0 : t.Ticks / TimeSpan.TicksPerSecond;
        var hours = seconds / 3600;
        var minutes = seconds / 60 % 60;
        var rest = seconds % 60;
        if (hours > 0)
            return string.Create(CultureInfo.InvariantCulture, $"{hours}h{minutes:00}m");
        if (minutes > 0)
            return string.Create(CultureInfo.InvariantCulture, $"{minutes}m{rest:00}s");
        return string.Create(CultureInfo.InvariantCulture, $"{rest}s");
    }

    // local "HH:mm:ss"
    public static string Clock(DateTimeOffset t) =>
        t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    // "0.25 USD", invariant culture, 2 decimals
    public static string Usd(double v) =>
        v.ToString("0.00", CultureInfo.InvariantCulture) + " USD";

    // "950", "47.4k", "1.2M"
    public static string Tokens(long n)
    {
        if (n < 1_000)
            return n.ToString(CultureInfo.InvariantCulture);
        if (n < 1_000_000)
            return (n / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k";
        return (n / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M";
    }

    // 0.2 -> "20 %", 0.045 -> "5 %"; whole percent, midpoint away from zero
    public static string Percent(double fraction) =>
        Math.Round(fraction * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " %";

    // 12_930_990_000 -> "12.93 AIU"; nano-AIU / 1e9, 2 decimals
    public static string Aiu(long nanoAiu) =>
        (nanoAiu / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + " AIU";

    // "34.5k", or "34.5k of 200.0k (17 %)" with a limit above 0
    public static string ContextSize(long tokens, long? limit) =>
        limit is > 0
            ? $"{Tokens(tokens)} of {Tokens(limit.Value)} ({Percent((double)tokens / limit.Value)})"
            : Tokens(tokens);

    // local "HH:mm"
    public static string ShortClock(DateTimeOffset t) =>
        t.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    // local "yyyy-MM-dd HH:mm"
    public static string DateClock(DateTimeOffset t) =>
        t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

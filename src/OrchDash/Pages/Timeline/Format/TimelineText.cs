using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;
using OrchDash.Pages.Overview.Format;

namespace OrchDash.Pages.Timeline.Format;

/// <summary>
/// The text of the Timeline page (spec 33.1-33.4): the filter row labels, the filter itself, the rows by the row text
/// table and the pop-ups by the pop-up table in section 4.3. Markup goes through <see cref="Look.Tag"/>; plain texts,
/// cells and pop-up titles are not escaped.
/// </summary>
public static class TimelineText
{
    public const string NoEvents = "No events yet";
    public const string ReplayHere = "▶ replay here";
    public const string RunGroup = "run";

    public const int KindWidth = 6;

    private const string Gap = "  ";
    private const string Dot = " · ";
    private const string Missing = "-";

    /// <summary>The five kind toggles in the order of the filter row (33.2); <c>e</c> covers Prompt and Result.</summary>
    public static ImmutableArray<KindFilter> Filters { get; } =
    [
        new('o', "orchestrator", "Orchestrator", [TimelineKind.Orchestrator]),
        new('a', "calls", "Calls", [TimelineKind.Call]),
        new('u', "tools", "Tools", [TimelineKind.Tool]),
        new('x', "text", "Text", [TimelineKind.Text]),
        new('e', "prompt/result", "Prompt/result", [TimelineKind.Prompt, TimelineKind.Result]),
    ];

    /// <summary>Every kind: the kinds shown at the start.</summary>
    public static ImmutableHashSet<TimelineKind> AllKinds { get; } = [.. Enum.GetValues<TimelineKind>()];

    /// <summary>Whether every kind of the toggle is shown.</summary>
    public static bool IsActive(KindFilter filter, ImmutableHashSet<TimelineKind> shown) =>
        filter.Kinds.All(shown.Contains);

    /// <summary>The shown kinds with the toggle's kinds hidden when it is active, else shown (33.2).</summary>
    public static ImmutableHashSet<TimelineKind> Toggle(KindFilter filter, ImmutableHashSet<TimelineKind> shown) =>
        IsActive(filter, shown) ? shown.Except(filter.Kinds) : shown.Union(filter.Kinds);

    /// <summary><c>[u] tools</c> in the accent colour when active, else in muted.</summary>
    public static string FilterLabel(KindFilter filter, bool active) =>
        Look.Tag(active ? "accent" : "muted", $"[{filter.Key}] {filter.Name}");

    /// <summary><c>task: all</c> without a task filter, else <c>task: &lt;group&gt;</c> (33.3).</summary>
    public static string TaskLabel(string? group) => Look.Tag("", "task: " + (group ?? "all"));

    public static string ReplayLabel() => Look.Tag("", ReplayHere);

    /// <summary>
    /// The events of a shown kind and, with a task filter, of that <paramref name="group"/> or <c>run</c> (33.2, 33.3),
    /// in their order.
    /// </summary>
    public static ImmutableArray<TimelineEvent> Visible(
        ImmutableArray<TimelineEvent> events, ImmutableHashSet<TimelineKind> shown, string? group) =>
        [.. events.Where(e => shown.Contains(e.Kind) && (group is null || e.Group == group || e.Group == RunGroup))];

    /// <summary>The markup rows of the events, the source column padded to the longest source of the list.</summary>
    public static ImmutableArray<string> Rows(ImmutableArray<TimelineEvent> events)
    {
        var width = SourceWidth(events);
        return [.. events.Select(e => Row(e, width))];
    }

    /// <summary>The rows of <see cref="Rows"/> without markup.</summary>
    public static ImmutableArray<string> PlainRows(ImmutableArray<TimelineEvent> events)
    {
        var width = SourceWidth(events);
        return [.. events.Select(e => PlainRow(e, width))];
    }

    public static int SourceWidth(ImmutableArray<TimelineEvent> events) =>
        events.IsEmpty ? 0 : events.Max(e => Source(e).Length);

    /// <summary>One row: time and source plain, kind and text in the event's colour, two spaces between columns.</summary>
    public static string Row(TimelineEvent e, int sourceWidth)
    {
        var cells = Cells(e);
        var color = Color(e);
        return string.Join(Gap,
            Look.Tag("", cells.Time),
            Look.Tag("", cells.Source.PadRight(sourceWidth)),
            Look.Tag(color, cells.Kind.PadRight(KindWidth)),
            Look.Tag(color, cells.Text));
    }

    public static string PlainRow(TimelineEvent e, int sourceWidth)
    {
        var cells = Cells(e);
        return string.Join(Gap, cells.Time, cells.Source.PadRight(sourceWidth), cells.Kind.PadRight(KindWidth), cells.Text);
    }

    /// <summary>The four columns of an event's row by the row text table, without padding.</summary>
    public static TimelineCells Cells(TimelineEvent e) => new(Look.Clock(e.Time), Source(e), KindText(e.Kind), Text(e));

    /// <summary><c>orchestrator</c>, or <c>&lt;TaskId&gt; &lt;role&gt; &lt;attempt&gt;</c> of the event's session.</summary>
    public static string Source(TimelineEvent e)
    {
        if (e.Session is not { Files: var files })
        {
            return "orchestrator";
        }
        var agent = Words.Role(files.Role) + " " + Words.Attempt(files);
        return files.TaskId is { } taskId ? taskId + " " + agent : agent;
    }

    public static string KindText(TimelineKind kind) => kind switch
    {
        TimelineKind.Orchestrator => "orch",
        TimelineKind.Prompt => "prompt",
        TimelineKind.Call => "call",
        TimelineKind.Tool => "tool",
        TimelineKind.Text => "text",
        TimelineKind.Result => "result",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The text column of the row text table.</summary>
    public static string Text(TimelineEvent e) => e.Kind switch
    {
        TimelineKind.Orchestrator => OrchestratorText(e.Entry!),
        TimelineKind.Prompt => "prompt sent" + Dot + Words.Count(e.Session!.Prompt.Length, "char"),
        TimelineKind.Call => CallText(e.Call!, e.Index),
        TimelineKind.Tool => ToolText((ToolCall)e.Item!),
        TimelineKind.Text => TextPreview.FirstLines(((AssistantText)e.Item!).Text, 1) is [var first] ? first : "text",
        TimelineKind.Result => ResultText(e.Result!),
        _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, null),
    };

    /// <summary>The colour of the kind and text columns.</summary>
    public static string Color(TimelineEvent e) => e.Kind switch
    {
        TimelineKind.Orchestrator => Look.Color(e.Entry!.Kind),
        TimelineKind.Prompt => "accent",
        TimelineKind.Call => "muted",
        TimelineKind.Tool => ((ToolCall)e.Item!).Result switch
        {
            null => "primary",
            { IsError: true } => "error",
            _ => "success",
        },
        TimelineKind.Text => "",
        TimelineKind.Result => e.Result!.IsError ? "error" : "success",
        _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, null),
    };

    /// <summary>The pop-up title of an event by the pop-up table (33.4).</summary>
    public static string PopupTitle(TimelineEvent e) => e.Kind switch
    {
        TimelineKind.Orchestrator => "Log entry",
        TimelineKind.Call => "Call " + Words.Number(e.Index + 1),
        _ => Entry(e).PopupTitle,
    };

    /// <summary>The pop-up sections of an event by the pop-up table (33.4).</summary>
    public static IReadOnlyList<PopupSection> Popup(TimelineEvent e) => e.Kind switch
    {
        TimelineKind.Orchestrator => OverviewText.LogPopup(e.Entry!),
        TimelineKind.Call => [new PopupSection("Call", string.Join('\n', CallLines(e.Call!, e.Index)))],
        _ => Entry(e).Popup,
    };

    /// <summary>The lines of the <c>Call</c> section; <c>-</c> for an unknown value.</summary>
    public static ImmutableArray<string> CallLines(ModelCall call, int index) =>
    [
        "number: " + Words.Number(index + 1),
        "model: " + (string.IsNullOrEmpty(call.Model) ? Missing : call.Model),
        "started: " + (call.StartedAt is { } started ? Look.Clock(started) : Missing),
        "context: " + (call.Usage is { } usage ? Look.Tokens(usage.Context) : Missing),
        "output: " + (call.Usage?.Output is { } output ? Look.Tokens(output) : Missing),
        "thinking: " + (call.ThinkingTokens is { } thinking ? Look.Tokens(thinking) : Missing),
        "stop reason: " + (string.IsNullOrEmpty(call.StopReason) ? Missing : call.StopReason),
        "duration: " + (call.Duration is { } duration ? Look.Span(duration) : Missing),
    ];

    // The Conversation page's entry of a prompt, tool call, assistant text or result event.
    private static ConversationEntry Entry(TimelineEvent e) => e.Kind switch
    {
        TimelineKind.Prompt => ConversationText.PromptEntry(e.Session!.Prompt),
        TimelineKind.Tool or TimelineKind.Text => ConversationText.ItemEntry(e.Item!),
        TimelineKind.Result => ConversationText.ResultEntry(e.Result!),
        _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, null),
    };

    private static string OrchestratorText(ProgressEntry entry)
    {
        var line = entry.Message.Split('\n')[0].TrimEnd('\r');
        return entry.Source is { } source ? $"[{source}] {line}" : line;
    }

    private static string CallText(ModelCall call, int index)
    {
        var text = "call " + Words.Number(index + 1);
        if (!string.IsNullOrEmpty(call.Model))
            text += Dot + call.Model;
        if (call.Usage is { } usage)
            text += Dot + "context " + Look.Tokens(usage.Context);
        return text;
    }

    private static string ToolText(ToolCall call)
    {
        var text = (call.Summary.Length > 0 ? call.Summary : call.Name) + Dot + call.Result switch
        {
            null => "running",
            { IsError: true } => "error",
            _ => "ok",
        };
        if (call.Result?.Time is { } end && call.Time is { } start)
            text += Dot + Look.Span(end - start);
        return text;
    }

    private static string ResultText(SessionResult result)
    {
        var text = "result " + result.Subtype;
        if (result.Worker is { } worker)
            return text + Dot + worker.Status;
        if (result.Review is { } review)
            return text + Dot + $"spec {review.SpecVerdict}, quality {review.QualityVerdict}, {Words.Count(review.Issues.Length, "issue")}";
        return text;
    }
}

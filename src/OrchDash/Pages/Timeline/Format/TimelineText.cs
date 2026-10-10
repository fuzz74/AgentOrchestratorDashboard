using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;
using OrchDash.Pages.Overview.Format;

namespace OrchDash.Pages.Timeline.Format;

/// <summary>
/// The text of the Timeline page (spec 33.1-33.4, 44.1-44.4): the filter row labels, the filter itself, the rows by the
/// row text table and the pop-ups by the pop-up table in section 4.3. Markup goes through <see cref="Look.Tag"/>; plain
/// texts, cells and pop-up titles are not escaped.
/// </summary>
public static class TimelineText
{
    public const string NoEvents = "No events yet";
    public const string ReplayHere = "▶ replay here";
    public const string RunGroup = "run";

    /// <summary>The sub-agents toggle of the filter row and the command bar (44.4).</summary>
    public const char SubAgentKey = 's';
    public const string SubAgentName = "sub-agents";
    public const string SubAgentCommandLabel = "Sub-agents";

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
    public static string FilterLabel(KindFilter filter, bool active) => ToggleLabel(filter.Key, filter.Name, active);

    /// <summary><c>[s] sub-agents</c> in the accent colour while sub-agent events are shown, else in muted (44.4).</summary>
    public static string SubAgentLabel(bool active) => ToggleLabel(SubAgentKey, SubAgentName, active);

    /// <summary>Whether a session of the snapshot has sub-agents: the filter row then shows <see cref="SubAgentLabel"/> (44.4).</summary>
    public static bool HasSubAgents(RunSnapshot snapshot) =>
        !snapshot.Sessions.IsDefault && snapshot.Sessions.Any(session => !session.Content.SubAgents.IsDefaultOrEmpty);

    /// <summary><c>task: all</c> without a task filter, else <c>task: &lt;group&gt;</c> (33.3).</summary>
    public static string TaskLabel(string? group) => Look.Tag("", "task: " + (group ?? "all"));

    public static string ReplayLabel() => Look.Tag("", ReplayHere);

    /// <summary>
    /// The events of a shown kind and, with a task filter, of that <paramref name="group"/> or <c>run</c> (33.2, 33.3),
    /// in their order. An event with an AgentId is kept with its session's task, whose group it has, and only while
    /// <paramref name="subAgents"/> is set (44.3, 44.4).
    /// </summary>
    public static ImmutableArray<TimelineEvent> Visible(
        ImmutableArray<TimelineEvent> events, ImmutableHashSet<TimelineKind> shown, string? group, bool subAgents = true) =>
        [.. events.Where(e => shown.Contains(e.Kind) && (subAgents || e.AgentId is null) &&
            (group is null || e.Group == group || e.Group == RunGroup))];

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

    /// <summary>
    /// <c>orchestrator</c>, or the path of the event's agent: <c>&lt;TaskId&gt; &lt;role&gt; &lt;attempt&gt;</c> of its
    /// session, then <c> › &lt;Name&gt;</c> per sub-agent down to the event's (44.1).
    /// </summary>
    public static string Source(TimelineEvent e) =>
        e.Session is { } session ? AgentPath.Of(session, e.AgentId) : "orchestrator";

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

    /// <summary>The text column of the row text table; a sub-agent's prompt and result by 44.2.</summary>
    public static string Text(TimelineEvent e) => e.Kind switch
    {
        TimelineKind.Orchestrator => OrchestratorText(e.Entry!),
        TimelineKind.Prompt when e.AgentId is not null => SubAgentPromptText(SubAgentOf(e)),
        TimelineKind.Prompt => "prompt sent" + Dot + Words.Count(e.Session!.Prompt.Length, "char"),
        TimelineKind.Call => CallText(e.Call!, CallIndex(e)),
        TimelineKind.Tool => ToolText((ToolCall)e.Item!),
        TimelineKind.Text => TextPreview.FirstLines(((AssistantText)e.Item!).Text, 1) is [var first] ? first : "text",
        TimelineKind.Result when e.AgentId is not null => SubAgentResultText(e.Session!, SubAgentOf(e)),
        TimelineKind.Result => ResultText(e.Result!),
        _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, null),
    };

    /// <summary>The colour of the kind and text columns; a sub-agent's result in its state's colour (44.2).</summary>
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
        TimelineKind.Result when e.AgentId is not null =>
            SubAgentOf(e) is { } sub ? Look.Color(SubAgents.StateOf(e.Session!, sub)) : "",
        TimelineKind.Result => e.Result!.IsError ? "error" : "success",
        _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, null),
    };

    /// <summary>The pop-up title of an event by the pop-up table (33.4); a call's number among its agent's calls (44.2).</summary>
    public static string PopupTitle(TimelineEvent e) => e.Kind switch
    {
        TimelineKind.Orchestrator => "Log entry",
        TimelineKind.Call => "Call " + Words.Number(CallIndex(e) + 1),
        _ => Entry(e).PopupTitle,
    };

    /// <summary>The pop-up sections of an event by the pop-up table (33.4, 44.2).</summary>
    public static IReadOnlyList<PopupSection> Popup(TimelineEvent e) => e.Kind switch
    {
        TimelineKind.Orchestrator => OverviewText.LogPopup(e.Entry!),
        TimelineKind.Call => [new PopupSection("Call", string.Join('\n', CallLines(e.Call!, CallIndex(e))))],
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

    // The Conversation page's entry of a prompt, tool call, assistant text or result event; a sub-agent's prompt and
    // result show its Prompt and Report (44.2), and nothing when the session no longer has it.
    private static ConversationEntry Entry(TimelineEvent e) => e.Kind switch
    {
        TimelineKind.Prompt when e.AgentId is not null => SubAgentOf(e) is { } sub
            ? ConversationText.SubAgentPromptEntry(sub)
            : ConversationText.PromptEntry(""),
        TimelineKind.Prompt => ConversationText.PromptEntry(e.Session!.Prompt),
        TimelineKind.Tool or TimelineKind.Text => ConversationText.ItemEntry(e.Item!),
        TimelineKind.Result when e.AgentId is not null => SubAgentOf(e) is { } sub
            ? ConversationText.SubAgentResultEntry(e.Session!, sub)
            : new ConversationEntry(EntryKind.Result, [], "Result", []),
        TimelineKind.Result => ConversationText.ResultEntry(e.Result!),
        _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, null),
    };

    // The SubAgent of an event with an AgentId; null without one, or when the session does not have it.
    private static SubAgent? SubAgentOf(TimelineEvent e) =>
        e.Session is { } session ? SubAgents.Find(session.Content, e.AgentId) : null;

    // 44.2: a call's index among the calls of its agent (the agent's own for a null AgentId): those that share its AgentId
    // and come before it in Content.Calls. Copilot turn ids repeat across agents (35.4), so the call is found by its
    // position; an index past the session's calls is kept as it is.
    private static int CallIndex(TimelineEvent e)
    {
        if (e.Session is not { Content.Calls: { IsDefault: false } calls } || e.Index > calls.Length)
            return e.Index;
        var index = 0;
        for (var i = 0; i < e.Index; i++)
        {
            if (string.Equals(calls[i].AgentId, e.AgentId, StringComparison.Ordinal))
                index++;
        }
        return index;
    }

    // 39.3, 44.2: "[<Source> › <SubAgent>]", "[<SubAgent>]" without a source, "[<Source>]" without a SubAgent.
    private static string OrchestratorText(ProgressEntry entry)
    {
        var line = entry.Message.Split('\n')[0].TrimEnd('\r');
        var path = entry.SubAgent switch
        {
            null => entry.Source,
            { } sub when entry.Source is null => sub,
            { } sub => entry.Source + AgentPath.Separator + sub,
        };
        return path is not null ? $"[{path}] {line}" : line;
    }

    // 44.2: "sub-agent started · <AgentType|-> · <n> chars"; "sub-agent started" without its SubAgent.
    private static string SubAgentPromptText(SubAgent? sub) =>
        sub is null
            ? "sub-agent started"
            : "sub-agent started" + Dot + (string.IsNullOrEmpty(sub.AgentType) ? Missing : sub.AgentType) + Dot +
              Words.Count(sub.Prompt.Length, "char");

    // 44.2: "result <state>[ · <Report's first line>]"; "result" without its SubAgent.
    private static string SubAgentResultText(Session session, SubAgent? sub)
    {
        if (sub is null)
            return "result";
        var text = "result " + Words.State(SubAgents.StateOf(session, sub));
        return TextPreview.FirstLines(sub.Report, 1) is [var first] ? text + Dot + first : text;
    }

    private static string ToggleLabel(char key, string name, bool active) =>
        Look.Tag(active ? "accent" : "muted", $"[{key}] {name}");

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

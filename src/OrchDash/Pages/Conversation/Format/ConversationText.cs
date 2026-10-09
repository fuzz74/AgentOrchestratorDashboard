using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Conversation.Format;

// The text of the Conversation page (8.1, 8.3-8.6, 8.9, 41.1-41.4): session and child rows, the entries of an agent
// or a sub-agent, and their pop-ups. Lines are markup; every piece of model text goes through Look.Tag. Pop-up titles
// and texts are plain text.
public static class ConversationText
{
    private const int PreviewLines = 3;
    private const string Dot = " · ";

    // Bootstrap, planner, then each task's sessions in task order; sessions of unknown tasks last.
    // Within each group the snapshot order is kept.
    public static ImmutableArray<Session> OrderSessions(RunSnapshot s)
    {
        var taskRank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < s.Tasks.Length; i++)
            taskRank.TryAdd(s.Tasks[i].Id, i);

        int Rank(Session session) => session.Files.Role switch
        {
            AgentRole.Bootstrap => -2,
            AgentRole.Planner => -1,
            _ => session.Files.TaskId is { } id && taskRank.TryGetValue(id, out var rank) ? rank : int.MaxValue,
        };

        return [.. s.Sessions.OrderBy(Rank)];
    }

    public static string SessionRow(Session session, DateTimeOffset now)
    {
        var files = session.Files;
        var parts = new List<string>
        {
            Look.Tag(Look.Color(session.State), Look.Icon(session.State)),
            Look.Tag("", files.TaskId ?? Words.Role(files.Role)),
            Look.Tag(Look.Color(files.Role), Words.Role(files.Role)),
            Words.Attempt(files),
        };
        if (!string.IsNullOrEmpty(session.Content.Model))
            parts.Add(Look.Tag("", session.Content.Model));
        if (Duration(session, now) is { } duration)
            parts.Add(Look.Span(duration));
        parts.Add(ToolCalls(session.Content, null));
        if (session.Content.Result?.CostUsd is { } cost)
            parts.Add(Look.Usd(cost));
        return string.Join(' ', parts);
    }

    // A child row (41.1): "  <Prefix><icon> <Name> · <AgentType|-> · <Model|-> · <span> · <n tool calls>", with the
    // tree lines muted and the icon in the state colour.
    public static string ChildRow(AgentRow row, DateTimeOffset now)
    {
        var (session, sub) = (row.Session, row.SubAgent);
        var state = SubAgents.StateOf(session, sub);
        var fields = string.Join(Dot, Look.Tag("", sub.Name), OrDash(sub.AgentType), OrDash(sub.Model),
            SubAgentSpan(sub, now), ToolCalls(session.Content, sub.Id));
        return "  " + Look.Tag("muted", row.Prefix) + Look.Tag(Look.Color(state), Look.Icon(state)) + " " + fields;
    }

    // "n tool calls" of the agent's own items (agentId null) or of a sub-agent's, without its sub-agents' (41.1, 41.2).
    private static string ToolCalls(SessionContent content, string? agentId) =>
        Words.Count(SubAgents.Items(content, agentId).OfType<ToolCall>().Count(), "tool call");

    // From StartedAt to FinishedAt, or to now while it has none (41.1); "-" without a start.
    private static string SubAgentSpan(SubAgent sub, DateTimeOffset now) =>
        sub.StartedAt is { } start ? Look.Span((sub.FinishedAt ?? now) - start) : "-";

    // The text, or "-" when it is null or empty.
    private static string OrDash(string? text) => string.IsNullOrEmpty(text) ? "-" : Look.Tag("", text);

    // Result.Duration; else, while running, the time since the start; else the start to the last event.
    public static TimeSpan? Duration(Session session, DateTimeOffset now)
    {
        if (session.Content.Result?.Duration is { } duration)
            return duration;
        if (session.StartedAt is not { } start)
            return null;
        if (session.State == SessionState.Running)
            return now - start;
        return session.Content.LastEventAt - start;
    }

    public static ImmutableArray<ConversationEntry> Entries(Session session, DateTimeOffset now)
    {
        var entries = ImmutableArray.CreateBuilder<ConversationEntry>();
        entries.Add(Header(session, now));
        entries.Add(PromptEntry(session.Prompt));

        if (!session.Files.HasEventsFile)
        {
            entries.Add(new ConversationEntry(EntryKind.NoEventLog, [Look.Tag("muted", "no event log")], "", []));
            return entries.ToImmutable();
        }

        // Only the agent's own items, with its call separators numbered among its own calls (41.2).
        var content = session.Content;
        AddItems(entries, session, SubAgents.Items(content, null), SubAgents.Calls(content, null));

        if (content.Result is { } result)
            entries.Add(ResultEntry(result));
        return entries.ToImmutable();
    }

    // The entries of a sub-agent (41.4): its header, its prompt, its items and, once it no longer runs, its result.
    // An id that the session does not have gives the session's entries, as the session list picks the session then.
    public static ImmutableArray<ConversationEntry> SubAgentEntries(Session session, string agentId, DateTimeOffset now)
    {
        var content = session.Content;
        if (SubAgents.Find(content, agentId) is not { } sub)
            return Entries(session, now);

        var entries = ImmutableArray.CreateBuilder<ConversationEntry>();
        entries.Add(SubAgentHeader(session, sub, now));
        entries.Add(SubAgentPromptEntry(sub));
        AddItems(entries, session, SubAgents.Items(content, sub.Id), SubAgents.Calls(content, sub.Id));
        if (SubAgents.StateOf(session, sub) != SessionState.Running)
            entries.Add(SubAgentResultEntry(session, sub));
        return entries.ToImmutable();
    }

    // The items of one agent or sub-agent, which all have the AgentId of `calls`. A call separator comes wherever the
    // call changes to one of `calls` (8.3, 41.2), and a ToolCall that started a sub-agent gets its start entry (41.3).
    private static void AddItems(ImmutableArray<ConversationEntry>.Builder entries, Session session,
        ImmutableArray<ConversationItem> items, ImmutableArray<ModelCall> calls)
    {
        string? previousCallId = null;
        foreach (var item in items)
        {
            if (item.CallId is { } callId && callId != previousCallId && CallSeparator(calls, callId) is { } separator)
                entries.Add(separator);
            previousCallId = item.CallId;
            entries.Add(item is ToolCall call && SubAgents.StartedBy(session.Content, call) is { } sub
                ? SubAgentStartEntry(session, call, sub)
                : ItemEntry(item));
        }
    }

    // The sub-agent header (41.4): "sub-agent <path>", then
    // "<AgentType|-> · <Model|-> · <icon> <state> · started HH:mm:ss · <span> · <n tool calls>".
    public static ConversationEntry SubAgentHeader(Session session, SubAgent sub, DateTimeOffset now)
    {
        var state = SubAgents.StateOf(session, sub);
        var path = AgentPath.Of(session, sub.Id);
        string[] second =
        [
            OrDash(sub.AgentType),
            OrDash(sub.Model),
            Look.Tag(Look.Color(state), Look.Icon(state) + " " + Words.State(state)),
            "started " + (sub.StartedAt is { } start ? Look.Clock(start) : "-"),
            SubAgentSpan(sub, now),
            ToolCalls(session.Content, sub.Id),
        ];
        return new ConversationEntry(EntryKind.Header,
            [Look.Tag("accent", "sub-agent") + " " + Look.Tag("", path), string.Join(Dot, second)],
            path, HeaderPopup.SubAgentSections(session, sub));
    }

    // The sub-agent's prompt (41.4); Enter opens the whole prompt.
    public static ConversationEntry SubAgentPromptEntry(SubAgent sub) => PromptEntry(sub.Prompt);

    // The ToolCall that started a sub-agent (41.3): "<icon> sub-agent <Name> · <AgentType|-> · <state>", the icon in the
    // state colour, then the Report's first line, muted. Enter selects the sub-agent (SelectsAgentId); the pop-up, for
    // pages that open one, is the call's own.
    public static ConversationEntry SubAgentStartEntry(Session session, ToolCall call, SubAgent sub)
    {
        var state = SubAgents.StateOf(session, sub);
        var first = Look.Tag(Look.Color(state), Look.Icon(state)) + " " + Look.Tag("accent", "sub-agent") + " " +
            string.Join(Dot, Look.Tag("", sub.Name), OrDash(sub.AgentType), Words.State(state));
        ImmutableArray<string> lines = TextPreview.FirstLines(sub.Report, 1) is [var report]
            ? [first, Look.Tag("muted", report)]
            : [first];
        var callEntry = ToolCallEntry(call);
        return new ConversationEntry(EntryKind.SubAgent, lines, callEntry.PopupTitle, callEntry.Popup)
        {
            SelectsAgentId = sub.Id,
        };
    }

    // The sub-agent's result (41.4): "result <state>" in the state colour with the Report's first lines; Enter opens the
    // whole Report.
    public static ConversationEntry SubAgentResultEntry(Session session, SubAgent sub)
    {
        var state = SubAgents.StateOf(session, sub);
        var label = Look.Tag(Look.Color(state), "result " + Words.State(state));
        ImmutableArray<PopupSection> popup = string.IsNullOrEmpty(sub.Report) ? [] : [new PopupSection("Report", sub.Report)];
        return new ConversationEntry(EntryKind.Result, Lines(label, TextPreview.FirstLines(sub.Report, PreviewLines)),
            "Result", popup);
    }

    private static ConversationEntry Header(Session session, DateTimeOffset now)
    {
        var files = session.Files;
        var content = session.Content;

        var first = new List<string>
        {
            Look.Tag("", session.Provider.ToString()),
            Look.Tag(Look.Color(files.Role), Words.Role(files.Role)),
        };
        if (files.TaskId is { } taskId)
            first.Add(Look.Tag("", taskId));
        if (!string.IsNullOrEmpty(content.Model))
            first.Add(Look.Tag("", content.Model));
        first.Add(Look.Tag(Look.Color(session.State), Look.Icon(session.State) + " " + Words.State(session.State)));

        var second = new List<string>();
        if (!string.IsNullOrEmpty(content.SessionId))
            second.Add(Look.Tag("", "session " + content.SessionId));
        if (session.StartedAt is { } start)
            second.Add("started " + Look.Clock(start));
        if (Duration(session, now) is { } duration)
            second.Add(Look.Span(duration));
        if (content.Result?.Turns is { } turns)
            second.Add(Words.Count(turns, "turn"));
        if (content.Result?.CostUsd is { } cost)
            second.Add(Look.Usd(cost));

        var lines = ImmutableArray.CreateBuilder<string>(3);
        lines.Add(string.Join(' ', first));
        if (second.Count > 0)
            lines.Add(string.Join(Dot, second));
        if (content.UnparsedLines > 0)
            lines.Add(Look.Tag("warning", $"{Words.Number(content.UnparsedLines)} lines not understood"));

        return new ConversationEntry(EntryKind.Header, lines.ToImmutable(), files.Key, HeaderPopup.Sections(session));
    }

    public static ConversationEntry PromptEntry(string prompt)
    {
        var label = Look.Tag("accent", "prompt") + " " + Look.Tag("muted", Words.Count(prompt.Length, "char"));
        return new ConversationEntry(EntryKind.Prompt, Lines(label, TextPreview.FirstLines(prompt, PreviewLines)),
            "Prompt", [new PopupSection("Prompt", prompt)]);
    }

    private static ConversationEntry? CallSeparator(ImmutableArray<ModelCall> calls, string callId)
    {
        var index = -1;
        for (var i = 0; i < calls.Length && index < 0; i++)
        {
            if (calls[i].Id == callId)
                index = i;
        }
        if (index < 0)
            return null;

        var call = calls[index];
        var text = "call " + Words.Number(index + 1);
        if (call.StartedAt is { } start)
            text += Dot + Look.Clock(start);
        if (call.Usage is { } usage)
            text += Dot + "context " + Look.Tokens(usage.Context);
        return new ConversationEntry(EntryKind.CallSeparator, [Look.Tag("muted", text)], "", []);
    }

    public static ConversationEntry ItemEntry(ConversationItem item) => item switch
    {
        AssistantText text => TextEntry(EntryKind.AssistantText, "", text.Text, "", "Assistant text"),
        Thinking thinking => ThinkingEntry(thinking),
        ToolCall call => ToolCallEntry(call),
        UserText user => TextEntry(EntryKind.UserText, Label("accent", user.IsSynthetic ? "user (synthetic)" : "user"),
            user.Text, "", "User text"),
        Notice notice => TextEntry(EntryKind.Notice, Label("warning", notice.Kind), notice.Text, "", notice.Kind),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item.GetType().Name, null),
    };

    private static ConversationEntry TextEntry(EntryKind kind, string label, string text, string textColor, string title) =>
        new(kind, Lines(label, TextPreview.FirstLines(text, PreviewLines), textColor), title, [new PopupSection("Text", text)]);

    private static ConversationEntry ThinkingEntry(Thinking thinking)
    {
        if (!string.IsNullOrWhiteSpace(thinking.Text))
            return TextEntry(EntryKind.Thinking, "", thinking.Text, "muted", "Thinking");

        var text = thinking.EstimatedTokens is { } tokens
            ? $"thinking (~{Look.Tokens(tokens)} tokens, no text)"
            : "thinking (no text)";
        return new ConversationEntry(EntryKind.Thinking, [Look.Tag("muted", text)], "Thinking", [new PopupSection("Text", text)]);
    }

    private static ConversationEntry ToolCallEntry(ToolCall call)
    {
        var summary = call.Summary.Length > 0 ? call.Summary : call.Name;
        var result = call.Result;

        var first = new List<string>
        {
            Look.Tag("", summary),
            result is null ? Look.Tag("primary", "running")
                : result.IsError ? Look.Tag("error", "error")
                : Look.Tag("success", "ok"),
        };
        if (result?.Time is { } end && call.Time is { } start)
            first.Add(Look.Span(end - start));

        var lines = ImmutableArray.CreateBuilder<string>(2);
        lines.Add(string.Join(' ', first));
        if (result is not null && TextPreview.FirstLines(result.Content, 1) is [var output])
            lines.Add(Look.Tag("muted", output));

        var sections = ImmutableArray.CreateBuilder<PopupSection>(3);
        sections.Add(new PopupSection("Input", call.InputJson, TextKind.Json));
        if (result is not null)
        {
            var text = result.ExitCode is { } code ? $"exit code: {Words.Number(code)}\n{result.Content}" : result.Content;
            sections.Add(new PopupSection("Result", text));
            if (!string.IsNullOrEmpty(result.Diff))
                sections.Add(new PopupSection("Diff", result.Diff, TextKind.Diff));
        }

        return new ConversationEntry(EntryKind.ToolCall, lines.ToImmutable(), summary, sections.ToImmutable());
    }

    public static ConversationEntry ResultEntry(SessionResult result)
    {
        var label = Look.Tag(result.IsError ? "error" : "success", "result " + result.Subtype);

        ImmutableArray<string> lines;
        if (result.Worker is { } worker)
        {
            var status = Label(worker.Status == "done" ? "success" : "warning", worker.Status);
            lines = Lines(Join(label, status), TextPreview.FirstLines(worker.Summary, PreviewLines));
        }
        else if (result.Review is { } review)
        {
            lines =
            [
                Join(label, "spec", Verdict(review.SpecVerdict), "quality", Verdict(review.QualityVerdict),
                    Words.Count(review.Issues.Length, "issue")),
            ];
        }
        else
        {
            lines = Lines(label, TextPreview.FirstLines(result.Text, PreviewLines));
        }

        var sections = ImmutableArray.CreateBuilder<PopupSection>(3);
        if (!string.IsNullOrEmpty(result.Text))
            sections.Add(new PopupSection("Text", result.Text));
        if (!string.IsNullOrEmpty(result.StructuredJson))
            sections.Add(new PopupSection("Structured", result.StructuredJson, TextKind.Json));
        if (result.Review is { Issues.IsEmpty: false } withIssues)
            sections.Add(new PopupSection("Issues", string.Join('\n', withIssues.Issues.Select(Issue))));

        return new ConversationEntry(EntryKind.Result, lines, "Result", sections.ToImmutable());
    }

    private static string Verdict(string verdict) => Look.Tag(verdict == "pass" ? "success" : "error", verdict);

    private static string Issue(ReviewIssue issue) =>
        string.IsNullOrEmpty(issue.File)
            ? issue.Severity + Dot + issue.Description
            : issue.Severity + Dot + issue.File + Dot + issue.Description;

    // A label in its colour, or "" when the label text is empty.
    private static string Label(string color, string text) => text.Length == 0 ? "" : Look.Tag(color, text);

    private static string Join(params string[] parts) => string.Join(' ', parts.Where(part => part.Length > 0));

    // Entry lines: the label at the start of the first line, then the text lines in the text colour.
    private static ImmutableArray<string> Lines(string label, ImmutableArray<string> textLines, string textColor = "")
    {
        if (textLines.IsEmpty)
            return [label];

        var lines = ImmutableArray.CreateBuilder<string>(textLines.Length);
        for (var i = 0; i < textLines.Length; i++)
        {
            var line = Look.Tag(textColor, textLines[i]);
            lines.Add(i == 0 ? Join(label, line) : line);
        }
        return lines.MoveToImmutable();
    }
}

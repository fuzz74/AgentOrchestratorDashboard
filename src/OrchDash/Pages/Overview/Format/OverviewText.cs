using System.Collections.Immutable;
using System.Globalization;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Pages.Overview.Format;

// The text of the Overview page (spec 7.1-7.9, 39): markup lines and pop-up sections built from the model.
// Every piece of model text in a markup line goes through Look.Tag; PopupSection.Text is plain text.
public static class OverviewText
{
    public const string Missing = "-";
    public const int WaveWidth = 4;
    public const int AttemptsWidth = 3;
    public const int CostWidth = 10;
    public const int ElapsedWidth = 7;
    // The width of a task row without its id, title and detail: icon, wave, attempts, cost, elapsed and the 7 gaps.
    public const int FixedRowWidth = 1 + WaveWidth + AttemptsWidth + CostWidth + ElapsedWidth + 7 * 2;
    public const int MinTitleWidth = 12;
    public const int RunningItemCount = 5;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private const string ColumnGap = "  ";
    private const string Separator = " · ";
    private const string ItemIndent = "  ";
    // The icon and the gap after it, so that a child row starts at the id column of its task row (39.1).
    private const string ChildIndent = "   ";
    private const string PopupChildIndent = "  ";

    private static readonly TaskState[] TaskStates = Enum.GetValues<TaskState>();

    // 7.1: one markup line per value of the run panel.
    public static IReadOnlyList<string> RunPanel(RunSnapshot s, DateTimeOffset now)
    {
        var run = s.Run;
        var counts = TaskStates.Select(state =>
            Look.Tag(Look.Color(state), $"{Look.Icon(state)} {Count(s, state)} {state.ToString().ToLowerInvariant()}"));
        var lines = new List<string>
        {
            Field("Phase", run.Phase.ToString()),
            Field("Started", Time(run.StartedAt)),
            Field("Elapsed", Elapsed(run.StartedAt, run.FinishedAt, now)),
            Field("Provider", ProviderText(run.Provider)),
            Field("Max parallel", Number(run.MaxParallel)),
            "Tasks: " + string.Join(ColumnGap, counts),
            Field("Cost", Look.Usd(s.Tasks.Sum(t => t.CostUsd))),
        };
        if (run.StopRequested)
            lines.Add(Look.Tag("warning", "stop requested"));
        return lines;
    }

    // Done tasks over all tasks, 0 without tasks.
    public static double Progress(RunSnapshot s) =>
        s.Tasks.IsEmpty ? 0 : (double)Count(s, TaskState.Done) / s.Tasks.Length;

    // 7.2: every RunInfo and PlanInfo value as "name: value"; without a plan only the run values.
    public static IReadOnlyList<PopupSection> RunPopup(RunSnapshot s)
    {
        var run = s.Run;
        var sections = new List<PopupSection>
        {
            new("Run", Lines(
                ("phase", run.Phase.ToString()),
                ("started", Time(run.StartedAt)),
                ("finished", Time(run.FinishedAt)),
                ("max parallel", Number(run.MaxParallel)),
                ("provider", ProviderText(run.Provider)),
                ("agent path", Or(run.AgentPath)),
                ("stop requested", run.StopRequested ? "true" : "false"))),
        };
        if (s.Plan is { } plan)
        {
            sections.Add(new PopupSection("Plan", Lines(
                ("spec", Or(plan.Spec)),
                ("base branch", Or(plan.BaseBranch)),
                ("integration branch", Or(plan.IntegrationBranch)))));
            AddIfText(sections, "Settings", Lines([.. plan.Settings.Select(p => (p.Key, p.Value))]));
        }
        return sections;
    }

    // 7.3: plain cells icon, id, W<wave>, title, detail, attempts, cost, elapsed (empty when not started).
    public static IReadOnlyList<string> TaskCells(TaskView t, DateTimeOffset now) =>
    [
        Look.Icon(t.Status),
        t.Id,
        "W" + t.Wave.ToString(CultureInfo.InvariantCulture),
        t.Title,
        t.Detail,
        t.Attempts.ToString(CultureInfo.InvariantCulture),
        Look.Usd(t.CostUsd),
        t.StartedAt is null ? "" : Elapsed(t.StartedAt, t.FinishedAt, now),
    ];

    // 7.3: one task row in the status colour; columns are separated by two spaces, numbers right-aligned.
    public static string TaskRow(TaskView t, DateTimeOffset now, int idWidth, int titleWidth, int detailWidth) =>
        Look.Tag(Look.Color(t.Status), Columns(TaskCells(t, now), idWidth, titleWidth, detailWidth));

    // 39.1: the rows of the task table: each task, followed by the child rows of its sessions in snapshot order.
    public static IReadOnlyList<OverviewRow> TaskRows(RunSnapshot s)
    {
        var rows = new List<OverviewRow>();
        foreach (var task in s.Tasks)
        {
            rows.Add(new OverviewRow(task, null));
            rows.AddRange(AgentTree.Rows(s.Sessions.Where(x => x.Files.TaskId == task.Id))
                .Select(child => new OverviewRow(task, child)));
        }
        return rows;
    }

    // 39.1: " <Prefix><icon> <Name> · <role> <attempt> · <n tool calls> · <span>", starting at the id column and cut to
    // the row width, in the colour of the sub-agent's state.
    public static string ChildRow(AgentRow row, DateTimeOffset now, int width)
    {
        var (session, sub) = (row.Session, row.SubAgent);
        var state = SubAgents.StateOf(session, sub);
        var text = string.Join(Separator,
            $" {row.Prefix}{Look.Icon(state)} {sub.Name}",
            Words.Role(session.Files.Role) + " " + Words.Attempt(session.Files),
            ToolCalls(session.Content, sub.Id),
            Elapsed(sub.StartedAt, sub.FinishedAt, now));
        return Look.Tag(Look.Color(state), Cut(ChildIndent + text, width));
    }

    // The header of the task table, aligned with TaskRow.
    public static string TaskHeader(int idWidth, int titleWidth, int detailWidth) =>
        Look.Tag("muted", Columns([" ", "Id", "Wave", "Title", "Detail", "Att", "Cost", "Elapsed"],
            idWidth, titleWidth, detailWidth));

    // 7.3: the id, title and detail widths for task rows of the given width. The id column fits every id; the detail
    // column comes next, up to its longest cell, while it leaves the title MinTitleWidth; the title takes the rest, up to
    // its longest cell. Each column is at least as wide as its header.
    public static (int Id, int Title, int Detail) TaskWidths(IReadOnlyList<TaskView> tasks, int width)
    {
        var id = Longest(tasks, t => t.Id, "Id");
        var longestTitle = Longest(tasks, t => t.Title, "Title");
        var longestDetail = Longest(tasks, t => t.Detail, "Detail");
        var rest = Math.Max(0, width - FixedRowWidth - id);
        var detail = Math.Min(longestDetail, Math.Max(0, rest - Math.Min(longestTitle, MinTitleWidth)));
        var title = Math.Min(longestTitle, rest - detail);
        return (id, title, detail);
    }

    // 7.4, 27.2: Status, Plan, Prompt, Summary, Notes, Error, Feedback, Sessions, Processes; empty sections left out.
    // 39.4: each session line is followed by the lines of its sub-agents.
    public static IReadOnlyList<PopupSection> TaskPopup(TaskView t, RunSnapshot s)
    {
        var sections = new List<PopupSection>();
        AddIfText(sections, "Status", Lines(
            ("status", t.Status.ToString()),
            ("mode", Or(t.Mode)),
            ("attempts", Number(t.Attempts)),
            ("sync runs", Number(t.SyncRuns)),
            ("spec rejections", Number(t.SpecRejections)),
            ("cost", Look.Usd(t.CostUsd)),
            ("session id", Or(t.SessionId)),
            ("started", Time(t.StartedAt)),
            ("finished", Time(t.FinishedAt)),
            ("merged sha", Or(t.MergedSha)),
            ("detail", Or(t.Detail))));
        AddIfText(sections, "Plan", Lines(
            ("deps", List(t.Deps)),
            ("owns", List(t.Owns)),
            ("acceptance", Or(t.Acceptance)),
            ("model", Or(t.Model)),
            ("wave", Number(t.Wave)),
            ("dependents", Number(t.Dependents))));
        AddIfText(sections, "Prompt", t.Prompt);
        AddIfText(sections, "Summary", t.Summary);
        AddIfText(sections, "Notes", t.Notes);
        AddIfText(sections, "Error", t.Error);
        AddIfText(sections, "Feedback", t.Feedback);
        AddIfText(sections, "Sessions", string.Join('\n', s.Sessions
            .Where(x => x.Files.TaskId == t.Id)
            .SelectMany(x => AgentTree.Rows([x]).Select(PopupChildLine).Prepend(PopupSessionLine(x)))));
        AddIfText(sections, "Processes", string.Join('\n', s.Processes.Processes
            .Where(p => p.TaskId == t.Id)
            .Select(p => string.Join(Separator,
                    "pid " + p.Pid.ToString(CultureInfo.InvariantCulture),
                    p.Role is { } role ? Words.Role(role) : Missing,
                    "started " + Time(p.StartedAt),
                    "cpu " + Cpu(p.CpuShare),
                    "mem " + Look.Bytes(p.WorkingSetBytes))
                + "\n" + Or(p.CommandLine))));
        return sections;
    }

    // 7.5: the sessions in state Running, in snapshot order.
    public static IReadOnlyList<Session> RunningSessions(RunSnapshot s) =>
        [.. s.Sessions.Where(x => x.State == SessionState.Running)];

    // 7.5, 7.6, 17.1: a header line (warning colour when the last event is more than 5 minutes old), the context size
    // of the latest call with usage (no line without one), the process line (27.1; none while no sample was taken), then
    // the last 5 items, one line each. 39.2: the header, context and items are the agent's own; one line per sub-agent
    // follows.
    public static IReadOnlyList<string> RunningBlock(Session session, RunSnapshot snapshot, DateTimeOffset now)
    {
        var files = session.Files;
        var content = session.Content;
        var name = files.TaskId ?? files.Role.ToString();
        var role = files.Role.ToString();
        var rest = string.Join(Separator,
            Attempt(files),
            Or(content.Model),
            ToolCalls(content, null),
            content.LastEventAt is { } last ? Look.Span(now - last) + " ago" : Missing);

        var stale = content.LastEventAt is { } at && now - at > StaleAfter;
        var header = stale
            ? Look.Tag("warning", string.Join(Separator, name, role, rest))
            : string.Join(Separator, Look.Tag("", name), Look.Tag(Look.Color(files.Role), role), Look.Tag("", rest));

        var lines = new List<string> { header };
        if (LatestUsage(content, null) is { } usage)
            lines.Add(ItemIndent + "context " + Look.ContextSize(usage.Context, ContextLimit.For(snapshot, session)));
        if (snapshot.Processes.SampledAt is not null)
            lines.Add(ItemIndent + ProcessLine(session, snapshot.Processes.Processes, now));
        var items = SubAgents.Items(content, null);
        for (var i = Math.Max(0, items.Length - RunningItemCount); i < items.Length; i++)
            lines.Add(ItemIndent + ItemLine(items[i]));
        lines.AddRange(AgentTree.Rows([session]).Select(row => RunningChildLine(row, snapshot)));
        return lines;
    }

    // 7.8, 39.3: "HH:mm:ss [source] <first line>" in the kind's colour; the source is the entry's path (LogPath).
    public static string LogLine(ProgressEntry e)
    {
        var source = LogPath(e) is { } path ? $"[{path}] " : "";
        return Look.Tag(Look.Color(e.Kind), $"{Look.Clock(e.Time)} {source}{FirstLine(e.Message)}");
    }

    // 7.9, 39.3: one section with the whole message, under the time, the entry's path and its kind.
    public static IReadOnlyList<PopupSection> LogPopup(ProgressEntry e)
    {
        var heading = string.Join(Separator,
            new[] { Look.Clock(e.Time), LogPath(e), e.Kind.ToString() }.OfType<string>());
        return [new PopupSection(heading, e.Message)];
    }

    // 39.3: "<Source> › <SubAgent>", or the SubAgent alone without a source; the Source alone without a SubAgent.
    private static string? LogPath(ProgressEntry e) => e.SubAgent switch
    {
        null => e.Source,
        { } sub when e.Source is null => sub,
        { } sub => e.Source + AgentPath.Separator + sub,
    };

    // 39.2: "<Prefix><icon> <Name> · <n tool calls>" with the tree part in the state's colour, then " · context <size>"
    // from its latest call with usage, then, while it runs, " · <its latest item line>".
    private static string RunningChildLine(AgentRow row, RunSnapshot snapshot)
    {
        var (session, sub) = (row.Session, row.SubAgent);
        var content = session.Content;
        var state = SubAgents.StateOf(session, sub);
        var line = Look.Tag(Look.Color(state), $"{row.Prefix}{Look.Icon(state)} {sub.Name}")
            + Separator + ToolCalls(content, sub.Id);
        if (LatestUsage(content, sub.Id) is { } usage)
            line += Separator + "context " + Look.ContextSize(usage.Context, ContextLimit.ForModel(snapshot, sub.Model));
        var items = SubAgents.Items(content, sub.Id);
        if (state == SessionState.Running && items.Length > 0)
            line += Separator + ItemLine(items[^1]);
        return line;
    }

    // 7.4: "<Role> · <attempt> · <State> · <Model|->".
    private static string PopupSessionLine(Session x) =>
        string.Join(Separator, x.Files.Role.ToString(), Attempt(x.Files), x.State.ToString(), Or(x.Content.Model));

    // 39.4: "  <Prefix> <Name> · <AgentType|-> · <State> · <Model|->", plain text like the session line above it.
    private static string PopupChildLine(AgentRow row) =>
        $"{PopupChildIndent}{row.Prefix} " + string.Join(Separator,
            row.SubAgent.Name,
            Or(row.SubAgent.AgentType),
            SubAgents.StateOf(row.Session, row.SubAgent).ToString(),
            Or(row.SubAgent.Model));

    // The usage of the agent's latest call that has one; null gives the agent's own calls (39.2).
    private static TokenUsage? LatestUsage(SessionContent content, string? agentId) =>
        SubAgents.Calls(content, agentId).LastOrDefault(c => c.Usage is not null)?.Usage;

    // 27.1: the figures of the process with the session's task id and role (the latest started of several, nulls last),
    // or "no process" in the warning colour.
    private static string ProcessLine(Session session, ImmutableArray<AgentProcess> processes, DateTimeOffset now)
    {
        var process = processes
            .Where(p => p.TaskId == session.Files.TaskId && p.Role == session.Files.Role)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefault();
        if (process is null)
            return Look.Tag("warning", "no process");
        return Look.Tag("", string.Join(Separator,
            "pid " + process.Pid.ToString(CultureInfo.InvariantCulture),
            "up " + (process.StartedAt is { } started ? Look.Span(now - started) : Missing),
            "cpu " + Cpu(process.CpuShare),
            "mem " + Look.Bytes(process.WorkingSetBytes)));
    }

    private static string Cpu(double? share) => share is { } value ? Look.Percent(value) : Missing;

    private static string ItemLine(ConversationItem item) => item switch
    {
        ToolCall call => Look.Tag("", call.Summary),
        Thinking { Text.Length: 0 } thinking => Look.Tag("muted", thinking.EstimatedTokens is { } n
            ? $"thinking (~{Look.Tokens(n)} tokens, no text)"
            : "thinking (no text)"),
        Thinking thinking => Look.Tag("muted", FirstLine(thinking.Text)),
        AssistantText text => Look.Tag("", FirstLine(text.Text)),
        UserText text => Look.Tag("", FirstLine(text.Text)),
        Notice notice => Look.Tag("", FirstLine(notice.Text)),
        _ => "",
    };

    private static string Columns(IReadOnlyList<string> cells, int idWidth, int titleWidth, int detailWidth) =>
        string.Join(ColumnGap,
            cells[0],
            Fit(cells[1], idWidth),
            Fit(cells[2], WaveWidth),
            Fit(cells[3], titleWidth),
            Fit(cells[4], detailWidth),
            Fit(cells[5], AttemptsWidth, right: true),
            Fit(cells[6], CostWidth, right: true),
            Fit(cells[7], ElapsedWidth, right: true)).TrimEnd();

    // Pads the cell to its width, or cuts it so that it ends with "...".
    private static string Fit(string cell, int width, bool right = false)
    {
        width = Math.Max(0, width);
        if (cell.Length > width)
            return width < 3 ? new string('.', width) : string.Concat(cell.AsSpan(0, width - 3), "...");
        return right ? cell.PadLeft(width) : cell.PadRight(width);
    }

    // Cuts the text as Fit does when it is longer than the width; shorter text keeps its length.
    private static string Cut(string text, int width) => text.Length > width ? Fit(text, width) : text;

    private static string Attempt(SessionFiles files)
    {
        var text = "#" + files.Attempt.ToString(CultureInfo.InvariantCulture);
        if (files.Role == AgentRole.Reviewer)
            text += "." + files.ReviewTry.ToString(CultureInfo.InvariantCulture);
        return files.IsNudge ? text + " nudge" : text;
    }

    // "1 tool call", "2 tool calls": the ToolCalls among the agent's items, so a nested sub-agent's do not count (39.1,
    // 39.2); null counts the agent's own.
    private static string ToolCalls(SessionContent content, string? agentId) =>
        Words.Count(SubAgents.Items(content, agentId).OfType<ToolCall>().Count(), "tool call");

    private static int Longest(IReadOnlyList<TaskView> tasks, Func<TaskView, string> cell, string header) =>
        tasks.Select(t => cell(t).Length).Append(header.Length).Max();

    private static int Count(RunSnapshot s, TaskState state) => s.Tasks.Count(t => t.Status == state);

    private static string Field(string name, string value) => $"{name}: {Look.Tag("", value)}";

    private static string Lines(params (string Name, string Value)[] fields) =>
        string.Join('\n', fields.Select(f => $"{f.Name}: {f.Value}"));

    private static void AddIfText(List<PopupSection> sections, string heading, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            sections.Add(new PopupSection(heading, text));
    }

    private static string Elapsed(DateTimeOffset? started, DateTimeOffset? finished, DateTimeOffset now) =>
        started is { } start ? Look.Span((finished ?? now) - start) : Missing;

    private static string Time(DateTimeOffset? t) => t is { } value ? Look.Clock(value) : Missing;

    private static string Number(int? n) => n is { } value ? value.ToString(CultureInfo.InvariantCulture) : Missing;

    private static string ProviderText(Provider p) => p == Provider.Unknown ? Missing : p.ToString();

    private static string Or(string? text) => string.IsNullOrEmpty(text) ? Missing : text;

    private static string List(IEnumerable<string> items) => Or(string.Join(", ", items));

    private static string FirstLine(string text)
    {
        var end = text.IndexOf('\n');
        return (end < 0 ? text : text[..end]).TrimEnd('\r');
    }
}

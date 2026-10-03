using System.Globalization;
using OrchDash.Contracts;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Overview.Format;

// The text of the Overview page (spec 7.1-7.9): markup lines and pop-up sections built from the model.
// Every piece of model text in a markup line goes through Look.Tag; PopupSection.Text is plain text.
public static class OverviewText
{
    public const string Missing = "-";
    public const int WaveWidth = 4;
    public const int AttemptsWidth = 3;
    public const int CostWidth = 10;
    public const int ElapsedWidth = 7;
    public const int RunningItemCount = 5;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private const string ColumnGap = "  ";
    private const string Separator = " · ";
    private const string ItemIndent = "  ";

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

    // The header of the task table, aligned with TaskRow.
    public static string TaskHeader(int idWidth, int titleWidth, int detailWidth) =>
        Look.Tag("muted", Columns([" ", "Id", "Wave", "Title", "Detail", "Att", "Cost", "Elapsed"],
            idWidth, titleWidth, detailWidth));

    // 7.4: Status, Plan, Prompt, Summary, Notes, Error, Feedback, Sessions; empty sections left out.
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
            .Select(x => string.Join(Separator,
                x.Files.Role.ToString(), Attempt(x.Files), x.State.ToString(), Or(x.Content.Model)))));
        return sections;
    }

    // 7.5: the sessions in state Running, in snapshot order.
    public static IReadOnlyList<Session> RunningSessions(RunSnapshot s) =>
        [.. s.Sessions.Where(x => x.State == SessionState.Running)];

    // 7.5, 7.6: a header line (warning colour when the last event is more than 5 minutes old),
    // then the last 5 items, one line each.
    public static IReadOnlyList<string> RunningBlock(Session session, DateTimeOffset now)
    {
        var files = session.Files;
        var content = session.Content;
        var name = files.TaskId ?? files.Role.ToString();
        var role = files.Role.ToString();
        var rest = string.Join(Separator,
            Attempt(files),
            Or(content.Model),
            ToolCalls(content.Items.OfType<ToolCall>().Count()),
            content.LastEventAt is { } last ? Look.Span(now - last) + " ago" : Missing);

        var stale = content.LastEventAt is { } at && now - at > StaleAfter;
        var header = stale
            ? Look.Tag("warning", string.Join(Separator, name, role, rest))
            : string.Join(Separator, Look.Tag("", name), Look.Tag(Look.Color(files.Role), role), Look.Tag("", rest));

        var lines = new List<string> { header };
        var items = content.Items;
        for (var i = Math.Max(0, items.Length - RunningItemCount); i < items.Length; i++)
            lines.Add(ItemIndent + ItemLine(items[i]));
        return lines;
    }

    // 7.8: "HH:mm:ss [source] <first line>" in the kind's colour.
    public static string LogLine(ProgressEntry e)
    {
        var source = e.Source is null ? "" : $"[{e.Source}] ";
        return Look.Tag(Look.Color(e.Kind), $"{Look.Clock(e.Time)} {source}{FirstLine(e.Message)}");
    }

    // 7.9: one section with the whole message.
    public static IReadOnlyList<PopupSection> LogPopup(ProgressEntry e)
    {
        var heading = string.Join(Separator,
            new[] { Look.Clock(e.Time), e.Source, e.Kind.ToString() }.OfType<string>());
        return [new PopupSection(heading, e.Message)];
    }

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

    private static string Attempt(SessionFiles files)
    {
        var text = "#" + files.Attempt.ToString(CultureInfo.InvariantCulture);
        if (files.Role == AgentRole.Reviewer)
            text += "." + files.ReviewTry.ToString(CultureInfo.InvariantCulture);
        return files.IsNudge ? text + " nudge" : text;
    }

    private static string ToolCalls(int n) => n == 1 ? "1 tool call" : $"{Number(n)} tool calls";

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

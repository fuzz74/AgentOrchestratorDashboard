using System.Collections.Immutable;
using System.Globalization;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Pages.Usage.Format;

// The text of the Usage page (16.1-16.6, 16.8, 43.1-43.4): markup lines built from the usage rules, the session pop-up
// and the sub-agent pop-up. Every piece of model text in a markup line goes through Look.Tag; pop-up titles and texts
// are plain text.
public static class UsageText
{
    public const string Missing = "-";
    public const int MaxGroupNameWidth = 14;   // a group row is then at most 105 columns wide, 117 with the Sub column
    public const int RoleWidth = 9;
    public const int AttemptWidth = 8;   // "#1 nudge"; a reviewer's nudge "#1.1 nudge" is not cut
    public const int ModelWidth = 20;

    // 43.1: the role and attempt columns together while the snapshot has sub-agents, wide enough for a top-level child
    // row ("├✔ " and the longest name); a session row is then 147 columns wide instead of 131.
    public const int AgentWidth = 3 + SubAgents.NameLength;

    private const int SessionsWidth = 4;
    private const int CallsWidth = 5;
    private const int TokensWidth = 7;
    private const int CostWidth = 9;
    private const int PremiumWidth = 5;
    private const int AiuWidth = 9;
    private const int LinesWidth = 11;
    private const int SubWidth = 10;   // "12 · 100 %"
    private const string ColumnGap = "  ";
    private const string Separator = " · ";
    private const string GroupHeading = "Group";
    private const string SubAgentBar = "└ sub-agents";

    // 16.1: one markup line per figure, summed over all sessions; 43.2: then, where the snapshot has sub-agents, their number.
    public static IReadOnlyList<string> RunPanel(RunSnapshot snapshot)
    {
        var f = UsageRules.Total(snapshot);
        var lines = new List<string>
        {
            Field("Sessions", Words.Number(f.Sessions)),
            Field("Calls", Words.Number(f.Calls)),
            Field("Input", Tokens(f.Input)),
            Field("Cache read", Tokens(f.CacheRead)),
            Field("Cache write", Tokens(f.CacheWrite)),
            Field("Output", Tokens(f.Output)),
            Field("Thinking", Tokens(f.Thinking)),
            Field("Cost", Cost(f.CostUsd)),
            Field("Premium requests", Premium(f.PremiumRequests)),
            Field("AIU", Aiu(f.NanoAiu)),
            Field("Lines", Lines(f)),
        };
        if (UsageRules.SubAgentCount(snapshot.Sessions) is var subAgents and > 0)
            lines.Add("sub-agents " + Words.Number(subAgents));
        return lines;
    }

    // 16.2: the rate limit with the latest SeenAt; the status in the success colour while it is "allowed".
    public static string RateLimitLine(RunSnapshot snapshot)
    {
        if (UsageRules.LatestRateLimit(snapshot) is not { } limit)
            return "Rate limits: " + Missing;

        var parts = new List<string>
        {
            Window("5-hour", limit.FiveHourUsed, limit.FiveHourResetsAt is { } fiveHour ? Look.ShortClock(fiveHour) : null),
            Window("7-day", limit.SevenDayUsed, limit.SevenDayResetsAt is { } sevenDay ? Look.DateClock(sevenDay) : null),
        };
        if (!string.IsNullOrEmpty(limit.Status))
            parts.Add(Look.Tag(limit.Status == "allowed" ? "success" : "warning", limit.Status));
        return "Rate limits: " + string.Join(Separator, parts);
    }

    // 16.3: per provider with sessions its versions and the tested version; the warning colour when one differs.
    public static string VersionsLine(RunSnapshot snapshot)
    {
        var parts = new List<string>();
        if (UsageRules.HasSessions(snapshot, Provider.Claude))
            parts.Add(Versions("Claude Code", UsageRules.Versions(snapshot, Provider.Claude), TestedVersions.ClaudeCode));
        if (UsageRules.HasSessions(snapshot, Provider.Copilot))
            parts.Add(Versions("Copilot CLI", UsageRules.Versions(snapshot, Provider.Copilot), TestedVersions.CopilotCli));
        return "Versions: " + (parts.Count == 0 ? Missing : string.Join(Separator, parts));
    }

    // The width of the group name column: the longest name or the heading, at most MaxGroupNameWidth.
    public static int GroupNameWidth(IEnumerable<UsageGroup> groups) =>
        Math.Min(MaxGroupNameWidth, groups.Select(g => g.Name.Length).Append(GroupHeading.Length).Max());

    // 16.4: the header of the group table, aligned with GroupRow; 43.4: with the Sub column while the snapshot has
    // sub-agents.
    public static string GroupHeader(int nameWidth, bool subAgents = false)
    {
        var header = string.Join(ColumnGap,
            Fit(GroupHeading, nameWidth),
            Right("Sess", SessionsWidth),
            Right("Calls", CallsWidth),
            Right("Input", TokensWidth),
            Right("C.read", TokensWidth),
            Right("C.write", TokensWidth),
            Right("Output", TokensWidth),
            Right("Cost", CostWidth),
            Right("Prem", PremiumWidth),
            Right("AIU", AiuWidth),
            Right("Lines", LinesWidth));
        return Look.Tag("muted", subAgents ? header + ColumnGap + Right("Sub", SubWidth) : header);
    }

    // 16.4: name, sessions, calls, input, cache read, cache write, output, cost, premium requests, AIU, lines; 43.4:
    // then, while the snapshot has sub-agents, the group's sub-agents.
    public static string GroupRow(UsageGroup group, int nameWidth, bool subAgents = false)
    {
        var f = group.Figures;
        var row = string.Join(ColumnGap,
            Look.Tag("", Fit(group.Name, nameWidth)),
            Right(Words.Number(f.Sessions), SessionsWidth),
            Right(Words.Number(f.Calls), CallsWidth),
            Right(Tokens(f.Input), TokensWidth),
            Right(Tokens(f.CacheRead), TokensWidth),
            Right(Tokens(f.CacheWrite), TokensWidth),
            Right(Tokens(f.Output), TokensWidth),
            Right(Cost(f.CostUsd), CostWidth),
            Right(Premium(f.PremiumRequests), PremiumWidth),
            Right(Aiu(f.NanoAiu), AiuWidth),
            Right(Lines(f), LinesWidth));
        return subAgents ? row + ColumnGap + Right(SubAgentShare(group), SubWidth) : row;
    }

    // 16.5: per group its name and input + cache read + cache write + output; 43.4: under a group with sub-agents, a
    // bar "└ sub-agents" with their tokens.
    public static ImmutableArray<UsageBar> BarItems(IEnumerable<UsageGroup> groups) => [.. groups.SelectMany(Bars)];

    // 16.6: the header of the session table, aligned with SessionRow; 43.1: while the snapshot has sub-agents, the
    // attempt column widens so that role and attempt hold AgentWidth, aligned with ChildRow.
    public static string SessionHeader(bool subAgents = false) =>
        Look.Tag("muted", string.Join(ColumnGap,
            " ",
            Fit("Role", RoleWidth),
            Fit("Attempt", AttemptWidthOf(subAgents)),
            Fit("Model", ModelWidth),
            Right("Calls", CallsWidth),
            Right("Peak", TokensWidth),
            Right("Input", TokensWidth),
            Right("C.read", TokensWidth),
            Right("C.write", TokensWidth),
            Right("Output", TokensWidth),
            Right("Cost", CostWidth),
            Right("AIU", AiuWidth),
            Right("Lines", LinesWidth)));

    // 16.6: state icon, role, attempt, model, calls, peak context, input, cache read, cache write, output, cost, AIU, lines.
    public static string SessionRow(RunSnapshot snapshot, Session session)
    {
        var files = session.Files;
        var f = UsageRules.Of(snapshot, session);
        return string.Join(ColumnGap,
            Look.Tag(Look.Color(session.State), Look.Icon(session.State)),
            Look.Tag(Look.Color(files.Role), Fit(Words.Role(files.Role), RoleWidth)),
            Words.Attempt(files).PadRight(AttemptWidthOf(UsageRules.HasSubAgents(snapshot))),
            Look.Tag("", Fit(Or(session.Content.Model), ModelWidth)),
            Right(Words.Number(f.Calls), CallsWidth),
            Right(Tokens(f.PeakContext), TokensWidth),
            Right(Tokens(f.Input), TokensWidth),
            Right(Tokens(f.CacheRead), TokensWidth),
            Right(Tokens(f.CacheWrite), TokensWidth),
            Right(Tokens(f.Output), TokensWidth),
            Right(Cost(f.CostUsd), CostWidth),
            Right(Aiu(f.NanoAiu), AiuWidth),
            Right(Lines(f), LinesWidth));
    }

    // 43.1: a child row under its session: an empty icon column, "<Prefix><icon> <Name>" in the state's colour across
    // the role and attempt columns, its model, the figures of its calls, and "-" for cost and lines.
    public static string ChildRow(AgentRow row)
    {
        var sub = row.SubAgent;
        var state = SubAgents.StateOf(row.Session, sub);
        var f = UsageRules.OfSubAgent(row.Session, sub.Id);
        return string.Join(ColumnGap,
            " ",
            Look.Tag(Look.Color(state), Fit($"{row.Prefix}{Look.Icon(state)} {sub.Name}", AgentWidth)),
            Look.Tag("", Fit(Or(sub.Model), ModelWidth)),
            Right(Words.Number(f.Calls), CallsWidth),
            Right(Tokens(f.PeakContext), TokensWidth),
            Right(Tokens(f.Input), TokensWidth),
            Right(Tokens(f.CacheRead), TokensWidth),
            Right(Tokens(f.CacheWrite), TokensWidth),
            Right(Tokens(f.Output), TokensWidth),
            Right(Missing, CostWidth),
            Right(Aiu(f.NanoAiu), AiuWidth),
            Right(Missing, LinesWidth));
    }

    // 16.8: "Usage: <group> <role> #<attempt>".
    public static string SessionPopupTitle(Session session) =>
        $"Usage: {UsageRules.GroupOf(session)} {Words.Role(session.Files.Role)} {Words.Attempt(session.Files)}";

    // 16.8: Totals, Calls and, while Unavailable is not empty, Unavailable. 43.3: Calls lists the agent's own calls, and
    // a section "Sub-agent <Name>" per sub-agent, in the order of its child rows, lists that sub-agent's calls.
    public static IReadOnlyList<PopupSection> SessionPopup(RunSnapshot snapshot, Session session)
    {
        var f = UsageRules.Of(snapshot, session);
        var totals = string.Join('\n', CallTotals(f, ContextLimit.For(snapshot, session)).Concat(
        [
            $"cost: {Cost(f.CostUsd)}",
            $"premium requests: {Premium(f.PremiumRequests)}",
            $"AIU: {Aiu(f.NanoAiu)}",
            $"lines: {Lines(f)}",
        ]));

        var content = session.Content;
        var sections = new List<PopupSection>
        {
            new("Totals", totals),
            new("Calls", CallLines(SubAgents.Calls(content, null))),
        };
        foreach (var sub in SubAgents.Tree(content))
            sections.Add(new PopupSection("Sub-agent " + sub.Name, CallLines(SubAgents.Calls(content, sub.Id))));
        if (!session.Unavailable.IsDefaultOrEmpty)
            sections.Add(new PopupSection("Unavailable", string.Join('\n', session.Unavailable)));
        return sections;
    }

    // 43.3: "Usage: <path>", for example "Usage: alpha worker #1 › Survey the parser module".
    public static string SubAgentPopupTitle(Session session, string agentId) => "Usage: " + AgentPath.Of(session, agentId);

    // 43.3: Totals and Calls of one sub-agent; its peak context against the context window of its model.
    public static IReadOnlyList<PopupSection> SubAgentPopup(RunSnapshot snapshot, Session session, string agentId)
    {
        var f = UsageRules.OfSubAgent(session, agentId);
        var limit = ContextLimit.ForModel(snapshot, SubAgents.Find(session.Content, agentId)?.Model);
        return
        [
            new PopupSection("Totals", string.Join('\n', CallTotals(f, limit).Append($"AIU: {Aiu(f.NanoAiu)}"))),
            new PopupSection("Calls", CallLines(SubAgents.Calls(session.Content, agentId))),
        ];
    }

    // The lines that the totals of a session and of a sub-agent share: calls, tokens, thinking and peak context.
    private static IEnumerable<string> CallTotals(UsageFigures f, long? limit) =>
    [
        $"calls: {Words.Number(f.Calls)}",
        $"input: {Tokens(f.Input)}",
        $"cache read: {Tokens(f.CacheRead)}",
        $"cache write: {Tokens(f.CacheWrite)}",
        $"output: {Tokens(f.Output)}",
        $"thinking: {Tokens(f.Thinking)}",
        $"peak context: {(f.PeakContext is { } peak ? Look.ContextSize(peak, limit) : Missing)}",
    ];

    // One CallLine per call, numbered within the calls given.
    private static string CallLines(ImmutableArray<ModelCall> calls) =>
        calls.IsEmpty ? "no model calls" : string.Join('\n', calls.Select((call, i) => CallLine(call, i)));

    // "call 1 · 12:06:30 · gpt-5.1 · input 20 · cache read 0 · cache write 14.2k · output 310 · thinking 96 · 1.85 AIU ·
    // duration 4s · stop tool_calls", with "-" for an unknown figure ("AIU -" for an unknown AIU)
    private static string CallLine(ModelCall call, int index)
    {
        var usage = call.Usage;
        return string.Join(Separator,
            "call " + Words.Number(index + 1),
            call.StartedAt is { } start ? Look.Clock(start) : Missing,
            Or(call.Model),
            "input " + Tokens(usage?.Input),
            "cache read " + Tokens(usage?.CacheRead),
            "cache write " + Tokens(usage?.CacheWrite),
            "output " + Tokens(usage?.Output),
            "thinking " + Tokens(call.ThinkingTokens),
            call.NanoAiu is { } nanoAiu ? Look.Aiu(nanoAiu) : "AIU " + Missing,
            "duration " + (call.Duration is { } duration ? Look.Span(duration) : Missing),
            "stop " + Or(call.StopReason));
    }

    private static IEnumerable<UsageBar> Bars(UsageGroup group)
    {
        yield return new UsageBar(group.Name, group.Figures.Tokens);
        if (group.SubAgentCount > 0)
            yield return new UsageBar(SubAgentBar, group.SubAgentTokens);
    }

    // 43.4: "<n> · <share>", the share being the sub-agents' tokens over the group's; "-" for a group without sub-agents.
    private static string SubAgentShare(UsageGroup group)
    {
        if (group.SubAgentCount == 0)
            return Missing;
        var tokens = group.Figures.Tokens;
        var share = tokens > 0 ? Look.Percent((double)group.SubAgentTokens / tokens) : Missing;
        return Words.Number(group.SubAgentCount) + Separator + share;
    }

    // 43.1: with sub-agents the attempt column takes what AgentWidth leaves of the role column.
    private static int AttemptWidthOf(bool subAgents) =>
        subAgents ? AgentWidth - RoleWidth - ColumnGap.Length : AttemptWidth;

    private static string Window(string name, double? used, string? resetsAt)
    {
        var text = name + " " + (used is { } fraction ? Look.Percent(fraction) : Missing);
        return resetsAt is null ? text : text + ", resets " + resetsAt;
    }

    private static string Versions(string cli, ImmutableArray<string> versions, string tested)
    {
        var text = $"{cli} {(versions.IsEmpty ? "unknown" : string.Join(", ", versions))} (made for {tested})";
        return Look.Tag(versions.Any(v => v != tested) ? "warning" : "", text);
    }

    private static string Field(string name, string value) => $"{name}: {value}";

    private static string Tokens(long? n) => n is { } value ? Look.Tokens(value) : Missing;

    private static string Cost(double? usd) => usd is { } value ? Look.Usd(value) : Missing;

    private static string Aiu(long? nanoAiu) => nanoAiu is { } value ? Look.Aiu(value) : Missing;

    // Invariant, at most 2 decimals.
    private static string Premium(double? requests) =>
        requests is { } value ? value.ToString("0.##", CultureInfo.InvariantCulture) : Missing;

    private static string Lines(UsageFigures f) =>
        f is { LinesAdded: { } added, LinesRemoved: { } removed }
            ? $"+{Words.Number(added)} -{Words.Number(removed)}"
            : Missing;

    private static string Or(string? text) => string.IsNullOrEmpty(text) ? Missing : text;

    private static string Right(string cell, int width) => cell.PadLeft(width);

    // Pads a text cell to its width, or cuts it so that it ends with "...".
    private static string Fit(string cell, int width)
    {
        width = Math.Max(0, width);
        if (cell.Length > width)
            return width < 3 ? new string('.', width) : string.Concat(cell.AsSpan(0, width - 3), "...");
        return cell.PadRight(width);
    }
}

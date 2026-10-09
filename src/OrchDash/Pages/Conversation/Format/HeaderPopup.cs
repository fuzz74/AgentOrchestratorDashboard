using System.Collections.Immutable;
using System.Globalization;
using OrchDash.Contracts;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Conversation.Format;

// The header's pop-up (8.6): every SessionFiles, SessionInit and SessionResult value as "name: value"; a sub-agent's
// fields for the sub-agent header (41.4).
internal static class HeaderPopup
{
    private const string None = "none";

    public static ImmutableArray<PopupSection> Sections(Session session)
    {
        var sections = ImmutableArray.CreateBuilder<PopupSection>(3);
        sections.Add(new PopupSection("Files", Files(session.Files)));
        if (session.Content.Init is { } init)
            sections.Add(new PopupSection("Init", Init(init)));
        if (session.Content.Result is { } result)
            sections.Add(new PopupSection("Result", Result(result)));
        return sections.ToImmutable();
    }

    // The sub-agent header's pop-up (41.4). Parent is the parent's Name, or "agent" when the agent's own call started it.
    public static ImmutableArray<PopupSection> SubAgentSections(Session session, SubAgent sub) =>
    [
        new PopupSection("Sub-agent", Join(
            ("Id", sub.Id),
            ("ToolCallId", sub.ToolCallId.Length > 0 ? sub.ToolCallId : null),
            ("Parent", SubAgents.Find(session.Content, sub.ParentId)?.Name ?? "agent"),
            ("AgentType", sub.AgentType),
            ("Model", sub.Model),
            ("Background", Bool(sub.Background)),
            ("Description", sub.Description),
            ("StartedAt", sub.StartedAt is { } started ? Look.Clock(started) : null),
            ("FinishedAt", sub.FinishedAt is { } finished ? Look.Clock(finished) : null))),
    ];

    private static string Files(SessionFiles f) => Join(
        ("Key", f.Key),
        ("TaskId", f.TaskId),
        ("Role", Words.Role(f.Role)),
        ("StartFolder", f.StartFolder),
        ("Attempt", Words.Number(f.Attempt)),
        ("ReviewTry", Words.Number(f.ReviewTry)),
        ("IsNudge", Bool(f.IsNudge)),
        ("ResultPath", f.ResultPath),
        ("PromptPath", f.PromptPath),
        ("EventsPath", f.EventsPath),
        ("StderrPath", f.StderrPath),
        ("HasResultFile", Bool(f.HasResultFile)),
        ("HasEventsFile", Bool(f.HasEventsFile)),
        ("PromptWrittenAt", f.PromptWrittenAt is { } t ? Look.Clock(t) : null));

    private static string Init(SessionInit i) => Join(
        ("Cwd", i.Cwd),
        ("PermissionMode", i.PermissionMode),
        ("CliVersion", i.CliVersion),
        ("Tools", List(i.Tools)),
        ("McpServers", List(i.McpServers)));

    // Text, StructuredJson, Worker and Review are shown in full in the result entry's pop-up; here they are summarised.
    private static string Result(SessionResult r) => Join(
        ("IsError", Bool(r.IsError)),
        ("Subtype", r.Subtype),
        ("Text", r.Text is null ? null : Words.Count(r.Text.Length, "char")),
        ("StructuredJson", r.StructuredJson is null ? null : Words.Count(r.StructuredJson.Length, "char")),
        ("Worker", r.Worker is { } w ? "status " + w.Status : null),
        ("Review", r.Review is { } v
            ? $"spec {v.SpecVerdict}, quality {v.QualityVerdict}, {Words.Count(v.Issues.Length, "issue")}"
            : null),
        ("CostUsd", r.CostUsd is { } cost ? Look.Usd(cost) : null),
        ("Turns", r.Turns is { } turns ? Words.Number(turns) : null),
        ("Duration", r.Duration is { } d ? Look.Span(d) : null),
        ("ApiDuration", r.ApiDuration is { } api ? Look.Span(api) : null),
        ("Usage", r.Usage is { } usage ? Usage(usage) : null),
        ("ContextWindow", r.ContextWindow is { } window ? Look.Tokens(window) : null),
        ("PremiumRequests", r.PremiumRequests?.ToString(CultureInfo.InvariantCulture)),
        ("LinesAdded", r.LinesAdded is { } added ? Words.Number(added) : null),
        ("LinesRemoved", r.LinesRemoved is { } removed ? Words.Number(removed) : null));

    private static string Usage(TokenUsage u)
    {
        var text = $"input {Look.Tokens(u.Input)}, cache read {Look.Tokens(u.CacheRead)}, " +
                   $"cache write {Look.Tokens(u.CacheWrite)}";
        return u.Output is { } output ? text + ", output " + Look.Tokens(output) : text;
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string? List(ImmutableArray<string> values) => values.IsEmpty ? null : string.Join(", ", values);

    private static string Join(params (string Name, string? Value)[] lines) =>
        string.Join('\n', lines.Select(line => $"{line.Name}: {line.Value ?? None}"));
}

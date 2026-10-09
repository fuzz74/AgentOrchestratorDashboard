using System.Collections.Immutable;
using System.Globalization;
using OrchDash.Core.Model;

namespace OrchDash.Core.Timeline;

/// <summary>
/// Builds the merged timeline of a snapshot by the event table (spec 4.3, 29.1-29.3, 38.2) and steps through it
/// (29.4). Pure; no I/O; never throws (a default array counts as empty).
/// </summary>
public static class TimelineBuilder
{
    private const string RunGroup = "run";
    private const string BootstrapGroup = "bootstrap";
    private const string PlannerGroup = "planner";

    /// <summary>
    /// Returns one event per orchestrator entry (not Activity), kept session, call, tool call, assistant text,
    /// sub-agent start and finish and result, ordered by time; equal times keep the generation order: the
    /// orchestrator entries in progress order, then the sessions in snapshot order, each with its prompt, calls,
    /// items, sub-agent events (per sub-agent its prompt, then its result) and result (29.1, 38.2).
    /// </summary>
    public static ImmutableArray<TimelineEvent> Build(RunSnapshot snapshot)
    {
        if (snapshot is null)
            return [];

        var events = new List<TimelineEvent>();
        AddOrchestrator(events, snapshot.Progress);

        if (!snapshot.Sessions.IsDefault)
        {
            var prefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var session in snapshot.Sessions)
            {
                if (session?.StartedAt is { } start)
                    AddSession(events, session, start, UniquePrefix(prefixes, session.Files.Key), prefixes);
            }
        }

        // OrderBy is a stable sort, so ties keep the generation order of the tie rules.
        return [.. events.OrderBy(e => e.Time)];
    }

    /// <summary>The latest event time strictly earlier than <paramref name="time"/>, or null (29.4).</summary>
    public static DateTimeOffset? Before(ImmutableArray<TimelineEvent> events, DateTimeOffset time)
    {
        if (events.IsDefault)
            return null;

        DateTimeOffset? best = null;
        foreach (var e in events)
        {
            if (e is not null && e.Time < time && (best is null || e.Time > best.Value))
                best = e.Time;
        }
        return best;
    }

    /// <summary>The earliest event time strictly later than <paramref name="time"/>, or null (29.4).</summary>
    public static DateTimeOffset? After(ImmutableArray<TimelineEvent> events, DateTimeOffset time)
    {
        if (events.IsDefault)
            return null;

        DateTimeOffset? best = null;
        foreach (var e in events)
        {
            if (e is not null && e.Time > time && (best is null || e.Time < best.Value))
                best = e.Time;
        }
        return best;
    }

    private static void AddOrchestrator(List<TimelineEvent> events, ImmutableArray<ProgressEntry> progress)
    {
        if (progress.IsDefault)
            return;

        for (var i = 0; i < progress.Length; i++)
        {
            var entry = progress[i];
            if (entry is null || entry.Kind == ProgressKind.Activity)
                continue;
            events.Add(new TimelineEvent("progress:" + Number(i), entry.Time, TimelineKind.Orchestrator,
                entry.Source ?? RunGroup, i, entry, null, null, null, null));
        }
    }

    private static void AddSession(List<TimelineEvent> events, Session session, DateTimeOffset start, string prefix,
        HashSet<string> prefixes)
    {
        var group = GroupOf(session.Files);
        var content = session.Content;

        events.Add(new TimelineEvent(prefix + ":prompt", start, TimelineKind.Prompt, group, 0,
            null, session, null, null, null));

        if (!content.Calls.IsDefault)
        {
            for (var i = 0; i < content.Calls.Length; i++)
            {
                var call = content.Calls[i];
                events.Add(new TimelineEvent(prefix + ":call:" + Number(i), call.StartedAt ?? start, TimelineKind.Call,
                    group, i, null, session, call, null, null) { AgentId = call.AgentId });
            }
        }

        var times = SessionTimes.ItemTimes(content.Items, start);
        for (var i = 0; i < times.Length; i++)
        {
            var item = content.Items[i];
            TimelineKind? kind = item switch
            {
                ToolCall => TimelineKind.Tool,
                AssistantText => TimelineKind.Text,
                _ => null,   // Thinking, UserText and Notice make no event (29.3)
            };
            if (kind is { } k)
            {
                events.Add(new TimelineEvent(prefix + ":item:" + Number(i), times[i], k, group, i,
                    null, session, null, item, null) { AgentId = item.AgentId });
            }
        }

        // 38.2: a sub-agent starts at its StartedAt, else the session start, and finishes at its FinishedAt.
        if (!content.SubAgents.IsDefault)
        {
            foreach (var subAgent in content.SubAgents)
            {
                var subPrefix = UniquePrefix(prefixes, prefix + ":sub:" + subAgent.Id);
                events.Add(new TimelineEvent(subPrefix + ":prompt", subAgent.StartedAt ?? start, TimelineKind.Prompt,
                    group, 0, null, session, null, null, null) { AgentId = subAgent.Id });
                if (subAgent.FinishedAt is { } finished)
                {
                    events.Add(new TimelineEvent(subPrefix + ":result", finished, TimelineKind.Result,
                        group, 0, null, session, null, null, null) { AgentId = subAgent.Id });
                }
            }
        }

        if (content.Result is { } result)
        {
            events.Add(new TimelineEvent(prefix + ":result", content.LastEventAt ?? start, TimelineKind.Result, group, 0,
                null, session, null, null, result));
        }
    }

    // The task id; else "bootstrap" or "planner" by the role; any other role without a task id (which the run folder
    // reader does not produce) uses the lower-case role name.
    private static string GroupOf(SessionFiles files) =>
        files.TaskId ?? files.Role switch
        {
            AgentRole.Bootstrap => BootstrapGroup,
            AgentRole.Planner => PlannerGroup,
            var role => role.ToString().ToLowerInvariant(),
        };

    // Files.Key is unique within a snapshot; should two sessions share one, the later gets "<key>#<n>" so that keys stay
    // unique (29.2). A sub-agent's prefix is "<session prefix>:sub:<id>", taken from the same set, so a sub-agent id
    // that repeats within a session gets "#<n>" too. Distinct prefixes give distinct keys: every session key is a
    // session or sub-agent prefix followed by ":prompt", ":result", ":call:<digits>" or ":item:<digits>", and
    // "progress:<digits>" ends in neither form.
    private static string UniquePrefix(HashSet<string> used, string key)
    {
        var prefix = key;
        for (var n = 2; !used.Add(prefix); n++)
            prefix = key + "#" + Number(n);
        return prefix;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

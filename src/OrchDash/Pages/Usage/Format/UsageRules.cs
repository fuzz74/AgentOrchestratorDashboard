using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Pages.Usage.Format;

// The usage rules and the version rules of the spec (section 4.3) as functions of one snapshot.
public static class UsageRules
{
    public const string Bootstrap = "bootstrap";
    public const string Planner = "planner";

    // One session's figures by the usage rules table. They count its sub-agents' calls too, except the peak context,
    // which comes from the agent's own calls (43.2).
    public static UsageFigures Of(RunSnapshot snapshot, Session session)
    {
        var content = session.Content;
        var calls = content.Calls;
        var result = content.Result;
        var withUsage = calls.Where(c => c.Usage is not null).Select(c => c.Usage!).ToList();

        long? input = null, cacheRead = null, cacheWrite = null;
        if (withUsage.Count > 0)
        {
            input = withUsage.Sum(u => u.Input);
            cacheRead = withUsage.Sum(u => u.CacheRead);
            cacheWrite = withUsage.Sum(u => u.CacheWrite);
        }
        else if (result?.Usage is { } total)
        {
            (input, cacheRead, cacheWrite) = (total.Input, total.CacheRead, total.CacheWrite);
        }

        var output = withUsage.Count > 0 && withUsage.All(u => u.Output is not null)
            ? withUsage.Sum(u => u.Output!.Value)
            : result?.Usage?.Output;

        var thinking = SumKnown(calls.Select(c => c.ThinkingTokens));
        var peak = Peak(SubAgents.Calls(content, null));

        // A transcript covers every attempt of its session, so its cost and lines count only for a session of its own.
        var ownStores = !SharesSessionId(snapshot, session);
        var cost = result?.CostUsd ?? (ownStores ? session.Stores.CostUsd : null);

        int? added = null, removed = null;
        if (result is { LinesAdded: { } resultAdded, LinesRemoved: { } resultRemoved })
            (added, removed) = (resultAdded, resultRemoved);
        else if (ownStores && session.Stores is { LinesAdded: { } storeAdded, LinesRemoved: { } storeRemoved })
            (added, removed) = (storeAdded, storeRemoved);

        var premium = result?.PremiumRequests ?? content.Checkpoint?.PremiumRequests;
        var nanoAiu = SumKnown(calls.Select(c => c.NanoAiu)) ?? content.Checkpoint?.NanoAiu;

        return new UsageFigures(1, calls.Length, input, cacheRead, cacheWrite, output, thinking, peak,
            cost, premium, nanoAiu, added, removed);
    }

    // 43.1: a sub-agent's figures from its calls (SubAgents.Calls): the tokens and the peak context of those with
    // usage, thinking and AIU where known. It is no session and has no result, so no cost, premium requests or lines.
    public static UsageFigures OfSubAgent(Session session, string agentId)
    {
        var calls = SubAgents.Calls(session.Content, agentId);
        var withUsage = calls.Where(c => c.Usage is not null).Select(c => c.Usage!).ToList();
        var known = withUsage.Count > 0;

        return new UsageFigures(0, calls.Length,
            known ? withUsage.Sum(u => u.Input) : null,
            known ? withUsage.Sum(u => u.CacheRead) : null,
            known ? withUsage.Sum(u => u.CacheWrite) : null,
            known && withUsage.All(u => u.Output is not null) ? withUsage.Sum(u => u.Output!.Value) : null,
            SumKnown(calls.Select(c => c.ThinkingTokens)),
            Peak(calls),
            null, null, SumKnown(calls.Select(c => c.NanoAiu)), null, null);
    }

    // The sub-agents of the sessions, nested ones included (43.2, 43.4).
    public static int SubAgentCount(IEnumerable<Session> sessions) =>
        sessions.Sum(session => SubAgents.Tree(session.Content).Length);

    // Whether a session of the snapshot has sub-agents; only then does the page show their columns, bars and lines (N.1).
    public static bool HasSubAgents(RunSnapshot snapshot) =>
        snapshot.Sessions.Any(session => !session.Content.SubAgents.IsDefaultOrEmpty);

    // The session's TaskId, else "bootstrap" or "planner" by its role.
    public static string GroupOf(Session session) => session.Files.TaskId ?? Words.Role(session.Files.Role);

    // Only groups with sessions: bootstrap, planner, the tasks in snapshot order, then the groups of task ids that are
    // not among the snapshot's tasks in order of first appearance. Sessions keep the snapshot order. Each group also
    // counts its sub-agents and their tokens (43.4).
    public static ImmutableArray<UsageGroup> Groups(RunSnapshot snapshot)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal) { [Bootstrap] = 0, [Planner] = 1 };
        foreach (var task in snapshot.Tasks)
            rank.TryAdd(task.Id, rank.Count);

        var byName = new Dictionary<string, List<Session>>(StringComparer.Ordinal);
        var firstSeen = new List<string>();
        foreach (var session in snapshot.Sessions)
        {
            var name = GroupOf(session);
            if (!byName.TryGetValue(name, out var sessions))
            {
                byName[name] = sessions = [];
                firstSeen.Add(name);
            }
            sessions.Add(session);
        }

        return
        [
            .. firstSeen
                .Select((name, seen) => (Name: name, Order: rank.TryGetValue(name, out var r) ? r : rank.Count + seen))
                .OrderBy(g => g.Order)
                .Select(g => Group(snapshot, g.Name, byName[g.Name])),
        ];
    }

    private static UsageGroup Group(RunSnapshot snapshot, string name, List<Session> sessions) =>
        new(name, [.. sessions], Sum(sessions.Select(session => Of(snapshot, session))))
        {
            SubAgentCount = SubAgentCount(sessions),
            SubAgentTokens = sessions.Sum(session =>
                SubAgents.Tree(session.Content).Sum(sub => OfSubAgent(session, sub.Id).Tokens)),
        };

    // The figures summed over all sessions of the snapshot.
    public static UsageFigures Total(RunSnapshot snapshot) => Sum(snapshot.Sessions.Select(session => Of(snapshot, session)));

    // Per figure the sum over those that know it (peak context: the largest); null when none does.
    public static UsageFigures Sum(IEnumerable<UsageFigures> figures)
    {
        var all = figures.ToList();
        return new UsageFigures(
            all.Sum(f => f.Sessions),
            all.Sum(f => f.Calls),
            SumKnown(all.Select(f => f.Input)),
            SumKnown(all.Select(f => f.CacheRead)),
            SumKnown(all.Select(f => f.CacheWrite)),
            SumKnown(all.Select(f => f.Output)),
            SumKnown(all.Select(f => f.Thinking)),
            all.Max(f => f.PeakContext),
            SumKnown(all.Select(f => f.CostUsd)),
            SumKnown(all.Select(f => f.PremiumRequests)),
            SumKnown(all.Select(f => f.NanoAiu)),
            SumKnown(all.Select(f => f.LinesAdded)),
            SumKnown(all.Select(f => f.LinesRemoved)));
    }

    // The RateLimit with the latest SeenAt among all sessions; a null SeenAt counts as earliest, the first wins a tie.
    public static RateLimit? LatestRateLimit(RunSnapshot snapshot)
    {
        RateLimit? latest = null;
        foreach (var session in snapshot.Sessions)
        {
            if (session.Content.RateLimit is not { } limit)
                continue;
            if (latest is null || (limit.SeenAt ?? DateTimeOffset.MinValue) > (latest.SeenAt ?? DateTimeOffset.MinValue))
                latest = limit;
        }
        return latest;
    }

    public static bool HasSessions(RunSnapshot snapshot, Provider provider) =>
        snapshot.Sessions.Any(s => s.Provider == provider);

    // The distinct non-null CLI versions of the provider's sessions, in session order, by the version rules:
    // Content.Init?.CliVersion ?? Stores.CliVersion for Claude, Stores.CliVersion for Copilot.
    public static ImmutableArray<string> Versions(RunSnapshot snapshot, Provider provider) =>
    [
        .. snapshot.Sessions
            .Where(s => s.Provider == provider)
            .Select(s => provider == Provider.Claude ? s.Content.Init?.CliVersion ?? s.Stores.CliVersion : s.Stores.CliVersion)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal),
    ];

    // The largest context of the calls with usage; null when none has usage.
    private static long? Peak(IEnumerable<ModelCall> calls) =>
        calls.Where(c => c.Usage is not null).Max(c => (long?)c.Usage!.Context);

    private static bool SharesSessionId(RunSnapshot snapshot, Session session) =>
        session.Content.SessionId is { } id && snapshot.Sessions.Any(other =>
            other.Files.Key != session.Files.Key && string.Equals(other.Content.SessionId, id, StringComparison.Ordinal));

    private static long? SumKnown(IEnumerable<long?> values)
    {
        long? sum = null;
        foreach (var value in values)
        {
            if (value is { } v)
                sum = (sum ?? 0) + v;
        }
        return sum;
    }

    private static int? SumKnown(IEnumerable<int?> values) => (int?)SumKnown(values.Select(v => (long?)v));

    private static double? SumKnown(IEnumerable<double?> values)
    {
        double? sum = null;
        foreach (var value in values)
        {
            if (value is { } v)
                sum = (sum ?? 0) + v;
        }
        return sum;
    }
}

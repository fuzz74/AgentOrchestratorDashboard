using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Replay;

/// <summary>The sessions at a replay time by the session cut table and the sub-agent cut (spec 30.3, 30.6, 38.1).</summary>
internal static class SessionCut
{
    /// <summary>
    /// The sessions started up to <paramref name="at"/>, in snapshot order, each cut at that time; a session that
    /// nothing is cut from is the live instance, and the live array is returned while every session is.
    /// </summary>
    public static ImmutableArray<Session> Cut(ImmutableArray<Session> sessions, DateTimeOffset at, RunPhase phase)
    {
        if (sessions.IsDefault)
            return [];

        var kept = sessions.Where(session => session.StartedAt <= at).Select(session => CutContent(session, at)).ToList();

        // For the Running rule: the latest start per task, and over all kept sessions for bootstrap and planner.
        DateTimeOffset? latestOfAll = null;
        var latestByTask = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var session in kept)
        {
            var started = session.StartedAt!.Value;
            if (latestOfAll is null || started > latestOfAll)
                latestOfAll = started;
            if (session.Files.TaskId is { } taskId && (!latestByTask.TryGetValue(taskId, out var latest) || started > latest))
                latestByTask[taskId] = started;
        }

        var cut = ImmutableArray.CreateBuilder<Session>(kept.Count);
        foreach (var session in kept)
        {
            var latest = session.Files.TaskId is not { } taskId ? latestOfAll
                : latestByTask.TryGetValue(taskId, out var ofTask) ? ofTask
                : null;
            var state = StateOf(session.Files, session.Content, phase, laterSessionExists: latest > session.StartedAt);
            cut.Add(state == session.State ? session : session with { State = state });
        }

        return cut.Count == sessions.Length && cut.Zip(sessions).All(pair => ReferenceEquals(pair.First, pair.Second))
            ? sessions
            : cut.MoveToImmutable();
    }

    // Every row of the cut table but State; the live instance while nothing is cut.
    private static Session CutContent(Session session, DateTimeOffset at)
    {
        var start = session.StartedAt!.Value;
        var content = session.Content;
        var lastEvent = content.LastEventAt;
        DateTimeOffset? latestKept = null;

        var items = OrEmpty(content.Items);
        var itemTimes = SessionTimes.ItemTimes(items, start);
        var keptItems = ImmutableArray.CreateBuilder<ConversationItem>(items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var itemTime = itemTimes[i];
            if (itemTime > at)
                continue;

            Later(ref latestKept, itemTime);
            var item = items[i];
            if (item is ToolCall { Result: { } result } call)
            {
                var resultTime = result.Time ?? itemTime;
                if (resultTime > at)
                    item = call with { Result = null };
                else
                    Later(ref latestKept, resultTime);
            }
            keptItems.Add(item);
        }
        var itemsCut = keptItems.Count < items.Length || keptItems.Zip(items).Any(pair => !ReferenceEquals(pair.First, pair.Second));

        var calls = OrEmpty(content.Calls);
        var keptCalls = calls.Where(call => (call.StartedAt ?? start) <= at).ToImmutableArray();
        foreach (var call in keptCalls)
            Later(ref latestKept, call.StartedAt ?? start);
        var callsCut = keptCalls.Length < calls.Length;

        // 38.1: the sub-agents started up to the time (an unknown start counts as the session's); one that finishes
        // later runs again.
        var subAgents = OrEmpty(content.SubAgents);
        var keptSubAgents = ImmutableArray.CreateBuilder<SubAgent>(subAgents.Length);
        foreach (var subAgent in subAgents)
        {
            if ((subAgent.StartedAt ?? start) > at)
                continue;
            keptSubAgents.Add(subAgent.FinishedAt > at
                ? subAgent with { State = SessionState.Running, FinishedAt = null, Report = null }
                : subAgent);
        }
        var subAgentsCut = keptSubAgents.Count < subAgents.Length
            || keptSubAgents.Zip(subAgents).Any(pair => !ReferenceEquals(pair.First, pair.Second));

        // A last event after the time cuts the result, the checkpoint, the result file and the last event itself.
        var endCut = lastEvent > at;
        var rateLimitCut = content.RateLimit?.SeenAt > at;

        var stores = CutStores(session.Stores, at);
        var storesCut = !ReferenceEquals(stores, session.Stores);

        if (!itemsCut && !callsCut && !subAgentsCut && !endCut && !rateLimitCut && !storesCut)
            return session;

        var cut = session;
        if (itemsCut || callsCut || subAgentsCut || endCut || rateLimitCut)
        {
            cut = cut with
            {
                Content = content with
                {
                    Items = itemsCut ? keptItems.ToImmutable() : content.Items,
                    Calls = callsCut ? keptCalls : content.Calls,
                    SubAgents = subAgentsCut ? keptSubAgents.ToImmutable() : content.SubAgents,
                    Result = endCut ? null : content.Result,
                    Checkpoint = endCut ? null : content.Checkpoint,
                    LastEventAt = endCut ? latestKept : lastEvent,
                    RateLimit = rateLimitCut ? null : content.RateLimit,
                },
            };
        }
        if (endCut && session.Files.HasResultFile)
            cut = cut with { Files = session.Files with { HasResultFile = false } };
        if (storesCut)
            cut = cut with { Stores = stores };
        return cut;
    }

    // The store calls and injected items up to the time or without one, here and in each sub-agent's store data
    // (38.1); the same instance while nothing is cut, and the same SubAgents dictionary while no value of it is.
    private static StoreData CutStores(StoreData stores, DateTimeOffset at)
    {
        var calls = OrEmpty(stores.Calls);
        var keptCalls = calls.Where(entry => !(entry.Time > at)).ToImmutableArray();
        var injected = OrEmpty(stores.Injected);
        var keptInjected = injected.Where(entry => !(entry.Time > at)).ToImmutableArray();

        var subAgents = stores.SubAgents;
        foreach (var (id, data) in stores.SubAgents)
        {
            var keptData = CutStores(data, at);
            if (!ReferenceEquals(keptData, data))
                subAgents = subAgents.SetItem(id, keptData);
        }

        var callsCut = keptCalls.Length < calls.Length;
        var injectedCut = keptInjected.Length < injected.Length;
        if (!callsCut && !injectedCut && ReferenceEquals(subAgents, stores.SubAgents))
            return stores;

        return stores with
        {
            Calls = callsCut ? keptCalls : stores.Calls,
            Injected = injectedCut ? keptInjected : stores.Injected,
            SubAgents = subAgents,
        };
    }

    // Part 1's session state rules (RunStore.StateOf) on the cut content and files.
    private static SessionState StateOf(SessionFiles files, SessionContent content, RunPhase phase, bool laterSessionExists)
    {
        if (content.Result is { } result)
            return result.IsError ? SessionState.Failed : SessionState.Succeeded;
        if (files.HasResultFile)
            return SessionState.Failed;
        return phase is RunPhase.Planning or RunPhase.Running or RunPhase.Stopping && !laterSessionExists
            ? SessionState.Running
            : SessionState.Aborted;
    }

    private static void Later(ref DateTimeOffset? latest, DateTimeOffset time)
    {
        if (latest is null || time > latest)
            latest = time;
    }

    private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> items) => items.IsDefault ? [] : items;
}

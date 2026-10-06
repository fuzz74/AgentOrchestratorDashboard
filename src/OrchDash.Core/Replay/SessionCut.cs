using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Replay;

/// <summary>The sessions at a replay time by the session cut table (spec 30.3, 30.6).</summary>
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

        // A last event after the time cuts the result, the checkpoint, the result file and the last event itself.
        var endCut = lastEvent > at;
        var rateLimitCut = content.RateLimit?.SeenAt > at;

        var storeCalls = OrEmpty(session.Stores.Calls);
        var keptStoreCalls = storeCalls.Where(entry => !(entry.Time > at)).ToImmutableArray();
        var injected = OrEmpty(session.Stores.Injected);
        var keptInjected = injected.Where(entry => !(entry.Time > at)).ToImmutableArray();
        var storesCut = keptStoreCalls.Length < storeCalls.Length || keptInjected.Length < injected.Length;

        if (!itemsCut && !callsCut && !endCut && !rateLimitCut && !storesCut)
            return session;

        var cut = session;
        if (itemsCut || callsCut || endCut || rateLimitCut)
        {
            cut = cut with
            {
                Content = content with
                {
                    Items = itemsCut ? keptItems.ToImmutable() : content.Items,
                    Calls = callsCut ? keptCalls : content.Calls,
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
            cut = cut with { Stores = session.Stores with { Calls = keptStoreCalls, Injected = keptInjected } };
        return cut;
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

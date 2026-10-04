using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Pages.ContextWindow.Format;

// The make-up rules of the Context page (15.3): a session's chain, its calls, and the parts of the context with
// their tokens. Parts are in part order and hold only parts with a birth below the number of calls.
public sealed record ContextMakeup(ImmutableArray<Session> Chain, ImmutableArray<ChainCall> Calls,
    ContextCheckpoint? LastCheckpoint, ImmutableArray<ContextPart> Parts)
{
    private static readonly PartCategory[] AlwaysShown = [PartCategory.SystemPrompt, PartCategory.ToolDefinitions];
    private static readonly PartCategory[] ShownWithParts =
        [PartCategory.Injected, PartCategory.Prompt, PartCategory.Conversation];

    // Never throws: no calls, no stores, default arrays and a session that is not in the snapshot give a make-up.
    public static ContextMakeup Build(RunSnapshot snapshot, Session session)
    {
        var chain = ChainOf(snapshot, session);
        var calls = CallsOf(chain);
        var checkpoint = chain.LastOrDefault(s => s.Content.Checkpoint is not null)?.Content.Checkpoint;
        var parts = MakeupParts.Collect(session, chain, calls, checkpoint);
        return new ContextMakeup(chain, calls, checkpoint, MakeupTokens.Assign(parts, calls));
    }

    // The largest context of the chain's calls; null while no call has usage.
    public long? Peak => Calls.Max(c => c.Call.Usage?.Context);

    // Context(i); null for a call without usage or an index outside the chain.
    public long? ContextAt(int callIndex) =>
        callIndex >= 0 && callIndex < Calls.Length ? Calls[callIndex].Call.Usage?.Context : null;

    // Context(k) - Context(j) with j the nearest earlier call with usage, or Context(k) without j.
    public long? StepAt(int callIndex)
    {
        if (ContextAt(callIndex) is not { } context)
            return null;
        for (var j = callIndex - 1; j >= 0; j--)
        {
            if (ContextAt(j) is { } previous)
                return context - previous;
        }
        return context;
    }

    // The parts in the context of the call, in part order.
    public ImmutableArray<ContextPart> PartsAt(int callIndex) => [.. Parts.Where(p => p.Birth <= callIndex)];

    // System prompt and Tool definitions always; Injected, Prompt and Conversation while they have parts; Other while
    // the call's context is above all known tokens. Tokens and shares are null for a call without usage.
    public ImmutableArray<CategoryTotal> TotalsAt(int callIndex)
    {
        var parts = PartsAt(callIndex);
        var context = ContextAt(callIndex);
        var totals = new List<CategoryTotal>();
        foreach (var category in AlwaysShown.Concat(ShownWithParts))
        {
            var own = parts.Where(p => p.Category == category).ToList();
            if (own.Count == 0 && ShownWithParts.Contains(category))
                continue;
            long? tokens = context is null ? null : own.Sum(p => p.Tokens ?? 0);
            totals.Add(new CategoryTotal(category, tokens, Share(tokens, context), own.Sum(p => p.Characters),
                own.Count, tokens is not null && own.Any(p => p.IsEstimate)));
        }

        if (context is { } known)
        {
            var other = known - parts.Sum(p => p.Tokens ?? 0);
            if (other > 0)
                totals.Add(new CategoryTotal(PartCategory.Other, other, Share(other, context), 0, 0, false));
        }
        return [.. totals];
    }

    // S plus the snapshot's other sessions with its SessionId, by StartedAt (null last), then Files.Key.
    private static ImmutableArray<Session> ChainOf(RunSnapshot snapshot, Session session)
    {
        var id = session.Content.SessionId;
        var others = id is null
            ? []
            : MakeupParts.OrEmpty(snapshot.Sessions).Where(s =>
                string.Equals(s.Content.SessionId, id, StringComparison.Ordinal) &&
                !string.Equals(s.Files.Key, session.Files.Key, StringComparison.Ordinal));
        return
        [
            .. others.Prepend(session)
                .OrderBy(s => s.StartedAt is null)
                .ThenBy(s => s.StartedAt)
                .ThenBy(s => s.Files.Key, StringComparer.Ordinal),
        ];
    }

    private static ImmutableArray<ChainCall> CallsOf(ImmutableArray<Session> chain) =>
    [
        .. chain
            .SelectMany(s => MakeupParts.OrEmpty(s.Content.Calls).Select(call => (Session: s, Call: call)))
            .Select((c, i) => new ChainCall(i, c.Session, c.Call)),
    ];

    private static double? Share(long? tokens, long? context) =>
        tokens is { } t && context is { } c && c > 0 ? (double)t / c : null;
}

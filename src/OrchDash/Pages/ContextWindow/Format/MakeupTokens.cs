using System.Collections.Immutable;

namespace OrchDash.Pages.ContextWindow.Format;

// The tokens of the make-up rules: each call with usage spreads its step over the parts born since the previous one.
internal static class MakeupTokens
{
    // parts carry their exact tokens (or null); the result has the known tokens and the estimate flags.
    public static ImmutableArray<ContextPart> Assign(ImmutableArray<ContextPart> parts, ImmutableArray<ChainCall> calls)
    {
        var tokens = new long?[parts.Length];
        var estimates = new bool[parts.Length];
        var previous = -1;
        long previousContext = 0;

        foreach (var call in calls)
        {
            if (call.Call.Usage is not { } usage)
                continue;
            var group = Enumerable.Range(0, parts.Length)
                .Where(i => parts[i].Birth > previous && parts[i].Birth <= call.Index)
                .ToList();
            Share(parts, group, usage.Context - previousContext, tokens, estimates);
            previous = call.Index;
            previousContext = usage.Context;
        }

        return [.. parts.Select((p, i) => p with { Tokens = tokens[i], IsEstimate = estimates[i] })];
    }

    // Exact parts keep their tokens; the others share what is left of the step by characters, rounded down, and the
    // rest of the rounding goes to the part with the most characters (the first on a tie).
    private static void Share(ImmutableArray<ContextPart> parts, List<int> group, long step, long?[] tokens,
        bool[] estimates)
    {
        var exact = 0L;
        var others = new List<int>();
        foreach (var i in group)
        {
            if (parts[i].Tokens is { } known)
            {
                tokens[i] = known;
                exact += known;
            }
            else
                others.Add(i);
        }

        var left = Math.Max(0, step - exact);
        var characters = others.Sum(i => parts[i].Characters);
        var given = 0L;
        var largest = -1;
        foreach (var i in others)
        {
            var share = characters > 0 ? (long)((Int128)left * parts[i].Characters / characters) : 0;
            tokens[i] = share;
            estimates[i] = true;
            given += share;
            if (largest < 0 || parts[i].Characters > parts[largest].Characters)
                largest = i;
        }
        if (characters > 0)
            tokens[largest] += left - given;
    }
}

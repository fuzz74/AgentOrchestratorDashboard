using System.Collections.Immutable;

namespace OrchDash.Core.Model;

// Pure; shared by the timeline builder and the replay function. A default array counts as empty.
public static class SessionTimes
{
    // One time per item, in item order: the item's Time; else the time given to the nearest earlier item; else start.
    public static ImmutableArray<DateTimeOffset> ItemTimes(ImmutableArray<ConversationItem> items, DateTimeOffset start)
    {
        if (items.IsDefaultOrEmpty)
            return [];

        var times = ImmutableArray.CreateBuilder<DateTimeOffset>(items.Length);
        var previous = start;
        foreach (var item in items)
        {
            previous = item.Time ?? previous;
            times.Add(previous);
        }
        return times.MoveToImmutable();
    }
}

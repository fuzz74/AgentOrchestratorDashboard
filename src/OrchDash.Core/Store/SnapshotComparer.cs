using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Store;

// Compares snapshots by content, ignoring Version and ReadAt (spec 5.2). Record equality compares
// immutable arrays and dictionaries by reference, so each record that holds one is compared as:
// its arrays element by element, then the record itself with those arrays copied over from the other side.
// A default array counts as empty.
internal static class SnapshotComparer
{
    public static bool SameContent(RunSnapshot a, RunSnapshot b) =>
        a.RepoPath == b.RepoPath
        && a.Run == b.Run
        && SamePlan(a.Plan, b.Plan)
        && SameItems(a.Tasks, b.Tasks, SameTask)
        && SameItems(a.Progress, b.Progress)
        && SameItems(a.Problems, b.Problems)
        && SameItems(a.Sessions, b.Sessions, SameSession);

    private static bool SamePlan(PlanInfo? a, PlanInfo? b) =>
        ReferenceEquals(a, b)
        || a is not null && b is not null
            && SameDictionary(a.Settings, b.Settings)
            && a == (b with { Settings = a.Settings });

    private static bool SameTask(TaskView a, TaskView b) =>
        ReferenceEquals(a, b)
        || SameItems(a.Deps, b.Deps)
            && SameItems(a.Owns, b.Owns)
            && a == (b with { Deps = a.Deps, Owns = a.Owns });

    private static bool SameSession(Session a, Session b) =>
        ReferenceEquals(a, b)
        || SameSessionContent(a.Content, b.Content)
            && SameStores(a.Stores, b.Stores)
            && SameItems(a.Unavailable, b.Unavailable)
            && a == (b with { Content = a.Content, Stores = a.Stores, Unavailable = a.Unavailable });

    // ModelCall and RateLimit hold no arrays, so record equality compares them by content.
    private static bool SameSessionContent(SessionContent a, SessionContent b) =>
        ReferenceEquals(a, b)
        || SameInit(a.Init, b.Init)
            && SameItems(a.Calls, b.Calls)
            && SameItems(a.Items, b.Items)
            && SameResult(a.Result, b.Result)
            && SameCheckpoint(a.Checkpoint, b.Checkpoint)
            && a == (b with { Init = a.Init, Calls = a.Calls, Items = a.Items, Result = a.Result, Checkpoint = a.Checkpoint });

    private static bool SameStores(StoreData a, StoreData b) =>
        ReferenceEquals(a, b)
        || SameItems(a.SystemPrompt, b.SystemPrompt)
            && SameItems(a.Tools, b.Tools)
            && SameItems(a.Injected, b.Injected)
            && SameItems(a.Calls, b.Calls)
            && a == (b with { SystemPrompt = a.SystemPrompt, Tools = a.Tools, Injected = a.Injected, Calls = a.Calls });

    private static bool SameCheckpoint(ContextCheckpoint? a, ContextCheckpoint? b) =>
        ReferenceEquals(a, b)
        || a is not null && b is not null
            && SameItems(a.ToolNames, b.ToolNames)
            && SameItems(a.SystemSegments, b.SystemSegments)
            && a == (b with { ToolNames = a.ToolNames, SystemSegments = a.SystemSegments });

    private static bool SameInit(SessionInit? a, SessionInit? b) =>
        ReferenceEquals(a, b)
        || a is not null && b is not null
            && SameItems(a.Tools, b.Tools)
            && SameItems(a.McpServers, b.McpServers)
            && a == (b with { Tools = a.Tools, McpServers = a.McpServers });

    private static bool SameResult(SessionResult? a, SessionResult? b) =>
        ReferenceEquals(a, b)
        || a is not null && b is not null
            && SameReview(a.Review, b.Review)
            && a == (b with { Review = a.Review });

    private static bool SameReview(ReviewVerdict? a, ReviewVerdict? b) =>
        ReferenceEquals(a, b)
        || a is not null && b is not null
            && SameItems(a.Issues, b.Issues)
            && a == (b with { Issues = a.Issues });

    private static bool SameItems<T>(ImmutableArray<T> a, ImmutableArray<T> b, Func<T, T, bool>? same = null)
    {
        var left = a.IsDefault ? [] : a;
        var right = b.IsDefault ? [] : b;
        if (left.Length != right.Length)
            return false;

        same ??= EqualityComparer<T>.Default.Equals;
        for (int i = 0; i < left.Length; i++)
        {
            if (!same(left[i], right[i]))
                return false;
        }
        return true;
    }

    private static bool SameDictionary(ImmutableSortedDictionary<string, string>? a, ImmutableSortedDictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if ((a?.Count ?? 0) != (b?.Count ?? 0))
            return false;
        if (a is null || b is null)
            return true;   // both empty

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || value != other)
                return false;
        }
        return true;
    }
}

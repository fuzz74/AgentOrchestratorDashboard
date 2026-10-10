using System.Collections.Immutable;
using System.Text;

namespace OrchDash.Core.Model;

// Reads the sub-agents of a session (35, 4.3). Pure; never throws; a default array counts as empty.
public static class SubAgents
{
    public const int NameLength = 32;

    private const string NoName = "sub-agent";
    private const int FirstCut = 20;   // a longer name is cut at its last space at index FirstCut..NameLength - 1

    // Whitespace runs become one space, then trimmed; "sub-agent" when empty. Longer than 32: cut at the last space
    // at index 20-31, else at 31, trim, append "…". (Same as Format-SubAgentName in the orchestrator.)
    public static string Name(string? description)
    {
        var text = Collapse(description);
        if (text.Length == 0)
            return NoName;
        if (text.Length <= NameLength)
            return text;

        var space = text.LastIndexOf(' ', NameLength - 1, NameLength - FirstCut);
        return text[..(space >= 0 ? space : NameLength - 1)].Trim() + "…";
    }

    // Items or calls whose AgentId equals agentId, in order; null gives the agent's own.
    public static ImmutableArray<ConversationItem> Items(SessionContent content, string? agentId) =>
        OfAgent(content.Items, agentId, item => item.AgentId);

    public static ImmutableArray<ModelCall> Calls(SessionContent content, string? agentId) =>
        OfAgent(content.Calls, agentId, call => call.AgentId);

    // null for null or unknown; the first one when two share the id.
    public static SubAgent? Find(SessionContent content, string? agentId)
    {
        var subs = OrEmpty(content.SubAgents);
        var index = IndexOf(subs, agentId);
        return index < 0 ? null : subs[index];
    }

    // Depth first: each SubAgent then its children; siblings in SubAgents order. A ParentId that names no
    // SubAgent counts as top level. Each SubAgent comes once. A ParentId cycle, which no top-level SubAgent
    // reaches, is cut at one of its members, so that it and the SubAgents below it still come, after the others.
    public static ImmutableArray<SubAgent> Tree(SessionContent content)
    {
        var subs = OrEmpty(content.SubAgents);
        return [.. Forest(subs).Select(node => subs[node.Index])];
    }

    // From the top-level ancestor down to agentId itself, along the Tree; empty for a null or unknown id.
    public static ImmutableArray<SubAgent> Lineage(SessionContent content, string agentId)
    {
        var subs = OrEmpty(content.SubAgents);
        var index = IndexOf(subs, agentId);
        if (index < 0)
            return [];

        var forest = Forest(subs);
        var lineage = new List<SubAgent>();
        for (var at = forest.FindIndex(node => node.Index == index); at >= 0; at = forest[at].Parent)
            lineage.Add(subs[forest[at].Index]);
        lineage.Reverse();
        return [.. lineage];
    }

    // The SubAgent whose ToolCallId is call.ToolId (non-empty); else null.
    public static SubAgent? StartedBy(SessionContent content, ToolCall call)
    {
        if (string.IsNullOrEmpty(call.ToolId))
            return null;
        foreach (var sub in OrEmpty(content.SubAgents))
        {
            if (string.Equals(sub.ToolCallId, call.ToolId, StringComparison.Ordinal))
                return sub;
        }
        return null;
    }

    // sub.State, but Aborted when sub.State is Running and session.State is not Running.
    public static SessionState StateOf(Session session, SubAgent sub) =>
        sub.State == SessionState.Running && session.State != SessionState.Running ? SessionState.Aborted : sub.State;

    // Every whitespace run as one space, without leading or trailing whitespace.
    private static string Collapse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(c);
        }
        return builder.ToString();
    }

    // The same array when every entry is the agent's.
    private static ImmutableArray<T> OfAgent<T>(ImmutableArray<T> all, string? agentId, Func<T, string?> agentOf)
    {
        if (all.IsDefault)
            return [];

        var kept = ImmutableArray.CreateBuilder<T>();
        foreach (var entry in all)
        {
            if (string.Equals(agentOf(entry), agentId, StringComparison.Ordinal))
                kept.Add(entry);
        }
        return kept.Count == all.Length ? all : kept.ToImmutable();
    }

    // The index of the first SubAgent with the id; -1 for null or unknown.
    private static int IndexOf(ImmutableArray<SubAgent> subs, string? agentId)
    {
        if (agentId is null)
            return -1;
        for (int i = 0; i < subs.Length; i++)
        {
            if (string.Equals(subs[i].Id, agentId, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    // The Tree as indexes into subs, each with the position of its parent in the result (-1 at the top level).
    // Iterative, so a deep chain cannot overflow the stack; each SubAgent is emitted once, so no cycle can hang.
    private static List<(int Index, int Parent)> Forest(ImmutableArray<SubAgent> subs)
    {
        var firstOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < subs.Length; i++)
            firstOf.TryAdd(subs[i].Id, i);
        var children = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < subs.Length; i++)
        {
            if (subs[i].ParentId is { } parentId && firstOf.ContainsKey(parentId))
            {
                if (!children.TryGetValue(parentId, out var siblings))
                    children[parentId] = siblings = [];
                siblings.Add(i);
            }
        }

        var forest = new List<(int Index, int Parent)>(subs.Length);
        var emitted = new bool[subs.Length];
        var pending = new Stack<(int Index, int Parent)>();
        void Walk(int root)
        {
            pending.Push((root, -1));
            while (pending.TryPop(out var node))
            {
                if (emitted[node.Index])
                    continue;
                emitted[node.Index] = true;
                forest.Add(node);
                if (!children.TryGetValue(subs[node.Index].Id, out var kids))
                    continue;
                for (int k = kids.Count - 1; k >= 0; k--)
                {
                    if (!emitted[kids[k]])
                        pending.Push((kids[k], forest.Count - 1));
                }
            }
        }

        for (int i = 0; i < subs.Length; i++)
        {
            if (subs[i].ParentId is not { } parentId || !firstOf.ContainsKey(parentId))
                Walk(i);
        }

        // What is left hangs below a cycle: climb from it until a SubAgent repeats, which lies on the cycle.
        for (int i = 0; i < subs.Length; i++)
        {
            if (emitted[i])
                continue;
            var climbed = new HashSet<int>();
            var at = i;
            while (climbed.Add(at) && subs[at].ParentId is { } parentId && firstOf.TryGetValue(parentId, out var parent))
                at = parent;
            Walk(at);
        }
        return forest;
    }

    private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> items) => items.IsDefault ? [] : items;
}

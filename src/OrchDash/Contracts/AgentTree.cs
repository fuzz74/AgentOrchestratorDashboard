using System.Collections.Immutable;
using System.Text;
using OrchDash.Core.Model;

namespace OrchDash.Contracts;

public sealed record AgentRow(Session Session, SubAgent SubAgent, int Depth, string Prefix);   // Depth 1: top level

public static class AgentTree
{
    private const string Through = "│ ";   // an ancestor level with a later sibling
    private const string Blank = "  ";     // an ancestor level without one
    private const string Branch = "├";     // the row has a later sibling
    private const string Last = "└";       // the row is the last of its siblings

    // SubAgents.Tree of each session, in the given order. The top-level SubAgents of all the sessions are siblings.
    // Prefix: for each ancestor level, "│ " when that ancestor has a later sibling, else "  "; then "├" when the
    // row has a later sibling, else "└". Examples: "├", "└", "│ └", "  ├".
    public static ImmutableArray<AgentRow> Rows(IEnumerable<Session> sessions)
    {
        var placed = new List<(Session Session, SubAgent SubAgent, int Depth)>();
        foreach (var session in sessions)
        {
            // The path from the top level down to the previous row; the Tree is depth first, so a row's parent is on
            // it. A row whose ParentId names none of it is top level, as SubAgents.Tree puts it.
            var path = new List<SubAgent>();
            foreach (var sub in SubAgents.Tree(session.Content))
            {
                while (path.Count > 0 && !string.Equals(path[^1].Id, sub.ParentId, StringComparison.Ordinal))
                    path.RemoveAt(path.Count - 1);
                path.Add(sub);
                placed.Add((session, sub, path.Count));
            }
        }

        // From the last row back: later[d - 1] tells whether a row at depth d follows before any row above depth d,
        // that is whether the row's ancestor at depth d, or at its own depth the row itself, has a later sibling.
        var rows = new AgentRow[placed.Count];
        var later = new List<bool>();
        for (int i = placed.Count - 1; i >= 0; i--)
        {
            var (session, sub, depth) = placed[i];
            while (later.Count < depth)
                later.Add(false);

            var prefix = new StringBuilder();
            for (int level = 0; level < depth - 1; level++)
                prefix.Append(later[level] ? Through : Blank);
            prefix.Append(later[depth - 1] ? Branch : Last);
            rows[i] = new AgentRow(session, sub, depth, prefix.ToString());

            later[depth - 1] = true;
            later.RemoveRange(depth, later.Count - depth);
        }
        return [.. rows];
    }
}

using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Contracts;

public static class AgentPath
{
    public const string Separator = " › ";

    // "[<TaskId> ]<Words.Role> <Words.Attempt>", e.g. "alpha worker #1", "planner #1" (the Timeline source today).
    public static string Of(Session session)
    {
        var files = session.Files;
        var agent = Words.Role(files.Role) + " " + Words.Attempt(files);
        return files.TaskId is { } taskId ? taskId + " " + agent : agent;
    }

    // Of(session), then Separator + Name for each SubAgent of Lineage; Of(session) for a null or unknown id.
    public static string Of(Session session, string? agentId)
    {
        var path = Of(session);
        if (agentId is null)
            return path;
        return path + string.Concat(SubAgents.Lineage(session.Content, agentId).Select(sub => Separator + sub.Name));
    }
}

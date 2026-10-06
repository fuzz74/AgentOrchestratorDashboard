using System.Collections.Immutable;
using System.Text.RegularExpressions;
using OrchDash.Core.Model;

namespace OrchDash.Core.Processes;

/// <summary>Matches agent processes to tasks, roles and sessions by the process matching table (spec 21.3, 4.3).</summary>
public static partial class ProcessRules
{
    private const string ReviewSuffix = ":review";
    private const string ResolveSuffix = ":resolve";

    /// <summary>
    /// The processes the matching table keeps, in the given order, each with <c>TaskId</c>, <c>Role</c> and
    /// <c>SessionId</c> set.
    /// </summary>
    public static ImmutableArray<AgentProcess> Match(ImmutableArray<AgentProcess> processes,
                                                     ImmutableArray<TaskView> tasks, ImmutableArray<Session> sessions)
    {
        if (processes.IsDefaultOrEmpty)
            return [];
        tasks = tasks.IsDefault ? [] : tasks;
        sessions = sessions.IsDefault ? [] : sessions;

        var taskIds = tasks.Select(task => task.Id).ToHashSet(StringComparer.Ordinal);
        var kept = ImmutableArray.CreateBuilder<AgentProcess>();
        foreach (var process in processes)
        {
            var resume = ResumeRegex().Match(process.CommandLine);
            var resumeId = resume.Success ? resume.Groups["sid"].Value : null;

            var name = NameRegex().Match(process.CommandLine);
            (string? TaskId, AgentRole Role)? owner = name.Success
                ? ByName(name.Groups["name"].Value, taskIds)
                : resumeId is not null ? ByResume(resumeId, tasks, sessions) : null;
            if (owner is not { } found)
                continue;

            var sessionId = resumeId ?? RunningSessionId(found.TaskId, found.Role, sessions);
            kept.Add(process with { TaskId = found.TaskId, Role = found.Role, SessionId = sessionId });
        }
        return kept.ToImmutable();
    }

    private static (string? TaskId, AgentRole Role)? ByName(string name, HashSet<string> taskIds)
    {
        if (name.Equals("planner", StringComparison.OrdinalIgnoreCase))
            return (null, AgentRole.Planner);
        if (name.Equals("bootstrap", StringComparison.OrdinalIgnoreCase))
            return (null, AgentRole.Bootstrap);

        var (id, role) = name.EndsWith(ReviewSuffix, StringComparison.OrdinalIgnoreCase) ? (name[..^ReviewSuffix.Length], AgentRole.Reviewer)
            : name.EndsWith(ResolveSuffix, StringComparison.OrdinalIgnoreCase) ? (name[..^ResolveSuffix.Length], AgentRole.Resolver)
            : (name, AgentRole.Worker);
        return taskIds.Contains(id) ? (id, role) : null;
    }

    private static (string? TaskId, AgentRole Role)? ByResume(string sessionId, ImmutableArray<TaskView> tasks,
                                                              ImmutableArray<Session> sessions)
    {
        foreach (var session in sessions)
        {
            if (string.Equals(session.Content.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                return (session.Files.TaskId, session.Files.Role);
        }
        foreach (var task in tasks)
        {
            if (string.Equals(task.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                return (task.Id, AgentRole.Worker);
        }
        return null;
    }

    // The last running session in snapshot order with the process's task and role.
    private static string? RunningSessionId(string? taskId, AgentRole role, ImmutableArray<Session> sessions)
    {
        for (var i = sessions.Length - 1; i >= 0; i--)
        {
            var session = sessions[i];
            if (session.State == SessionState.Running && session.Files.Role == role
                && string.Equals(session.Files.TaskId, taskId, StringComparison.Ordinal))
                return session.Content.SessionId;
        }
        return null;
    }

    [GeneratedRegex("""(?<=^|\s)--name[\s=]+"?orch:(?<name>[^"\s]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    [GeneratedRegex("""(?<=^|\s)--resume[\s=]+"?(?<sid>[0-9a-f-]{36})""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResumeRegex();
}

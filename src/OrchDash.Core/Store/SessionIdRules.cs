using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Store;

// Session id rules in spec 4.3 (14.2) for the Copilot sessions whose parser gave no SessionId. A rule may point to
// a session that gets its own id by a rule (attempt 3 from 2 from 1), so ids are resolved on demand, each session once.
internal sealed class SessionIdRules
{
    private readonly ImmutableArray<Session> _sessions;
    private readonly Func<SessionFiles, string> _workDir;
    private readonly ISessionIdFinder? _finder;
    private readonly HashSet<string> _knownIds = new(StringComparer.Ordinal);
    private readonly string?[] _ids;
    private readonly bool[] _visited;   // resolved or being resolved; a rule that points back gives no id

    private SessionIdRules(ImmutableArray<Session> sessions, Func<SessionFiles, string> workDir, ISessionIdFinder? finder)
    {
        _sessions = sessions;
        _workDir = workDir;
        _finder = finder;
        _ids = new string?[sessions.Length];
        _visited = new bool[sessions.Length];
        foreach (var session in sessions)
        {
            if (session.Content.SessionId is { } id)
                _knownIds.Add(id);
        }
    }

    // The session id of each session by position: the parser's, else the one the rules give, else null.
    public static string?[] Resolve(ImmutableArray<Session> sessions, Func<SessionFiles, string> workDir, ISessionIdFinder? finder)
    {
        var rules = new SessionIdRules(sessions, workDir, finder);
        for (int i = 0; i < sessions.Length; i++)
            rules._ids[i] = rules.IdOf(i);
        return rules._ids;
    }

    private string? IdOf(int index)
    {
        var session = _sessions[index];
        if (session.Content.SessionId is { } own)
            return own;
        if (session.Provider != Provider.Copilot || _visited[index])
            return _ids[index];

        _visited[index] = true;
        return _ids[index] = FromNudgeTarget(session.Files) ?? FromPreviousAttempt(session.Files) ?? Find(session);
    }

    // Rule 1: a nudge takes the id of the session it nudged.
    private string? FromNudgeTarget(SessionFiles files) =>
        files.IsNudge
            ? IdOfFirst(other => !other.IsNudge && other.TaskId == files.TaskId && other.Role == files.Role
                && other.StartFolder == files.StartFolder && other.Attempt == files.Attempt && other.ReviewTry == files.ReviewTry)
            : null;

    // Rule 2: a worker's later attempt resumes the session of the attempt before it.
    private string? FromPreviousAttempt(SessionFiles files) =>
        files is { Role: AgentRole.Worker, Attempt: > 1 }
            ? IdOfFirst(other => !other.IsNudge && other.Role == AgentRole.Worker && other.TaskId == files.TaskId
                && other.StartFolder == files.StartFolder && other.Attempt == files.Attempt - 1)
            : null;

    // Rule 3: the session folder whose workspace.yaml matches the session.
    private string? Find(Session session) =>
        _finder is not null && session.StartedAt is { } startedAt
            ? _finder.Find(NameOf(session.Files), _workDir(session.Files), startedAt, _knownIds)
            : null;

    // The session a rule points to is a Copilot session too.
    private string? IdOfFirst(Func<SessionFiles, bool> match)
    {
        for (int i = 0; i < _sessions.Length; i++)
        {
            if (_sessions[i].Provider == Provider.Copilot && match(_sessions[i].Files))
                return IdOf(i);
        }
        return null;
    }

    // The name the orchestrator gives the Copilot session.
    private static string NameOf(SessionFiles files) => files.Role switch
    {
        AgentRole.Bootstrap => "orch:bootstrap",
        AgentRole.Planner => "orch:planner",
        AgentRole.Reviewer => $"orch:{files.TaskId}:review",
        AgentRole.Resolver => $"orch:{files.TaskId}:resolve",
        _ => $"orch:{files.TaskId}",
    };
}

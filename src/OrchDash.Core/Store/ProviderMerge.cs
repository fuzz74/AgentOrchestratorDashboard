using System.Collections.Immutable;
using System.Globalization;
using OrchDash.Core.Model;

namespace OrchDash.Core.Store;

// Adds what the provider stores hold to the sessions of one poll (spec 14.1-14.7 and 14.9): session ids, store
// data, usage rows, call figures, the reasons for missing data and the version lines.
public static class ProviderMerge
{
    private const string NoSessionId = "session id not known yet";
    private const string NoTranscript = "no transcript";
    private const string NoSessionFolder = "no session folder";
    private const string NoDatabaseRows = "no database rows";

    // sessions: the sessions of one poll as RunStore builds them (Stores = StoreData.Empty, no Unavailable).
    // workDir: the folder the store passes to that session's parser.
    public static MergeResult Apply(ImmutableArray<Session> sessions, Func<SessionFiles, string> workDir, ProviderStores? stores)
    {
        ArgumentNullException.ThrowIfNull(workDir);
        if (stores is null || !HasSource(stores))
            return new MergeResult(sessions, []);

        var ids = SessionIdRules.Resolve(sessions, workDir, stores.CopilotIds);
        var read = new StoreData?[sessions.Length];
        for (int i = 0; i < sessions.Length; i++)
        {
            if (ids[i] is { } id)
                read[i] = ReadStore(sessions[i], id, workDir, stores);
        }
        var usage = ReadUsage(sessions, ids, stores.CopilotUsage);

        var merged = ImmutableArray.CreateBuilder<Session>(sessions.Length);
        for (int i = 0; i < sessions.Length; i++)
            merged.Add(Merge(sessions[i], ids[i], read[i], usage, stores));
        var result = merged.MoveToImmutable();
        return new MergeResult(result, Problems(result, usage));
    }

    private static bool HasSource(ProviderStores stores) =>
        stores.ClaudeTranscripts is not null || HasCopilotSource(stores);

    private static bool HasCopilotSource(ProviderStores stores) =>
        stores.CopilotFolders is not null || stores.CopilotIds is not null || stores.CopilotUsage is not null;

    // 14.3: null when the provider's store is not set or has nothing for the id.
    private static StoreData? ReadStore(Session session, string id, Func<SessionFiles, string> workDir, ProviderStores stores) =>
        session.Provider switch
        {
            Provider.Claude => stores.ClaudeTranscripts?.Read(id, session.Content.Init?.Cwd ?? workDir(session.Files)),
            Provider.Copilot => stores.CopilotFolders?.Read(id, workDir(session.Files)),
            _ => null,
        };

    // 14.4: one read per poll with the distinct ids of the Copilot sessions; null when there is nothing to read.
    private static UsageRows? ReadUsage(ImmutableArray<Session> sessions, string?[] ids, IUsageReader? reader)
    {
        if (reader is null)
            return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<string>();
        for (int i = 0; i < sessions.Length; i++)
        {
            if (sessions[i].Provider == Provider.Copilot && ids[i] is { } id && seen.Add(id))
                distinct.Add(id);
        }
        return distinct.Count == 0 ? null : reader.Read(distinct);
    }

    // Returns the session itself when nothing changes.
    private static Session Merge(Session session, string? id, StoreData? read, UsageRows? usage, ProviderStores stores)
    {
        if (session.Provider == Provider.Unknown)
            return session;

        if (id is null)
        {
            var hasSource = session.Provider == Provider.Claude ? stores.ClaudeTranscripts is not null : HasCopilotSource(stores);
            return hasSource ? session with { Unavailable = [NoSessionId] } : session;
        }

        var stored = read ?? StoreData.Empty;
        var unavailable = ImmutableArray.CreateBuilder<string>();
        if (session.Provider == Provider.Claude)
        {
            if (stores.ClaudeTranscripts is not null && read is null)
                unavailable.Add(NoTranscript);
        }
        else
        {
            if (stores.CopilotFolders is not null && read is null)
                unavailable.Add(NoSessionFolder);
            if (stores.CopilotUsage is not null)
            {
                var rows = RowsOf(session.Content, id, usage);
                if (rows.IsEmpty)
                    unavailable.Add(NoDatabaseRows);
                else
                    stored = stored with { Calls = rows };
            }
        }

        var content = session.Content.SessionId is null ? session.Content with { SessionId = id } : session.Content;
        content = WithFigures(content, session.Provider, stored);
        if (ReferenceEquals(content, session.Content) && ReferenceEquals(stored, session.Stores) && unavailable.Count == 0)
            return session;
        return session with { Content = content, Stores = stored, Unavailable = unavailable.ToImmutable() };
    }

    // 36.4, 36.5: each row of the id goes to its agent; per agent, the rows from that agent's first call on, cut to
    // its number of calls (a resumed attempt shares its id with the attempts before it). The kept rows, in query order.
    private static ImmutableArray<CallFigures> RowsOf(SessionContent content, string id, UsageRows? usage)
    {
        if (usage is null || !usage.BySession.TryGetValue(id, out var rows) || rows.IsDefaultOrEmpty)
            return [];

        var agents = new Agents(content.SubAgents);
        var firstStart = new DateTimeOffset?[agents.Count];
        var left = new int[agents.Count];
        foreach (var call in content.Calls)
        {
            var agent = agents.OfCall(call);
            if (agent != Agents.Unknown && left[agent]++ == 0)
                firstStart[agent] = call.StartedAt;
        }

        var kept = ImmutableArray.CreateBuilder<CallFigures>();
        foreach (var row in rows)
        {
            var agent = agents.OfRow(row);
            if (agent != Agents.Unknown && left[agent] > 0 && (firstStart[agent] is null || row.Time >= firstStart[agent]))
            {
                left[agent]--;
                kept.Add(row);
            }
        }
        return kept.ToImmutable();
    }

    // 14.5, 36.3, 36.4: a Claude call takes the figures with its id, a Copilot call the row at its position among
    // its agent's calls.
    private static SessionContent WithFigures(SessionContent content, Provider provider, StoreData stored)
    {
        if (content.Calls.IsEmpty)
            return content;

        var figures = provider == Provider.Claude ? ById(content.Calls, stored) : ByPosition(content, stored.Calls);
        ImmutableArray<ModelCall>.Builder? calls = null;
        for (int i = 0; i < figures.Length; i++)
        {
            if (figures[i] is not { } entry)
                continue;
            calls ??= content.Calls.ToBuilder();
            calls[i] = WithFigures(content.Calls[i], entry);
        }
        return calls is null ? content : content with { Calls = calls.ToImmutable() };
    }

    // The figures for each call by its id: from the transcript, then from the sub-agent transcripts in key order;
    // the first one with the id wins. Empty when there are no figures.
    private static CallFigures?[] ById(ImmutableArray<ModelCall> calls, StoreData stored)
    {
        var byId = new Dictionary<string, CallFigures>(StringComparer.Ordinal);
        AddById(byId, stored.Calls);
        foreach (var agentId in stored.SubAgents.Keys.Order(StringComparer.Ordinal))
            AddById(byId, stored.SubAgents[agentId].Calls);
        return byId.Count == 0 ? [] : [.. calls.Select(call => byId.GetValueOrDefault(call.Id))];
    }

    private static void AddById(Dictionary<string, CallFigures> byId, ImmutableArray<CallFigures> figures)
    {
        foreach (var entry in figures)
        {
            if (entry.CallId is { } callId)
                byId.TryAdd(callId, entry);
        }
    }

    // The figures for each call by its position among its agent's calls; the rows go to their agents as in RowsOf.
    // Empty when there are no rows.
    private static CallFigures?[] ByPosition(SessionContent content, ImmutableArray<CallFigures> rows)
    {
        if (rows.IsEmpty)
            return [];

        var agents = new Agents(content.SubAgents);
        var rowsOf = new List<CallFigures>?[agents.Count];
        foreach (var row in rows)
        {
            var agent = agents.OfRow(row);
            if (agent != Agents.Unknown)
                (rowsOf[agent] ??= []).Add(row);
        }

        var position = new int[agents.Count];
        var figures = new CallFigures?[content.Calls.Length];
        for (int i = 0; i < figures.Length; i++)
        {
            var agent = agents.OfCall(content.Calls[i]);
            if (agent == Agents.Unknown)
                continue;
            var at = position[agent]++;
            if (rowsOf[agent] is { } agentRows && at < agentRows.Count)
                figures[i] = agentRows[at];
        }
        return figures;
    }

    // A null figure keeps the call's own value.
    private static ModelCall WithFigures(ModelCall call, CallFigures figures) => call with
    {
        Usage = figures.Usage.Output is null && call.Usage?.Output is { } output ? figures.Usage with { Output = output } : figures.Usage,
        ThinkingTokens = figures.ThinkingTokens ?? call.ThinkingTokens,
        NanoAiu = figures.NanoAiu ?? call.NanoAiu,
        Duration = figures.Duration ?? call.Duration,
        StopReason = figures.StopReason ?? call.StopReason,
    };

    // 14.7: the usage problem, then the version rules in spec 4.3.
    private static ImmutableArray<string> Problems(ImmutableArray<Session> sessions, UsageRows? usage)
    {
        var problems = ImmutableArray.CreateBuilder<string>();
        if (usage?.Problem is { } problem)
            problems.Add(problem);

        AddVersionLines(problems, "Claude Code", TestedVersions.ClaudeCode, sessions
            .Where(s => s.Provider == Provider.Claude)
            .Select(s => s.Content.Init?.CliVersion ?? s.Stores.CliVersion));
        AddVersionLines(problems, "Copilot CLI", TestedVersions.CopilotCli, sessions
            .Where(s => s.Provider == Provider.Copilot)
            .Select(s => s.Stores.CliVersion));
        if (usage?.SchemaVersion is { } schema && schema != TestedVersions.CopilotSchema)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture,
                $"Copilot database schema {schema}: OrchDash was made for {TestedVersions.CopilotSchema}"));
        }
        return problems.ToImmutable();
    }

    // One line per distinct version that differs, in the order the versions first appear.
    private static void AddVersionLines(ImmutableArray<string>.Builder problems, string cli, string tested, IEnumerable<string?> versions)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var version in versions)
        {
            if (version is not null && version != tested && seen.Add(version))
                problems.Add($"{cli} {version}: OrchDash was made for {tested}");
        }
    }

    // The agents of a Copilot session as indexes below Count: Own is the agent itself, i + 1 the SubAgent at i, or
    // the first one before it with the same id.
    private sealed class Agents
    {
        public const int Unknown = -1;
        private const int Own = 0;

        private readonly Dictionary<string, int> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _byToolCallId = new(StringComparer.Ordinal);

        public Agents(ImmutableArray<SubAgent> subAgents)
        {
            var subs = subAgents.IsDefault ? [] : subAgents;
            Count = subs.Length + 1;
            for (int i = 0; i < subs.Length; i++)
            {
                if (string.IsNullOrEmpty(subs[i].Id))
                    continue;
                _byId.TryAdd(subs[i].Id, i + 1);
                if (!string.IsNullOrEmpty(subs[i].ToolCallId))
                    _byToolCallId.TryAdd(subs[i].ToolCallId, _byId[subs[i].Id]);
            }
        }

        public int Count { get; }

        // 36.4, 36.5: the SubAgent with the row's agent id, else the one its parent tool call started, else the agent
        // itself when the row has neither; else Unknown.
        public int OfRow(CallFigures row)
        {
            if (!string.IsNullOrEmpty(row.AgentId) && _byId.TryGetValue(row.AgentId, out var agent))
                return agent;
            if (!string.IsNullOrEmpty(row.ParentToolCallId) && _byToolCallId.TryGetValue(row.ParentToolCallId, out agent))
                return agent;
            return string.IsNullOrEmpty(row.AgentId) && string.IsNullOrEmpty(row.ParentToolCallId) ? Own : Unknown;
        }

        // The agent the call is tagged with; Unknown for an id that names no SubAgent.
        public int OfCall(ModelCall call) =>
            call.AgentId is null ? Own : _byId.GetValueOrDefault(call.AgentId, Unknown);
    }
}

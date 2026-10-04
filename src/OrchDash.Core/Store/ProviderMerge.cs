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
                var rows = RowsOf(session.Content.Calls, id, usage);
                if (rows.IsEmpty)
                    unavailable.Add(NoDatabaseRows);
                else
                    stored = stored with { Calls = rows };
            }
        }

        var content = session.Content.SessionId is null ? session.Content with { SessionId = id } : session.Content;
        content = WithFigures(content, session.Provider, stored.Calls);
        if (ReferenceEquals(content, session.Content) && ReferenceEquals(stored, session.Stores) && unavailable.Count == 0)
            return session;
        return session with { Content = content, Stores = stored, Unavailable = unavailable.ToImmutable() };
    }

    // The rows of the id from the session's first call on, cut to the number of calls: a resumed attempt shares
    // its id with the attempts before it.
    private static ImmutableArray<CallFigures> RowsOf(ImmutableArray<ModelCall> calls, string id, UsageRows? usage)
    {
        if (usage is null || !usage.BySession.TryGetValue(id, out var rows) || rows.IsDefaultOrEmpty)
            return [];

        var firstStart = calls.IsEmpty ? null : calls[0].StartedAt;
        return [.. rows.Where(row => firstStart is null || row.Time >= firstStart).Take(calls.Length)];
    }

    // 14.5: a Claude call takes the figures with its id, a Copilot call those at its position.
    private static SessionContent WithFigures(SessionContent content, Provider provider, ImmutableArray<CallFigures> figures)
    {
        if (figures.IsEmpty || content.Calls.IsEmpty)
            return content;

        Dictionary<string, CallFigures>? byId = null;
        if (provider == Provider.Claude)
        {
            byId = new Dictionary<string, CallFigures>(StringComparer.Ordinal);
            foreach (var entry in figures)
            {
                if (entry.CallId is { } callId)
                    byId.TryAdd(callId, entry);
            }
        }

        ImmutableArray<ModelCall>.Builder? calls = null;
        for (int i = 0; i < content.Calls.Length; i++)
        {
            var call = content.Calls[i];
            var entry = byId is not null ? byId.GetValueOrDefault(call.Id)
                : i < figures.Length ? figures[i]
                : null;
            if (entry is null)
                continue;
            calls ??= content.Calls.ToBuilder();
            calls[i] = WithFigures(call, entry);
        }
        return calls is null ? content : content with { Calls = calls.ToImmutable() };
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
}

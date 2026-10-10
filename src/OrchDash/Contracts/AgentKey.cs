using OrchDash.Core.Model;

namespace OrchDash.Contracts;

// IAppContext.SelectedSessionKey holds a SessionFiles.Key, or "<SessionFiles.Key>|<SubAgent.Id>".
public static class AgentKey
{
    public const char Separator = '|';

    // Files.Key[|agentId]
    public static string Of(Session session, string? agentId) =>
        agentId is null ? session.Files.Key : session.Files.Key + Separator + agentId;

    // split at the first '|'; null -> (null, null)
    public static (string? SessionKey, string? AgentId) Parse(string? key)
    {
        if (key is null)
            return (null, null);

        var at = key.IndexOf(Separator);
        return at < 0 ? (key, null) : (key[..at], key[(at + 1)..]);
    }
}

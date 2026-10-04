namespace OrchDash.Core.Model;

public sealed record ProviderStores(ISessionStore? ClaudeTranscripts, ISessionStore? CopilotFolders,
    ISessionIdFinder? CopilotIds, IUsageReader? CopilotUsage)
{
    // four nulls
    public static ProviderStores None { get; } = new(null, null, null, null);
}

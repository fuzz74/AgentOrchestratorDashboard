using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record Session(SessionFiles Files, Provider Provider, SessionState State,
    string Prompt, DateTimeOffset? StartedAt, SessionContent Content)
{
    public StoreData Stores { get; init; } = StoreData.Empty;        // what the provider stores hold for Content.SessionId
    public ImmutableArray<string> Unavailable { get; init; } = [];   // see the reasons table of the spec
}

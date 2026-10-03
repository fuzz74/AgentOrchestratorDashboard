namespace OrchDash.Core.Model;

public sealed record Session(SessionFiles Files, Provider Provider, SessionState State,
    string Prompt, DateTimeOffset? StartedAt, SessionContent Content);

using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record SessionContent(
    string? SessionId, string? Model, SessionInit? Init,
    ImmutableArray<ModelCall> Calls, ImmutableArray<ConversationItem> Items, SessionResult? Result,
    DateTimeOffset? FirstEventAt, DateTimeOffset? LastEventAt, int UnparsedLines)
{
    public static SessionContent Empty { get; } = new(
        null, null, null,
        ImmutableArray<ModelCall>.Empty, ImmutableArray<ConversationItem>.Empty, null,
        null, null, 0);
}

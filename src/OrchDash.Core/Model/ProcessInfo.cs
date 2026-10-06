using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record ProcessInfo(DateTimeOffset? SampledAt, ImmutableArray<AgentProcess> Processes, string? Problem)
{
    // null, empty, null; always the same instance
    public static ProcessInfo Empty { get; } = new(null, ImmutableArray<AgentProcess>.Empty, null);
}

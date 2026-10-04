using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record UsageRows(ImmutableDictionary<string, ImmutableArray<CallFigures>> BySession,   // only ids that have rows
    int? SchemaVersion, string? Problem)
{
    // no rows, null, null
    public static UsageRows Empty { get; } = new(
        ImmutableDictionary.Create<string, ImmutableArray<CallFigures>>(StringComparer.Ordinal), null, null);
}

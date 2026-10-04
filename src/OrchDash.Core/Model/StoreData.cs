using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record StoreData(string? CliVersion,
    ImmutableArray<string> SystemPrompt,          // text blocks in order; empty when unknown
    ImmutableArray<ToolDefinition> Tools,
    ImmutableArray<InjectedItem> Injected,        // file order
    ImmutableArray<CallFigures> Calls,            // Claude: by call id, in file order; Copilot: set by the store
    double? CostUsd, int? LinesAdded, int? LinesRemoved, int UnparsedLines)
{
    // nulls, empty arrays, 0
    public static StoreData Empty { get; } = new(
        null,
        ImmutableArray<string>.Empty, ImmutableArray<ToolDefinition>.Empty,
        ImmutableArray<InjectedItem>.Empty, ImmutableArray<CallFigures>.Empty,
        null, null, null, 0);
}

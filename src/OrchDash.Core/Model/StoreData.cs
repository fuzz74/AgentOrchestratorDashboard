using System.Collections.Immutable;

namespace OrchDash.Core.Model;

// What a provider store holds for one session; Claude adds one StoreData per sub-agent transcript (36.1).
public sealed record StoreData(string? CliVersion,
    ImmutableArray<string> SystemPrompt,          // text blocks in order; empty when unknown
    ImmutableArray<ToolDefinition> Tools,
    ImmutableArray<InjectedItem> Injected,        // file order
    ImmutableArray<CallFigures> Calls,            // Claude: by call id, in file order; Copilot: set by the store
    double? CostUsd, int? LinesAdded, int? LinesRemoved, int UnparsedLines)
{
    // One shared instance, so records with no sub-agents stay equal (SnapshotComparer, RunStoreMergeVersionTests).
    // Declared before Empty: static initializers run in textual order, and Empty's SubAgents reads it.
    public static ImmutableDictionary<string, StoreData> NoSubAgents { get; } =
        ImmutableDictionary.Create<string, StoreData>(StringComparer.Ordinal);

    // nulls, empty arrays, 0, NoSubAgents
    public static StoreData Empty { get; } = new(
        null,
        ImmutableArray<string>.Empty, ImmutableArray<ToolDefinition>.Empty,
        ImmutableArray<InjectedItem>.Empty, ImmutableArray<CallFigures>.Empty,
        null, null, null, 0);

    // Claude: one entry per sub-agent transcript, by SubAgent.Id (meta.json toolUseId)
    public ImmutableDictionary<string, StoreData> SubAgents { get; init; } = NoSubAgents;
}

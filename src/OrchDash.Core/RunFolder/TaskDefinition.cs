using System.Collections.Immutable;

namespace OrchDash.Core.RunFolder;

/// <summary>One task of <c>tasks.json</c>.</summary>
internal sealed record TaskDefinition(
    string Id, string Title, string Prompt, ImmutableArray<string> Deps, ImmutableArray<string> Owns,
    string? Acceptance, string? Model);

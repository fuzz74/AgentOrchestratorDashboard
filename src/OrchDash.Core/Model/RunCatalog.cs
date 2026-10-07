using System.Collections.Immutable;

namespace OrchDash.Core.Model;

// The runs of a repo: its own run first, then the archived runs by stamp descending (31.3, 31.4).
public sealed record RunCatalog(ImmutableArray<RunEntry> Runs, string? Problem);

using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Store;

// The sessions of one poll with the provider data added, and the problem lines the merge found (spec 14.7).
public sealed record MergeResult(ImmutableArray<Session> Sessions, ImmutableArray<string> Problems);

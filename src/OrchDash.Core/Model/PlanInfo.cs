using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record PlanInfo(string? Spec, string? BaseBranch, string IntegrationBranch,
    ImmutableSortedDictionary<string, string> Settings);   // tasks.json settings: name -> compact JSON value

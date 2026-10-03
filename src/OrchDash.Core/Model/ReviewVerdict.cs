using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record ReviewVerdict(string SpecVerdict, string QualityVerdict, string Summary, ImmutableArray<ReviewIssue> Issues);

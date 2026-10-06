namespace OrchDash.Core.Model;

public sealed record GitBranch(string Name, string Tip, DateTimeOffset? CommittedAt, GitBranchKind Kind);

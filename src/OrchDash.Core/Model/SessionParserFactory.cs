namespace OrchDash.Core.Model;

// workDir is <repo>.worktrees/<TaskId> for task sessions and the repo path for bootstrap and planner;
// the factory is not called for Provider.Unknown.
public delegate ISessionParser SessionParserFactory(Provider provider, string? workDir);

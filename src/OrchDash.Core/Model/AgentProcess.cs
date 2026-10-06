namespace OrchDash.Core.Model;

public sealed record AgentProcess(int Pid, string Name, string CommandLine, DateTimeOffset? StartedAt,
    long WorkingSetBytes, double? CpuShare,           // 0.12 = 12 % of all cores
    string? TaskId, AgentRole? Role, string? SessionId);   // null from the lister; set by ProcessRules.Match

namespace OrchDash.Core.Model;

public sealed record RunInfo(RunPhase Phase, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
    int? MaxParallel, Provider Provider, string? AgentPath, bool StopRequested);

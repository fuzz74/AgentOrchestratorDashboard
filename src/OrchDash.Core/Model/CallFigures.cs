namespace OrchDash.Core.Model;

public sealed record CallFigures(string? CallId, DateTimeOffset? Time, TokenUsage Usage,
    long? ThinkingTokens, long? NanoAiu, TimeSpan? Duration, string? StopReason);

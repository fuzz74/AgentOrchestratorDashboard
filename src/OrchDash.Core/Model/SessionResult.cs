namespace OrchDash.Core.Model;

public sealed record SessionResult(
    bool IsError, string Subtype, string? Text, string? StructuredJson,
    WorkerReport? Worker, ReviewVerdict? Review,
    double? CostUsd, int? Turns, TimeSpan? Duration, TimeSpan? ApiDuration,
    TokenUsage? Usage, long? ContextWindow, double? PremiumRequests, int? LinesAdded, int? LinesRemoved);

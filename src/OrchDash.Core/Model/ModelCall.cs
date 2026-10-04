namespace OrchDash.Core.Model;

public sealed record ModelCall(string Id, string? Model, DateTimeOffset? StartedAt, TokenUsage? Usage);

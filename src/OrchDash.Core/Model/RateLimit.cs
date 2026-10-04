namespace OrchDash.Core.Model;

public sealed record RateLimit(string? Status, string? LimitType,
    double? FiveHourUsed, DateTimeOffset? FiveHourResetsAt,          // used is a fraction: 0.2 = 20 %
    double? SevenDayUsed, DateTimeOffset? SevenDayResetsAt,
    DateTimeOffset? SeenAt);

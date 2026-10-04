namespace OrchDash.Core.Model;

public sealed record ProgressEntry(DateTimeOffset Time, string? Source, string Message, ProgressKind Kind);

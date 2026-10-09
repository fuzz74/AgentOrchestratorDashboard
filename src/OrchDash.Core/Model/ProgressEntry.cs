namespace OrchDash.Core.Model;

public sealed record ProgressEntry(DateTimeOffset Time, string? Source, string Message, ProgressKind Kind)
{
    public string? SubAgent { get; init; }   // name from "↳ [name] " (37.1)
}

namespace OrchDash.Core.Model;

public sealed record WorkerReport(string Status, string Summary, string? NotesForDependents, string? BlockedReason);

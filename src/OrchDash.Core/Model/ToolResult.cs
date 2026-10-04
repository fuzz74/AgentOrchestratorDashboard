namespace OrchDash.Core.Model;

public sealed record ToolResult(DateTimeOffset? Time, bool IsError, string Content, string? Diff, int? ExitCode);

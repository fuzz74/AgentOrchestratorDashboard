namespace OrchDash.Core.Model;

public sealed record CallFigures(string? CallId, DateTimeOffset? Time, TokenUsage Usage,
    long? ThinkingTokens, long? NanoAiu, TimeSpan? Duration, string? StopReason)
{
    public string? AgentId { get; init; }            // Copilot assistant_usage_events.agent_id
    public string? ParentToolCallId { get; init; }   // Copilot assistant_usage_events.parent_tool_call_id
}

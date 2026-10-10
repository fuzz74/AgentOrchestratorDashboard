namespace OrchDash.Core.Model;

public sealed record ModelCall(string Id, string? Model, DateTimeOffset? StartedAt, TokenUsage? Usage)
{
    public long? ThinkingTokens { get; init; }   // Claude thinking tokens, Copilot reasoning tokens
    public long? NanoAiu { get; init; }          // Copilot
    public TimeSpan? Duration { get; init; }     // Copilot
    public string? StopReason { get; init; }     // Claude stop_reason, Copilot finish_reason
    public string? AgentId { get; init; }        // null: the agent's own; else SubAgent.Id
}

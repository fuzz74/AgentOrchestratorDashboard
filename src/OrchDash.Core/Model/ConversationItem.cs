namespace OrchDash.Core.Model;

public abstract record ConversationItem(string? CallId, DateTimeOffset? Time)
{
    public string? AgentId { get; init; }        // null: the agent's own; else SubAgent.Id
}

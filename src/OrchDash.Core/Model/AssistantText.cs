namespace OrchDash.Core.Model;

public sealed record AssistantText(string? CallId, DateTimeOffset? Time, string Text) : ConversationItem(CallId, Time);

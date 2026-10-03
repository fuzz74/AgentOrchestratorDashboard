namespace OrchDash.Core.Model;

public sealed record Thinking(string? CallId, DateTimeOffset? Time, string Text, int? EstimatedTokens) : ConversationItem(CallId, Time);

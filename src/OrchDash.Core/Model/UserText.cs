namespace OrchDash.Core.Model;

public sealed record UserText(string? CallId, DateTimeOffset? Time, string Text, bool IsSynthetic) : ConversationItem(CallId, Time);

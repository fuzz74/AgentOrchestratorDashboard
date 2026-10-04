namespace OrchDash.Core.Model;

public sealed record Notice(string? CallId, DateTimeOffset? Time, string Kind, string Text) : ConversationItem(CallId, Time);

namespace OrchDash.Core.Model;

public sealed record ToolCall(string? CallId, DateTimeOffset? Time, string ToolId, string Name,
    string InputJson, string Summary, ToolResult? Result) : ConversationItem(CallId, Time);

namespace OrchDash.Pages.Conversation.Format;

public enum EntryKind
{
    Header,
    Prompt,
    CallSeparator,
    AssistantText,
    Thinking,
    ToolCall,
    UserText,
    Notice,
    Result,
    NoEventLog,
    SubAgent,   // the ToolCall that started a sub-agent (41.3)
}

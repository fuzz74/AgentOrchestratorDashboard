namespace OrchDash.Pages.ContextWindow.Format;

// The rows of the parts table and of the part pop-up table.
public enum PartKind
{
    SystemSegment,    // Source: TokenPart
    SystemBlock,      // Source: string
    ToolDefinition,   // Source: ToolDefinition
    ToolSummary,      // Source: ContextCheckpoint
    Injected,         // Source: InjectedItem
    Prompt,           // Source: Session; SubAgent in a sub-agent's make-up (42.3)
    ToolCall,         // Source: ToolCall
    Item,             // Source: ConversationItem
}

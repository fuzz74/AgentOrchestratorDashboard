namespace OrchDash.Pages.ContextWindow.Format;

// The categories of the context in the order of the parts table; Other occurs only in totals.
public enum PartCategory
{
    SystemPrompt,
    ToolDefinitions,
    Injected,
    Prompt,
    Conversation,
    Other,
}

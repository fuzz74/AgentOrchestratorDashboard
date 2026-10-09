namespace OrchDash.Core.Model;

// One event of the merged timeline (29.1-29.3, 38.2). <prefix> is the session's Files.Key.
public sealed record TimelineEvent(
    string Key,               // "progress:<i>", "<prefix>:prompt", "<prefix>:call:<i>", "<prefix>:item:<i>", "<prefix>:result",
                              // "<prefix>:sub:<id>:prompt", "<prefix>:sub:<id>:result"
    DateTimeOffset Time,
    TimelineKind Kind,
    string Group,             // "run", "bootstrap", "planner" or a task id
    int Index,                // the i of the key (index in Progress, Content.Calls or Content.Items); 0 for prompt and result
    ProgressEntry? Entry,     // Orchestrator
    Session? Session,         // every session event (Prompt, Call, Tool, Text, Result)
    ModelCall? Call,          // Call
    ConversationItem? Item,   // Tool (a ToolCall), Text (an AssistantText)
    SessionResult? Result)    // Result of the session; null for a sub-agent's Result
{
    public string? AgentId { get; init; }   // the SubAgent's Id for its events and its calls' and items'; else null
}

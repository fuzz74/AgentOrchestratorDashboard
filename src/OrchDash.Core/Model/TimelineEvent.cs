namespace OrchDash.Core.Model;

// One event of the merged timeline (29.1-29.3).
public sealed record TimelineEvent(
    string Key,               // "progress:<i>", "<Files.Key>:prompt", "<Files.Key>:call:<i>", "<Files.Key>:item:<i>", "<Files.Key>:result"
    DateTimeOffset Time,
    TimelineKind Kind,
    string Group,             // "run", "bootstrap", "planner" or a task id
    int Index,                // the i of the key (index in Progress, Content.Calls or Content.Items); 0 for prompt and result
    ProgressEntry? Entry,     // Orchestrator
    Session? Session,         // every session event (Prompt, Call, Tool, Text, Result)
    ModelCall? Call,          // Call
    ConversationItem? Item,   // Tool (a ToolCall), Text (an AssistantText)
    SessionResult? Result);   // Result

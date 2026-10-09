namespace OrchDash.Core.Model;

// One sub-agent of a session (35.1-35.5). Parsers set State to Running, Succeeded or Failed; SubAgents.StateOf
// adds Aborted.
public sealed record SubAgent(
    string Id,                  // Claude: id of the Agent/Task tool_use; Copilot: the agentId
    string? ParentId,           // the SubAgent whose call started it; null when the agent's own call did
    string ToolCallId,          // the starting tool call (Claude: equals Id); "" when unknown
    string Name,                // SubAgents.Name(Description)
    string? Description,        // as given, uncut
    string? AgentType,          // Claude subagent_type; Copilot agentType
    string? Model,              // Copilot data.model; Claude: the model of its first call
    bool Background,
    string Prompt,              // "" when unknown
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt, // null while running
    SessionState State,
    string? Report);            // null while unknown

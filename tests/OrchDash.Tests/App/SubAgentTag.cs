namespace OrchDash.Tests.App;

/// <summary>A Claude sub-agent as its events name it: the id of its Agent tool use, its type and its description.</summary>
internal sealed record SubAgentTag(string ToolUseId, string AgentType, string Description);

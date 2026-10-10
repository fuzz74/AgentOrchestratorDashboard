using System.Globalization;
using OrchDash.Core.Model;
using static OrchDash.Tests.App.SubAgentJson;

namespace OrchDash.Tests.App;

/// <summary>
/// Builds the lines of one Claude session for the end-to-end sub-agent run: its <c>events.jsonl</c> lines as in the
/// stream formats of spec 4.3, and its transcript lines in the Claude store (36.1). <c>system</c> lines and the
/// <c>result</c> line carry no timestamp, as in headless Claude Code.
/// </summary>
internal sealed class SubAgentClaudeLines(string sessionId)
{
    /// <summary>A system/init line.</summary>
    public string Init(string cwd, string model) =>
        $$"""{"type":"system","subtype":"init","cwd":{{Quote(cwd)}},"session_id":"{{sessionId}}","tools":["Agent","Glob","Read","StructuredOutput"],"mcp_servers":[],"model":"{{model}}","permissionMode":"acceptEdits","claude_code_version":"{{TestedVersions.ClaudeCode}}"}""";

    /// <summary>
    /// An assistant line of message <paramref name="messageId"/> with the given content blocks; with
    /// <paramref name="agent"/> it is that sub-agent's, with parent_tool_use_id, subagent_type and task_description at
    /// the top level (35.2).
    /// </summary>
    public string Assistant(string messageId, DateTimeOffset time, string model, TokenUsage usage, SubAgentTag? agent, params string[] blocks) =>
        $$"""{"type":"assistant","message":{"model":"{{model}}","id":"{{messageId}}","type":"message","role":"assistant","content":[{{string.Join(',', blocks)}}],"stop_reason":null,"usage":{{Usage(usage)}}},{{Parent(agent)}},"session_id":"{{sessionId}}","timestamp":"{{Time(time)}}"}""";

    /// <summary>A user line whose message.content is <paramref name="blocks"/>; with <paramref name="agent"/> it is that sub-agent's.</summary>
    public string User(DateTimeOffset time, SubAgentTag? agent, params string[] blocks) =>
        $$"""{"type":"user","message":{"role":"user","content":[{{string.Join(',', blocks)}}]},{{Parent(agent)}},"session_id":"{{sessionId}}","timestamp":"{{Time(time)}}"}""";

    /// <summary>A system/task_started line: task <paramref name="taskId"/> runs the sub-agent of <paramref name="agent"/>.</summary>
    public string TaskStarted(string taskId, SubAgentTag agent, string prompt, bool background) =>
        $$"""{"type":"system","subtype":"task_started","task_id":"{{taskId}}","tool_use_id":"{{agent.ToolUseId}}","description":{{Quote(agent.Description)}},"subagent_type":"{{agent.AgentType}}","is_backgrounded":{{Bool(background)}},"spawn_depth":1,"task_type":"local_agent","prompt":{{Quote(prompt)}},"session_id":"{{sessionId}}"}""";

    /// <summary>A system/task_notification line that reports task <paramref name="taskId"/> as completed with <paramref name="summary"/>.</summary>
    public string TaskCompleted(string taskId, string toolUseId, string summary) =>
        $$"""{"type":"system","subtype":"task_notification","task_id":"{{taskId}}","tool_use_id":"{{toolUseId}}","status":"completed","output_file":{{Quote(@"C:\Temp\" + taskId + ".output")}},"summary":{{Quote(summary)}},"usage":{"total_tokens":6000,"tool_uses":2,"duration_ms":8000},"session_id":"{{sessionId}}"}""";

    /// <summary>
    /// A successful result line with <paramref name="text"/> and fixed usage totals; <paramref name="structuredJson"/>,
    /// when given, is the structured_output object. The model's context window is 1M.
    /// </summary>
    public string Result(string text, string model, int turns, double costUsd, string? structuredJson = null) =>
        $$$"""{"type":"result","subtype":"success","is_error":false,"duration_ms":50000,"duration_api_ms":40000,"num_turns":{{{turns}}},"result":{{{Quote(text)}}},{{{(structuredJson is null ? "" : $"\"structured_output\":{structuredJson},")}}}"modelUsage":{"{{{model}}}":{"contextWindow":1000000,"maxOutputTokens":128000}},"usage":{"input_tokens":20,"cache_creation_input_tokens":9000,"cache_read_input_tokens":30000,"output_tokens":900},"total_cost_usd":{{{costUsd.ToString(CultureInfo.InvariantCulture)}}},"session_id":"{{{sessionId}}}"}""";

    /// <summary>
    /// A transcript line of the call <paramref name="messageId"/>, with its final usage; with <paramref name="agentId"/>
    /// it is a line of that sub-agent's transcript: isSidechain and its agentId (the task id).
    /// </summary>
    public string TranscriptCall(string messageId, DateTimeOffset time, string model, TokenUsage usage, string stopReason, string? agentId) =>
        $$"""{"parentUuid":null,{{Sidechain(agentId)}},"type":"assistant","message":{"model":"{{model}}","id":"{{messageId}}","type":"message","role":"assistant","content":[],"stop_reason":"{{stopReason}}","usage":{{Usage(usage)}}},"uuid":"{{Guid.NewGuid()}}","timestamp":"{{Time(time)}}","sessionId":"{{sessionId}}","version":"{{TestedVersions.ClaudeCode}}"}""";

    /// <summary>A transcript prompt_snapshot line: the system prompt blocks and the tools by name and description.</summary>
    public string TranscriptSnapshot(DateTimeOffset time, string[] systemPrompt, (string Name, string Description)[] tools, string? agentId) =>
        $$"""{"parentUuid":null,{{Sidechain(agentId)}},"type":"attachment","attachment":{"type":"prompt_snapshot","systemPrompt":[{{string.Join(',', systemPrompt.Select(Quote))}}],"tools":[{{string.Join(',', tools.Select(Tool))}}]},"uuid":"{{Guid.NewGuid()}}","timestamp":"{{Time(time)}}","sessionId":"{{sessionId}}","version":"{{TestedVersions.ClaudeCode}}"}""";

    /// <summary>The meta file of a sub-agent transcript, as a headless run writes it (36.1).</summary>
    public static string Meta(SubAgentTag agent, bool background) =>
        $$"""{"agentType":"{{agent.AgentType}}","description":{{Quote(agent.Description)}},"toolUseId":"{{agent.ToolUseId}}","spawnDepth":1,"requestShape":"{{(background ? "background" : "foreground")}}","requestNonInteractive":true}""";

    public static string Text(string text) => $$"""{"type":"text","text":{{Quote(text)}}}""";

    public static string ToolUse(string id, string name, string inputJson) =>
        $$"""{"type":"tool_use","id":"{{id}}","name":"{{name}}","caller":{"type":"direct"},"input":{{inputJson}}}""";

    /// <summary>The Agent tool use that starts the sub-agent of <paramref name="agent"/> (35.1).</summary>
    public static string AgentUse(SubAgentTag agent, string prompt, bool background) =>
        ToolUse(agent.ToolUseId, "Agent",
            $$"""{"description":{{Quote(agent.Description)}},"prompt":{{Quote(prompt)}},"subagent_type":"{{agent.AgentType}}","run_in_background":{{Bool(background)}}}""");

    public static string ToolResult(string toolUseId, string content) =>
        $$"""{"tool_use_id":"{{toolUseId}}","type":"tool_result","content":{{Quote(content)}},"is_error":false}""";

    private static string Parent(SubAgentTag? agent) =>
        agent is null
            ? "\"parent_tool_use_id\":null"
            : $$"""
                "parent_tool_use_id":"{{agent.ToolUseId}}","subagent_type":"{{agent.AgentType}}","task_description":{{Quote(agent.Description)}}
                """;

    private static string Sidechain(string? agentId) =>
        agentId is null ? "\"isSidechain\":false" : $"\"isSidechain\":true,\"agentId\":\"{agentId}\"";

    private static string Usage(TokenUsage usage) =>
        $$"""{"input_tokens":{{usage.Input}},"cache_creation_input_tokens":{{usage.CacheWrite}},"cache_read_input_tokens":{{usage.CacheRead}},"output_tokens":{{usage.Output ?? 0}}}""";

    private static string Tool((string Name, string Description) tool) =>
        $$$"""{"name":"{{{tool.Name}}}","schema":{"input_schema":{"type":"object"}},"description":{{{Quote(tool.Description)}}}}""";

    private static string Bool(bool value) => value ? "true" : "false";
}

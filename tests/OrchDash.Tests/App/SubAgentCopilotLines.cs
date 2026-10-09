using OrchDash.Core.Model;
using static OrchDash.Tests.App.SubAgentJson;

namespace OrchDash.Tests.App;

/// <summary>
/// Builds the lines of a Copilot session for the end-to-end sub-agent run: its <c>events.jsonl</c> lines as in the
/// stream formats of spec 4.3, where a sub-agent's events carry its <c>agentId</c> at the top level (35.4), and the
/// lines of its session folder's <c>events.jsonl</c> (36.6).
/// </summary>
internal static class SubAgentCopilotLines
{
    public static string UserMessage(DateTimeOffset time, string content) =>
        Event("user.message", time, null, $$"""{"content":{{Quote(content)}},"transformedContent":{{Quote(content)}},"attachments":[]}""");

    public static string TurnStart(DateTimeOffset time, string turnId, string? agentId) =>
        Event("assistant.turn_start", time, agentId, $$"""{"turnId":"{{turnId}}"}""");

    /// <summary>An assistant.message of turn <paramref name="turnId"/> with the given tool requests (<see cref="ToolRequest"/>).</summary>
    public static string Message(DateTimeOffset time, string turnId, string model, string? agentId, string content, params string[] toolRequests) =>
        Event("assistant.message", time, agentId,
            $$"""{"messageId":"{{Guid.NewGuid()}}","model":"{{model}}","content":{{Quote(content)}},"toolRequests":[{{string.Join(',', toolRequests)}}],"turnId":"{{turnId}}"}""");

    /// <summary>An assistant.message with phase final_answer: the agent's, or the sub-agent's report (35.5, 35.6).</summary>
    public static string FinalAnswer(DateTimeOffset time, string turnId, string model, string? agentId, string content) =>
        Event("assistant.message", time, agentId,
            $$"""{"messageId":"{{Guid.NewGuid()}}","model":"{{model}}","content":{{Quote(content)}},"toolRequests":[],"turnId":"{{turnId}}","phase":"final_answer"}""");

    public static string ToolRequest(string toolCallId, string name, string argumentsJson) =>
        $$"""{"toolCallId":"{{toolCallId}}","name":"{{name}}","arguments":{{argumentsJson}},"type":"function"}""";

    /// <summary>The arguments of a <c>task</c> call that starts a sub-agent.</summary>
    public static string TaskArguments(string description, string prompt, string agentType, string mode) =>
        $$"""{"description":{{Quote(description)}},"prompt":{{Quote(prompt)}},"agent_type":"{{agentType}}","mode":"{{mode}}"}""";

    /// <summary>A tool.execution_start; a sub-agent's names the call that started that sub-agent in parentToolCallId.</summary>
    public static string ToolStart(DateTimeOffset time, string toolCallId, string toolName, string argumentsJson, string? agentId, string? parentToolCallId) =>
        Event("tool.execution_start", time, agentId,
            $$"""{"toolCallId":"{{toolCallId}}","toolName":"{{toolName}}",{{(parentToolCallId is null ? "" : $"\"parentToolCallId\":\"{parentToolCallId}\",")}}"arguments":{{argumentsJson}}}""");

    public static string ToolComplete(DateTimeOffset time, string toolCallId, string content, string? agentId) =>
        Event("tool.execution_complete", time, agentId,
            $$"""{"toolCallId":"{{toolCallId}}","result":{"content":{{Quote(content)}}},"success":true}""");

    public static string SubagentStarted(DateTimeOffset time, string agentId, string toolCallId, string description, string agentType, string model, string mode) =>
        Event("subagent.started", time, agentId,
            $$"""{"toolCallId":"{{toolCallId}}","agentDescription":{{Quote(description)}},"agentDisplayName":"{{agentType}}","agentType":"{{agentType}}","model":"{{model}}","executionMode":"{{mode}}"}""");

    public static string SubagentCompleted(DateTimeOffset time, string agentId, string toolCallId) =>
        Event("subagent.completed", time, agentId, $$"""{"toolCallId":"{{toolCallId}}"}""");

    /// <summary>The result line, which names the session id (spec 4.3).</summary>
    public static string Result(DateTimeOffset time, string sessionId) =>
        $$"""{"type":"result","timestamp":"{{Time(time)}}","sessionId":"{{sessionId}}","usage":{"codeChanges":{"linesAdded":0,"linesRemoved":0,"filesModified":[]},"premiumRequests":1,"totalApiDurationMs":20000,"sessionDurationMs":26000},"exitCode":0}""";

    /// <summary>The session folder's session.start, which gives the CLI version.</summary>
    public static string SessionStart(DateTimeOffset time, string sessionId, string cwd) =>
        Event("session.start", time, null,
            $$"""{"sessionId":"{{sessionId}}","context":{"cwd":{{Quote(cwd)}}},"version":1,"producer":"copilot-agent","copilotVersion":"{{TestedVersions.CopilotCli}}","startTime":"{{Time(time)}}","selectedModel":"gpt-6-sol"}""");

    /// <summary>A session folder's system.message; a sub-agent's carries its <paramref name="agentId"/>.</summary>
    public static string SystemMessage(DateTimeOffset time, string content, string? agentId) =>
        Event("system.message", time, agentId, $$"""{"role":"system","content":{{Quote(content)}}}""");

    private static string Event(string type, DateTimeOffset time, string? agentId, string data) =>
        $$"""{"type":"{{type}}",{{(agentId is null ? "" : $"\"agentId\":\"{agentId}\",")}}"data":{{data}},"id":"{{Guid.NewGuid()}}","timestamp":"{{Time(time)}}"}""";
}

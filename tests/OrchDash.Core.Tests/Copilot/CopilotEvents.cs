using System.Globalization;
using System.Text.Json;
using OrchDash.Core.Copilot;
using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Copilot;

/// <summary>Builds Copilot events.jsonl lines in the shape the CLI writes them.</summary>
internal static class CopilotEvents
{
    public const string WorkDir = @"C:\Work\Repo.worktrees\core";

    /// <summary>The time of an event written at 09:00:<paramref name="second"/> UTC.</summary>
    public static DateTimeOffset At(int second) => new(2026, 10, 3, 9, 0, second, TimeSpan.Zero);

    /// <param name="agentId">The top-level <c>agentId</c> of a sub-agent's event (spec 4.3); null leaves it out.</param>
    public static string Event(string type, string data, int second = 0, bool ephemeral = false, string? agentId = null)
    {
        var agent = agentId is null ? "" : $"\"agentId\":{Quote(agentId)},";
        var flag = ephemeral ? "\"ephemeral\":true," : "";
        var time = At(second).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return $$"""{"type":"{{type}}",{{agent}}"data":{{data}},{{flag}}"id":"e{{second}}","timestamp":"{{time}}","parentId":null}""";
    }

    public static string TurnStart(string turnId, int second = 0, string? agentId = null) =>
        Event("assistant.turn_start", $$"""{"turnId":{{Quote(turnId)}},"interactionId":"i1"}""", second, agentId: agentId);

    public static string Message(string turnId, string content = "", string reasoning = "", string? phase = null,
        string model = "gpt-6-sol", int second = 0, string? agentId = null, params string[] toolRequests)
    {
        var phaseField = phase is null ? "" : $",\"phase\":{Quote(phase)}";
        var data = $$"""
            {"messageId":"m1","model":{{Quote(model)}},"content":{{Quote(content)}},"toolRequests":[{{string.Join(",", toolRequests)}}],"turnId":{{Quote(turnId)}},"reasoningText":{{Quote(reasoning)}}{{phaseField}}}
            """;
        return Event("assistant.message", data, second, agentId: agentId);
    }

    public static string ToolRequest(string toolCallId, string name, string arguments, string? intentionSummary = null)
    {
        var intention = intentionSummary is null ? "" : $",\"intentionSummary\":{Quote(intentionSummary)}";
        return $$"""{"toolCallId":{{Quote(toolCallId)}},"name":{{Quote(name)}},"arguments":{{arguments}},"type":"function"{{intention}}}""";
    }

    public static string ToolStart(string toolCallId, string toolName, string arguments, int second = 0,
        string? agentId = null) =>
        Event("tool.execution_start",
            $$"""{"toolCallId":{{Quote(toolCallId)}},"toolName":{{Quote(toolName)}},"arguments":{{arguments}},"turnId":"0"}""",
            second, agentId: agentId);

    public static string ToolComplete(string toolCallId, string fields, int second = 0, string? agentId = null) =>
        Event("tool.execution_complete", $$"""{"toolCallId":{{Quote(toolCallId)}},"turnId":"0",{{fields}}}""", second,
            agentId: agentId);

    /// <summary>The arguments of a <c>task</c> call; null ones are left out.</summary>
    public static string TaskArguments(string? description = null, string? prompt = null, string? agentType = null,
        string? mode = null) =>
        StringFields(("description", description), ("prompt", prompt), ("agent_type", agentType), ("mode", mode));

    /// <summary>The <c>tool.execution_start</c> of a <c>task</c> call, which starts a sub-agent.</summary>
    public static string TaskStart(string toolCallId, string? description = null, string? prompt = null,
        string? agentType = null, string? mode = null, int second = 0, string? agentId = null) =>
        ToolStart(toolCallId, "task", TaskArguments(description, prompt, agentType, mode), second, agentId);

    /// <summary>A <c>subagent.started</c> line; null data fields are left out.</summary>
    public static string SubagentStarted(string agentId, string? toolCallId, string? description = null,
        string? agentType = null, string? model = null, string? executionMode = null, string? displayName = null,
        int second = 0) =>
        Event("subagent.started",
            StringFields(("toolCallId", toolCallId), ("agentDescription", description), ("agentDisplayName", displayName),
                ("agentType", agentType), ("model", model), ("executionMode", executionMode)),
            second, agentId: agentId);

    public static string SubagentCompleted(string? agentId, string? toolCallId = null, int second = 0) =>
        Event("subagent.completed", StringFields(("toolCallId", toolCallId)), second, agentId: agentId);

    public static string SubagentFailed(string? agentId, string? toolCallId = null, int second = 0) =>
        Event("subagent.failed", StringFields(("toolCallId", toolCallId)), second, agentId: agentId);

    public static string Result(int exitCode, int second = 0)
    {
        var time = At(second).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        const string usage =
            """{"premiumRequests":1.5,"totalApiDurationMs":41112,"sessionDurationMs":52974,"codeChanges":{"linesAdded":445,"linesRemoved":23,"filesModified":["a.cs"]}}""";
        return $$"""{"type":"result","timestamp":"{{time}}","sessionId":"s-1","exitCode":{{exitCode}},"usage":{{usage}}}""";
    }

    public static string Quote(string text) => JsonSerializer.Serialize(text);

    /// <summary>A JSON object of string fields, without the null ones.</summary>
    private static string StringFields(params (string Name, string? Value)[] fields) =>
        "{" + string.Join(",", fields.Where(field => field.Value is not null)
            .Select(field => $"{Quote(field.Name)}:{Quote(field.Value!)}")) + "}";

    public static SessionContent Parse(params string[] lines) => ParseIn(WorkDir, lines);

    public static SessionContent ParseIn(string? workDir, params string[] lines)
    {
        var parser = new CopilotSessionParser(workDir);
        foreach (var line in lines)
            parser.AddLine(line);
        return parser.Build();
    }
}

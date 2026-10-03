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

    public static string Event(string type, string data, int second = 0, bool ephemeral = false)
    {
        var flag = ephemeral ? "\"ephemeral\":true," : "";
        var time = At(second).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return $$"""{"type":"{{type}}","data":{{data}},{{flag}}"id":"e{{second}}","timestamp":"{{time}}","parentId":null}""";
    }

    public static string TurnStart(string turnId, int second = 0) =>
        Event("assistant.turn_start", $$"""{"turnId":{{Quote(turnId)}},"interactionId":"i1"}""", second);

    public static string Message(string turnId, string content = "", string reasoning = "", string? phase = null,
        string model = "gpt-6-sol", int second = 0, params string[] toolRequests)
    {
        var phaseField = phase is null ? "" : $",\"phase\":{Quote(phase)}";
        var data = $$"""
            {"messageId":"m1","model":{{Quote(model)}},"content":{{Quote(content)}},"toolRequests":[{{string.Join(",", toolRequests)}}],"turnId":{{Quote(turnId)}},"reasoningText":{{Quote(reasoning)}}{{phaseField}}}
            """;
        return Event("assistant.message", data, second);
    }

    public static string ToolRequest(string toolCallId, string name, string arguments, string? intentionSummary = null)
    {
        var intention = intentionSummary is null ? "" : $",\"intentionSummary\":{Quote(intentionSummary)}";
        return $$"""{"toolCallId":{{Quote(toolCallId)}},"name":{{Quote(name)}},"arguments":{{arguments}},"type":"function"{{intention}}}""";
    }

    public static string ToolStart(string toolCallId, string toolName, string arguments, int second = 0) =>
        Event("tool.execution_start",
            $$"""{"toolCallId":{{Quote(toolCallId)}},"toolName":{{Quote(toolName)}},"arguments":{{arguments}},"turnId":"0"}""",
            second);

    public static string ToolComplete(string toolCallId, string fields, int second = 0) =>
        Event("tool.execution_complete", $$"""{"toolCallId":{{Quote(toolCallId)}},"turnId":"0",{{fields}}}""", second);

    public static string Result(int exitCode, int second = 0)
    {
        var time = At(second).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        const string usage =
            """{"premiumRequests":1.5,"totalApiDurationMs":41112,"sessionDurationMs":52974,"codeChanges":{"linesAdded":445,"linesRemoved":23,"filesModified":["a.cs"]}}""";
        return $$"""{"type":"result","timestamp":"{{time}}","sessionId":"s-1","exitCode":{{exitCode}},"usage":{{usage}}}""";
    }

    public static string Quote(string text) => JsonSerializer.Serialize(text);

    public static SessionContent Parse(params string[] lines) => ParseIn(WorkDir, lines);

    public static SessionContent ParseIn(string? workDir, params string[] lines)
    {
        var parser = new CopilotSessionParser(workDir);
        foreach (var line in lines)
            parser.AddLine(line);
        return parser.Build();
    }
}

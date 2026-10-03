using OrchDash.Core.Claude;
using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Claude;

/// <summary>Builds Claude events.jsonl lines for the parser tests.</summary>
internal static class ClaudeLines
{
    public const string SessionId = "sess-1";
    public const string ModelName = "claude-opus-5-5";
    public const string Time1 = "2026-10-01T08:46:40.513Z";
    public const string Time2 = "2026-10-01T08:46:41.464Z";
    public const string Time3 = "2026-10-01T08:46:43.772Z";

    public static readonly DateTimeOffset At1 = new(2026, 10, 1, 8, 46, 40, 513, TimeSpan.Zero);
    public static readonly DateTimeOffset At2 = new(2026, 10, 1, 8, 46, 41, 464, TimeSpan.Zero);
    public static readonly DateTimeOffset At3 = new(2026, 10, 1, 8, 46, 43, 772, TimeSpan.Zero);

    /// <summary>A system/init line with cwd <c>C:\Work\Repo</c>.</summary>
    public static string Init(string cwd = @"C:\\Work\\Repo") =>
        $$"""{"type":"system","subtype":"init","cwd":"{{cwd}}","session_id":"{{SessionId}}","tools":["Bash","Read"],"mcp_servers":[{"name":"docs","status":"connected","source":"claudeai"},{"name":"sample","status":"failed"}],"model":"{{ModelName}}","permissionMode":"acceptEdits","claude_code_version":"2.1.285"}""";

    public static string ThinkingTokens(int estimated) =>
        $$"""{"type":"system","subtype":"thinking_tokens","estimated_tokens":{{estimated}},"estimated_tokens_delta":50,"session_id":"{{SessionId}}"}""";

    /// <summary>An assistant line of message <paramref name="messageId"/> with the given content blocks.</summary>
    public static string Assistant(string messageId, string time, string blocks, int input = 2, int cacheRead = 17465, int cacheWrite = 29331) =>
        $$$"""{"type":"assistant","message":{"model":"{{{ModelName}}}","id":"{{{messageId}}}","type":"message","role":"assistant","content":[{{{blocks}}}],"usage":{"input_tokens":{{{input}}},"cache_creation_input_tokens":{{{cacheWrite}}},"cache_read_input_tokens":{{{cacheRead}}},"output_tokens":8}},"parent_tool_use_id":null,"session_id":"{{{SessionId}}}","timestamp":"{{{time}}}"}""";

    public static string Text(string text) => $$"""{"type":"text","text":"{{text}}"}""";

    public static string ThinkingBlock(string text) => $$"""{"type":"thinking","thinking":"{{text}}","signature":"abc"}""";

    public static string ToolUse(string id, string name, string input) =>
        $$$"""{"type":"tool_use","id":"{{{id}}}","name":"{{{name}}}","input":{{{input}}},"caller":{"type":"direct"}}""";

    /// <summary>A user line; <paramref name="content"/> is the JSON value of message.content.</summary>
    public static string User(string time, string content, string extra = "") =>
        $$"""{"type":"user","message":{"role":"user","content":{{content}}},"parent_tool_use_id":null,"session_id":"{{SessionId}}","timestamp":"{{time}}"{{extra}}}""";

    public static string ToolResultBlock(string toolUseId, string content, bool isError = false) =>
        $$"""{"tool_use_id":"{{toolUseId}}","type":"tool_result","content":{{content}},"is_error":{{(isError ? "true" : "false")}}}""";

    public static SessionContent ParseIn(string? workDir, params string[] lines)
    {
        var parser = new ClaudeSessionParser(workDir);
        foreach (var line in lines)
            parser.AddLine(line);
        return parser.Build();
    }

    public static SessionContent Parse(params string[] lines) => ParseIn(null, lines);
}

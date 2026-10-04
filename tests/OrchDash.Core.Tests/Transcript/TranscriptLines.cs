namespace OrchDash.Core.Tests.Transcript;

/// <summary>Small transcript lines for the tests.</summary>
internal static class TranscriptLines
{
    public const string SessionId = "11111111-2222-3333-4444-555555555555";
    public const string WorkDir = @"C:\Work\Demo.worktrees\core";
    public const string Folder = "C--Work-Demo-worktrees-core";

    public const string Time1 = "2026-10-01T08:46:40.513Z";
    public const string Time2 = "2026-10-01T08:47:00Z";

    public static readonly DateTimeOffset At1 = new(2026, 10, 1, 8, 46, 40, 513, TimeSpan.Zero);
    public static readonly DateTimeOffset At2 = new(2026, 10, 1, 8, 47, 0, TimeSpan.Zero);

    /// <summary>The lines, each ended by '\n'.</summary>
    public static string Lines(params string[] lines) => string.Concat(lines.Select(line => line + "\n"));

    public static string Assistant(string id, string time, string usage, string stopReason = "tool_use") =>
        $$$"""{"type":"assistant","timestamp":"{{{time}}}","version":"2.1.285","isSidechain":false,"message":{"id":"{{{id}}}","stop_reason":"{{{stopReason}}}","usage":{{{usage}}},"content":[]}}""";

    public static string Usage(long input, long cacheRead, long cacheWrite, long output, long thinking) =>
        $$$"""{"input_tokens":{{{input}}},"cache_creation_input_tokens":{{{cacheWrite}}},"cache_read_input_tokens":{{{cacheRead}}},"output_tokens":{{{output}}},"output_tokens_details":{"thinking_tokens":{{{thinking}}}}}""";

    public static string Injected(string kind, string time, string text) =>
        $$$"""{"type":"attachment","timestamp":"{{{time}}}","renderedRole":"system","rendered":[{"content":"{{{text}}}"}],"attachment":{"type":"{{{kind}}}"}}""";

    public static string CostState(double cost, int added, int removed) =>
        $$"""{"type":"cost-state","totalCostUSD":{{cost.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"totalLinesAdded":{{added}},"totalLinesRemoved":{{removed}}}""";
}

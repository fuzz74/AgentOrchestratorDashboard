using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OrchDash.Core.Model;

namespace OrchDash.Core.RunFolder;

/// <summary>Turns the text of <c>progress.md</c> into entries by the progress rules (spec 2.5, 4.3).</summary>
public static partial class ProgressParser
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string Separator = "  ";
    private const string SubAgentTagStart = "↳ [";
    private const string SubAgentTagEnd = "] ";

    private static readonly string[] AgentToolNames =
        ["Read", "Edit", "Write", "MultiEdit", "NotebookEdit", "Glob", "Grep", "Bash", "PowerShell", "StructuredOutput", "Task", "Agent"];

    private static readonly string[] FailurePrefixes =
        ["FAILED", "worker error", "acceptance failed", "review rejected", "commit failed", "sync with integration failed"];

    private static readonly string[] SuccessPrefixes =
        ["DONE", "review passed", "Plan written", "Skeleton committed", "Run finished"];

    private static readonly string[] WarningPrefixes =
        ["merge conflict", "paused", "Graceful stop", "edited files outside owns", "no changes", "worker gave no structured result", "Plan invalid"];

    /// <summary>Parses the text of <c>progress.md</c>; never throws.</summary>
    public static ImmutableArray<ProgressEntry> Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        if (text[0] == '﻿')
            text = text[1..];

        var entries = ImmutableArray.CreateBuilder<ProgressEntry>();
        DateTimeOffset time = default;
        string? source = null;
        string? subAgent = null;
        StringBuilder? message = null;

        var lines = text.Split('\n');
        // The empty string after the last line break is not a line.
        var count = lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++)
        {
            var line = lines[i].EndsWith('\r') ? lines[i][..^1] : lines[i];
            if (TryParseEntryStart(line, out var lineTime, out var rest))
            {
                if (message is not null)
                    entries.Add(CreateEntry(time, source, subAgent, message.ToString()));

                time = lineTime;
                (source, var body) = SplitSource(rest);
                (subAgent, body) = SplitSubAgent(body);
                message = new StringBuilder(body);
            }
            else
            {
                message?.Append('\n').Append(line);
            }
        }

        if (message is not null)
            entries.Add(CreateEntry(time, source, subAgent, message.ToString()));

        return entries.ToImmutable();
    }

    private static bool TryParseEntryStart(string line, out DateTimeOffset time, out string message)
    {
        time = default;
        message = "";
        var prefixLength = TimeFormat.Length + Separator.Length;
        if (line.Length < prefixLength || string.CompareOrdinal(line, TimeFormat.Length, Separator, 0, Separator.Length) != 0)
            return false;

        if (!DateTime.TryParseExact(line.AsSpan(0, TimeFormat.Length), TimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var wallTime))
            return false;

        time = new DateTimeOffset(wallTime, TimeZoneInfo.Local.GetUtcOffset(wallTime));
        message = line[prefixLength..];
        return true;
    }

    private static (string? Source, string Message) SplitSource(string message)
    {
        if (!message.StartsWith('['))
            return (null, message);

        var end = message.IndexOf("] ", StringComparison.Ordinal);
        if (end <= 1 || message.AsSpan(1, end - 1).Contains(']'))
            return (null, message);

        return (message[1..end], message[(end + 2)..]);
    }

    // 37.1, 37.2: a leading "↳ [<name>] " names the sub-agent, the name being the text up to the first "] "; a tag
    // with an empty name, or one anywhere else, stays part of the message.
    private static (string? SubAgent, string Message) SplitSubAgent(string message)
    {
        if (!message.StartsWith(SubAgentTagStart, StringComparison.Ordinal))
            return (null, message);

        var end = message.IndexOf(SubAgentTagEnd, SubAgentTagStart.Length, StringComparison.Ordinal);
        if (end <= SubAgentTagStart.Length)
            return (null, message);

        return (message[SubAgentTagStart.Length..end], message[(end + SubAgentTagEnd.Length)..]);
    }

    private static ProgressEntry CreateEntry(DateTimeOffset time, string? source, string? subAgent, string message) =>
        new(time, source, message, GetKind(source, message)) { SubAgent = subAgent };

    private static ProgressKind GetKind(string? source, string message)
    {
        if (ActivityCountRegex().IsMatch(message)
            || message.StartsWith("tool: ", StringComparison.Ordinal)
            || (source is "planner" or "bootstrap" && AgentToolNames.Contains(FirstWord(message), StringComparer.Ordinal)))
            return ProgressKind.Activity;

        if (StartsWithAny(message, FailurePrefixes) || AttemptFailedRegex().IsMatch(message))
            return ProgressKind.Failure;

        if (StartsWithAny(message, SuccessPrefixes))
            return ProgressKind.Success;

        if (StartsWithAny(message, WarningPrefixes))
            return ProgressKind.Warning;

        return ProgressKind.Info;
    }

    private static string FirstWord(string message)
    {
        var end = message.AsSpan().IndexOfAny(' ', '\n');
        return end < 0 ? message : message[..end];
    }

    private static bool StartsWithAny(string message, string[] prefixes) =>
        prefixes.Any(prefix => message.StartsWith(prefix, StringComparison.Ordinal));

    [GeneratedRegex("^(worker|review|resolver): [0-9]+ tool calls", RegexOptions.CultureInvariant)]
    private static partial Regex ActivityCountRegex();

    [GeneratedRegex("^attempt [0-9]+ failed", RegexOptions.CultureInvariant)]
    private static partial Regex AttemptFailedRegex();
}

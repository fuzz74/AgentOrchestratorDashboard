using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OrchDash.Core.RunFolder;

/// <summary>The texts of the task detail table and the owns-overlap rule (spec 2.4, 4.3).</summary>
public static partial class TaskDetails
{
    public const string Blocked = "a dependency failed";
    public const string Ready = "ready";
    public const string FreeSlot = "waiting for a free slot";
    public const string Starting = "starting";
    public const string IntegrationCheck = "integration check";

    private const string SetupLog = "setup.log";
    private const string IntegrationLogSuffix = ".log";

    public static string Done(double costUsd, int attempts) =>
        string.Create(CultureInfo.InvariantCulture, $"{costUsd:0.00} USD, {attempts} attempt(s)");

    // The first non-empty line of the error, or "" when there is none.
    public static string Failed(string? error) =>
        error?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";

    public static string WaitingForDeps(IEnumerable<string> ids) => "waiting for " + string.Join(", ", ids);

    public static string WaitingForOwns(string runningTaskId) => $"waiting for {runningTaskId} (owns overlap)";

    /// <summary>Two tasks overlap when either has no owns, or a prefix of one starts with a prefix of the other.</summary>
    public static bool OwnsOverlap(ImmutableArray<string> owns, ImmutableArray<string> otherOwns)
    {
        if (owns.IsDefaultOrEmpty || otherOwns.IsDefaultOrEmpty)
            return true;

        var otherPrefixes = otherOwns.Select(GlobPrefix).ToArray();
        return owns.Select(GlobPrefix).Any(prefix => otherPrefixes.Any(other =>
            prefix.StartsWith(other, StringComparison.Ordinal) || other.StartsWith(prefix, StringComparison.Ordinal)));
    }

    // The glob with '\' as '/', a leading "./" removed, in lower case, cut before the first '*', '?' or '['.
    private static string GlobPrefix(string glob)
    {
        var prefix = glob.Replace('\\', '/');
        if (prefix.StartsWith("./", StringComparison.Ordinal))
            prefix = prefix[2..];
        prefix = prefix.ToLowerInvariant();
        var wildcard = prefix.IndexOfAny(['*', '?', '[']);
        return wildcard < 0 ? prefix : prefix[..wildcard];
    }

    /// <summary>The step of a running task, from its latest start folder and the integration logs in the log root.</summary>
    internal static string Running(string taskId, LogsListing logs)
    {
        if (!logs.TryGetLatestStartFolder(taskId, out var files) || files.Count == 0)
            return Starting;

        var newest = files[0];
        foreach (var file in files.Skip(1))
        {
            var compare = file.LastWriteTimeUtc.CompareTo(newest.LastWriteTimeUtc);
            if (compare > 0 || (compare == 0 && string.CompareOrdinal(file.Name, newest.Name) > 0))
                newest = file;
        }

        var integrationPrefix = taskId + "-integration-";
        if (logs.RootFiles.Any(file => file.Name.StartsWith(integrationPrefix, StringComparison.Ordinal)
                && file.Name.EndsWith(IntegrationLogSuffix, StringComparison.Ordinal)
                && file.LastWriteTimeUtc > newest.LastWriteTimeUtc))
            return IntegrationCheck;

        return Step(newest.Name);
    }

    private static string Step(string fileName)
    {
        if (fileName == SetupLog)
            return "setup";

        var match = StepFile().Match(fileName);
        if (!match.Success)
            return "working";

        var step = $"{match.Groups["step"].Value} (attempt {match.Groups["n"].Value})";
        // A session's X.json, or the X.json of its nudge, is written when the agent exits.
        var isResultFile = match.Groups["ext"].Value is "json" or "json.nudge.json";
        return isResultFile ? step + " finished" : step;
    }

    [GeneratedRegex(
        @"\Aattempt-(?<n>[0-9]+)-(?:(?:(?<step>worker|resolver)|(?<step>review)-[0-9]+)\.(?<ext>.*)|(?<step>acceptance)\.log.*)\z",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex StepFile();
}

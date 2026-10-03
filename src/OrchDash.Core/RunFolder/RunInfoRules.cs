using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using OrchDash.Core.Model;

namespace OrchDash.Core.RunFolder;

/// <summary>Derives <see cref="RunInfo"/> from the progress entries by the run phase rules (spec 2.6, 2.9, 4.3).</summary>
public static partial class RunInfoRules
{
    private const string RunStartedPrefix = "Run started: ";
    private const string RunFinishedPrefix = "Run finished";
    private const string ClaudePrefix = "Claude: ";
    private const string CopilotPrefix = "Copilot: ";

    /// <summary>
    /// True when the latest <c>Run started</c> entry has no later <c>Run finished</c> entry (2.9):
    /// only then <c>run.lock</c> is tested.
    /// </summary>
    public static bool NeedsLockTest(ImmutableArray<ProgressEntry> progress)
    {
        var runEntries = RunEntries(progress);
        var start = LatestStart(runEntries);
        return start >= 0 && FirstFinishAfter(runEntries, start) is null;
    }

    /// <summary>Derives the run info; <paramref name="lockHeld"/> is false when <c>run.lock</c> is missing or was not tested.</summary>
    public static RunInfo Derive(ImmutableArray<ProgressEntry> progress, bool stopRequested, bool lockHeld)
    {
        var runEntries = RunEntries(progress);
        var start = LatestStart(runEntries);

        DateTimeOffset? startedAt = null;
        DateTimeOffset? finishedAt = null;
        int? maxParallel = null;
        var provider = Provider.Unknown;
        string? agentPath = null;

        if (start >= 0)
        {
            startedAt = runEntries[start].Time;
            finishedAt = FirstFinishAfter(runEntries, start)?.Time;
            maxParallel = ParseMaxParallel(runEntries[start].Message);
            if (start + 1 < runEntries.Count)
                (provider, agentPath) = ParseProvider(runEntries[start + 1].Message);
        }

        var phase = GetPhase(startedAt, finishedAt, hasEntries: !progress.IsDefaultOrEmpty, stopRequested, lockHeld);
        return new RunInfo(phase, startedAt, finishedAt, maxParallel, provider, agentPath, stopRequested);
    }

    private static RunPhase GetPhase(DateTimeOffset? startedAt, DateTimeOffset? finishedAt, bool hasEntries, bool stopRequested, bool lockHeld)
    {
        if (finishedAt is not null)
            return RunPhase.Finished;
        if (startedAt is not null)
        {
            if (!lockHeld)
                return RunPhase.Interrupted;
            return stopRequested ? RunPhase.Stopping : RunPhase.Running;
        }
        return hasEntries ? RunPhase.Planning : RunPhase.NotStarted;
    }

    // The run phase rules look only at entries without a source.
    private static List<ProgressEntry> RunEntries(ImmutableArray<ProgressEntry> progress) =>
        progress.IsDefaultOrEmpty ? [] : progress.Where(entry => entry.Source is null).ToList();

    private static int LatestStart(List<ProgressEntry> runEntries) =>
        runEntries.FindLastIndex(entry => entry.Message.StartsWith(RunStartedPrefix, StringComparison.Ordinal));

    private static ProgressEntry? FirstFinishAfter(List<ProgressEntry> runEntries, int start) =>
        runEntries.Skip(start + 1).FirstOrDefault(entry => entry.Message.StartsWith(RunFinishedPrefix, StringComparison.Ordinal));

    private static int? ParseMaxParallel(string message)
    {
        var match = MaxParallelRegex().Match(message);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static (Provider Provider, string? AgentPath) ParseProvider(string message)
    {
        if (message.StartsWith(ClaudePrefix, StringComparison.Ordinal))
            return (Provider.Claude, message[ClaudePrefix.Length..]);
        if (message.StartsWith(CopilotPrefix, StringComparison.Ordinal))
            return (Provider.Copilot, message[CopilotPrefix.Length..]);
        return (Provider.Unknown, null);
    }

    [GeneratedRegex("max ([0-9]+) in parallel", RegexOptions.CultureInvariant)]
    private static partial Regex MaxParallelRegex();
}

using System.Globalization;
using System.Text.RegularExpressions;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;

namespace OrchDash.Core.Replay;

/// <summary>
/// What one task's progress entries at a replay time say (spec 30.4): its status, mode, attempts, sync runs, start and
/// finish, error and running step. "Latest" means last in file order, so entries of the same second count in file order.
/// </summary>
internal sealed partial class TaskProgress
{
    private const string StartedPrefix = "started (";
    private const string SyncPrefix = "started (sync)";
    private const string DonePrefix = "DONE";
    private const string FailedPrefix = "FAILED";
    private const string FailedErrorPrefix = "FAILED: ";
    private const string PausedPrefix = "paused";
    private const string RequeuedPrefix = "merge conflict with newer integration work";
    private const string FreshMode = "fresh";

    private ProgressEntry? _deciding;   // the latest entry that decides the status
    private ProgressEntry? _latest;     // the latest entry of the task

    public TaskState Status { get; private set; } = TaskState.Pending;
    public string Mode { get; private set; } = FreshMode;
    public int Attempts { get; private set; }
    public int SyncRuns { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Reads the task's entries: those whose source is the task id, in file order.</summary>
    public TaskProgress(IEnumerable<ProgressEntry> entries)
    {
        foreach (var entry in entries)
            Add(entry);
    }

    /// <summary>For Failed: the deciding message after <c>FAILED: </c>, or the whole message; else null.</summary>
    public string? Error =>
        Status != TaskState.Failed || _deciding is null ? null
        : _deciding.Message.StartsWith(FailedErrorPrefix, StringComparison.Ordinal) ? _deciding.Message[FailedErrorPrefix.Length..]
        : _deciding.Message;

    /// <summary>The detail of a running task by the step table, from its latest entry.</summary>
    public string RunningStep()
    {
        var message = _latest?.Message ?? "";
        var attempt = Attempts == 0 ? 1 : Attempts;
        if (message.StartsWith(StartedPrefix, StringComparison.Ordinal))
            return TaskDetails.Starting;
        if (message.StartsWith("setup: ", StringComparison.Ordinal))
            return "setup";
        if (AttemptEntry().Match(message) is { Success: true } match)
            return $"worker (attempt {match.Groups[1].Value})";
        if (message.StartsWith("merge conflicts with", StringComparison.Ordinal))
            return Step("resolver", attempt);
        if (message.StartsWith("acceptance: ", StringComparison.Ordinal))
            return Step("acceptance", attempt);
        if (message.StartsWith("review started", StringComparison.Ordinal))
            return Step("review", attempt);
        if (message.StartsWith("review passed", StringComparison.Ordinal))
            return TaskDetails.IntegrationCheck;
        return "working";
    }

    private void Add(ProgressEntry entry)
    {
        _latest = entry;
        var message = entry.Message;

        if (message.StartsWith(StartedPrefix, StringComparison.Ordinal))
        {
            Decide(entry, TaskState.Running);
            Mode = ModeOf(message);
            StartedAt = entry.Time;
            FinishedAt = null;
            if (message.StartsWith(SyncPrefix, StringComparison.Ordinal))
                SyncRuns++;
        }
        else if (message.StartsWith(DonePrefix, StringComparison.Ordinal) || message.StartsWith(FailedPrefix, StringComparison.Ordinal))
        {
            Decide(entry, message.StartsWith(DonePrefix, StringComparison.Ordinal) ? TaskState.Done : TaskState.Failed);
            if (StartedAt is not null)
                FinishedAt ??= entry.Time;
        }
        else if (message.StartsWith(PausedPrefix, StringComparison.Ordinal) || message.StartsWith(RequeuedPrefix, StringComparison.Ordinal))
        {
            Decide(entry, TaskState.Pending);
        }
        else if (AttemptEntry().Match(message) is { Success: true } match
                 && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var attempts))
        {
            Attempts = attempts;
        }
    }

    private void Decide(ProgressEntry entry, TaskState status)
    {
        _deciding = entry;
        Status = status;
    }

    // The <mode> of "started (<mode>) ..."; the rest of the message when the parenthesis is not closed.
    private static string ModeOf(string message)
    {
        var end = message.IndexOf(')', StartedPrefix.Length);
        return end < 0 ? message[StartedPrefix.Length..] : message[StartedPrefix.Length..end];
    }

    private static string Step(string step, int attempt) =>
        string.Create(CultureInfo.InvariantCulture, $"{step} (attempt {attempt})");

    [GeneratedRegex(@"\Aattempt ([0-9]+)/", RegexOptions.CultureInvariant)]
    private static partial Regex AttemptEntry();
}

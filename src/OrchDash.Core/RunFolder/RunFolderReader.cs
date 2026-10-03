using System.Collections.Immutable;
using System.Security;
using System.Text;
using OrchDash.Core.Model;

namespace OrchDash.Core.RunFolder;

/// <summary>
/// Reads the <c>.orchestrator</c> folder of a run into one <see cref="RunFolderData"/> (spec 2.1-2.9, 4.5).
/// Every file is opened for reading only, sharing read, write and delete, and nothing is written.
/// </summary>
public sealed class RunFolderReader : IRunFolderReader
{
    private const string ProgressFile = "progress.md";
    private const string LockFile = "run.lock";
    private const string StopRequestedFile = "stop-requested";
    private const string LogsFolder = "logs";

    // Win32 error codes in the low word of IOException.HResult.
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    /// <summary>Never throws; a missing <paramref name="runDir"/> gives an empty, not started run.</summary>
    public RunFolderData Read(string runDir, DateTimeOffset now)
    {
        var problems = new List<string>();

        var progressText = ReadText(runDir, ProgressFile, problems);
        var progress = progressText is null ? [] : ProgressParser.Parse(progressText);
        var stopRequested = File.Exists(Path.Combine(runDir, StopRequestedFile));
        var lockHeld = RunInfoRules.NeedsLockTest(progress) && IsLockHeld(runDir, problems);
        var run = RunInfoRules.Derive(progress, stopRequested, lockHeld);

        var logs = new LogsListing();
        var sessions = SessionFileScanner.Scan(Path.Combine(runDir, LogsFolder), problems, logs);

        var plan = ReadText(runDir, PlanJson.FileName, problems) is { } planText ? PlanJson.Parse(planText, problems) : null;
        var states = ReadText(runDir, StateJson.FileName, problems) is { } stateText ? StateJson.Parse(stateText, problems) : null;
        var tasks = plan is null ? [] : BuildTasks(plan.Value.Tasks, states ?? new(), run.MaxParallel, logs);

        return new RunFolderData(run, plan?.Plan, tasks, progress, sessions, [.. problems]);
    }

    private static ImmutableArray<TaskView> BuildTasks(ImmutableArray<TaskDefinition> definitions,
        Dictionary<string, TaskStateEntry> states, int? maxParallel, LogsListing logs)
    {
        var graph = new TaskGraph(definitions);
        var entries = definitions.Select(task => states.GetValueOrDefault(task.Id) ?? TaskStateEntry.Default).ToArray();
        var statuses = entries.Select(entry => entry.Status).ToArray();
        for (var i = 0; i < statuses.Length; i++)
        {
            if (statuses[i] == TaskState.Pending && graph.AnyAncestor(i, dep => entries[dep].Status == TaskState.Failed))
                statuses[i] = TaskState.Blocked;
        }

        var running = Enumerable.Range(0, definitions.Length).Where(i => statuses[i] == TaskState.Running).ToArray();
        var views = ImmutableArray.CreateBuilder<TaskView>(definitions.Length);
        for (var i = 0; i < definitions.Length; i++)
        {
            var task = definitions[i];
            var entry = entries[i];
            var detail = statuses[i] switch
            {
                TaskState.Done => TaskDetails.Done(entry.CostUsd, entry.Attempts),
                TaskState.Failed => TaskDetails.Failed(entry.Error),
                TaskState.Blocked => TaskDetails.Blocked,
                TaskState.Running => TaskDetails.Running(task.Id, logs),
                _ => PendingDetail(i),
            };

            views.Add(new TaskView(
                task.Id, task.Title, task.Prompt, task.Deps, task.Owns, task.Acceptance, task.Model,
                graph.Wave(i), graph.Dependents(i),
                statuses[i], entry.Mode, entry.Attempts, entry.SyncRuns, entry.SpecRejections, entry.CostUsd,
                entry.SessionId, entry.Summary, entry.Notes, entry.Error, entry.Feedback,
                entry.StartedAt, entry.FinishedAt, entry.MergedSha, detail));
        }
        return views.MoveToImmutable();

        string PendingDetail(int task)
        {
            var waitingFor = graph.Deps(task).Where(dep => statuses[dep] != TaskState.Done).ToArray();
            if (waitingFor.Length > 0)
                return TaskDetails.WaitingForDeps(waitingFor.Select(dep => definitions[dep].Id));

            var overlap = running.Where(other => TaskDetails.OwnsOverlap(definitions[task].Owns, definitions[other].Owns)).ToArray();
            if (overlap.Length > 0)
                return TaskDetails.WaitingForOwns(definitions[overlap[0]].Id);

            return maxParallel is { } max && running.Length >= max ? TaskDetails.FreeSlot : TaskDetails.Ready;
        }
    }

    // The whole text of a file of the run folder, without a BOM. Null when the file is missing, and also,
    // with one problem line that names the file, when it cannot be read.
    private static string? ReadText(string runDir, string fileName, ICollection<string> problems)
    {
        try
        {
            using var stream = Open(Path.Combine(runDir, fileName));
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception e) when (IsAccessError(e))
        {
            problems.Add($"{fileName}: {e.Message}");
            return null;
        }
    }

    // Opens run.lock and closes it at once (spec 2.9): a sharing violation means the orchestrator holds it.
    private static bool IsLockHeld(string runDir, ICollection<string> problems)
    {
        try
        {
            using var stream = Open(Path.Combine(runDir, LockFile));
            return false;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException e) when ((e.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation)
        {
            return true;
        }
        catch (Exception e) when (IsAccessError(e))
        {
            problems.Add($"{LockFile}: {e.Message}");
            return false;
        }
    }

    // The one way this reader opens a file (spec N.1).
    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static bool IsAccessError(Exception e) =>
        e is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}

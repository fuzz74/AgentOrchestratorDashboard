using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrchDash.Core.Model;

namespace OrchDash.Core.CommandLogs;

/// <summary>
/// Sets <see cref="CommandLog.Command"/>, <see cref="CommandLog.Outcome"/> and <see cref="CommandLog.ExitCode"/>
/// by the command outcome table (spec 4.3, 22.5). Pure; never throws.
/// </summary>
public static partial class CommandRules
{
    private const string BootstrapOwner = "bootstrap";   // the progress source of the bootstrap, the owner of its logs
    private const string SetupSetting = "setup";
    private const string IntegrationCheckSetting = "integrationCheck";
    private const string CommandPrefix = "> ";

    private static readonly string[] SetupPassed = ["attempt "];
    private static readonly string[] SetupFailed = ["FAILED: Setup command failed"];
    private static readonly string[] AcceptancePassed =
        ["review started", "review passed", "DONE", "merge conflict", "paused",
         "FAILED: integration check failed", "FAILED: merge kept conflicting"];
    private static readonly string[] IntegrationPassed = ["DONE and merged"];
    private static readonly string[] IntegrationFailed = ["FAILED: integration check failed"];
    private static readonly string[] BootstrapPassed = ["Skeleton committed"];
    private static readonly string[] BootstrapFailed = ["FAILED"];

    /// <summary>Returns <paramref name="logs"/> in the same order with the command, outcome and exit code set.</summary>
    public static ImmutableArray<CommandLog> Resolve(ImmutableArray<CommandLog> logs, ImmutableArray<ProgressEntry> progress,
        PlanInfo? plan, ImmutableArray<TaskView> tasks, RunPhase phase)
    {
        if (logs.IsDefaultOrEmpty)
            return [];
        if (progress.IsDefault)
            progress = [];

        var tasksById = new Dictionary<string, TaskView>(StringComparer.Ordinal);
        if (!tasks.IsDefault)
        {
            foreach (var task in tasks)
                tasksById.TryAdd(task.Id, task);
        }

        var newest = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var log in logs)
        {
            if (Owner(log) is { } owner && (!newest.TryGetValue(owner, out var time) || log.WrittenAt > time))
                newest[owner] = log.WrittenAt;
        }

        var setup = Setting(plan, SetupSetting);
        var integrationCheck = Setting(plan, IntegrationCheckSetting);

        var resolved = ImmutableArray.CreateBuilder<CommandLog>(logs.Length);
        foreach (var log in logs)
        {
            var task = log.TaskId is null ? null : tasksById.GetValueOrDefault(log.TaskId);
            var command = log.Kind switch
            {
                CommandKind.Setup or CommandKind.IntegrationSetup => setup,
                CommandKind.Acceptance => task?.Acceptance,
                CommandKind.IntegrationCheck => integrationCheck,
                _ => FirstLineCommand(log.Text),
            };

            var owner = Owner(log);
            var (outcome, exitCode) = Decide(log, owner, progress);
            if (log.Kind == CommandKind.IntegrationSetup && outcome == CommandOutcome.Failed && CheckRanAfter(log, logs))
                outcome = CommandOutcome.Passed;
            else if (outcome == CommandOutcome.Unknown && owner is not null && log.WrittenAt >= newest[owner]
                     && IsRunning(log, task, phase))
                outcome = CommandOutcome.Running;

            resolved.Add(log with { Command = command, Outcome = outcome, ExitCode = exitCode });
        }
        return resolved.MoveToImmutable();
    }

    private static bool IsBootstrap(CommandLog log) => log.Kind is CommandKind.BootstrapSetup or CommandKind.BootstrapCheck;

    private static string? Owner(CommandLog log) => IsBootstrap(log) ? BootstrapOwner : log.TaskId;

    private static bool IsRunning(CommandLog log, TaskView? task, RunPhase phase) =>
        IsBootstrap(log) ? phase == RunPhase.Planning : task?.Status == TaskState.Running;

    // The outcome the decider gives: the first entry of the owner, not earlier than WrittenAt cut to whole seconds,
    // that starts with one of the kind's texts. Unknown without a decider.
    private static (CommandOutcome Outcome, int? ExitCode) Decide(CommandLog log, string? owner, ImmutableArray<ProgressEntry> progress)
    {
        if (owner is null)
            return (CommandOutcome.Unknown, null);

        var written = log.WrittenAt;
        var from = new DateTimeOffset(written.Ticks - written.Ticks % TimeSpan.TicksPerSecond, written.Offset);
        var (passed, failed) = Texts(log.Kind);

        foreach (var entry in progress)
        {
            if (!string.Equals(entry.Source, owner, StringComparison.Ordinal) || entry.Time < from)
                continue;

            var message = entry.Message;
            if (StartsWithAny(message, passed))
                return (CommandOutcome.Passed, null);
            if (StartsWithAny(message, failed))
                return (CommandOutcome.Failed, null);
            if (log.Kind == CommandKind.Acceptance && AcceptanceFailed().Match(message) is { Success: true } match)
            {
                var exit = int.TryParse(match.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
                    ? n
                    : (int?)null;
                return (CommandOutcome.Failed, exit);
            }
        }
        return (CommandOutcome.Unknown, null);
    }

    private static (string[] Passed, string[] Failed) Texts(CommandKind kind) => kind switch
    {
        CommandKind.Setup => (SetupPassed, SetupFailed),
        CommandKind.Acceptance => (AcceptancePassed, []),
        CommandKind.IntegrationSetup or CommandKind.IntegrationCheck => (IntegrationPassed, IntegrationFailed),
        _ => (BootstrapPassed, BootstrapFailed),
    };

    private static bool StartsWithAny(string message, string[] texts) =>
        texts.Any(text => message.StartsWith(text, StringComparison.Ordinal));

    // The task's IntegrationCheck log is not older than this IntegrationSetup log: the check ran, so the setup passed.
    private static bool CheckRanAfter(CommandLog setup, ImmutableArray<CommandLog> logs) =>
        logs.Any(log => log.Kind == CommandKind.IntegrationCheck
                        && string.Equals(log.TaskId, setup.TaskId, StringComparison.Ordinal)
                        && log.WrittenAt >= setup.WrittenAt);

    // The first line of the text without its "> ", or null when it does not start with "> ".
    private static string? FirstLineCommand(string text)
    {
        var end = text.IndexOf('\n');
        var line = (end < 0 ? text : text[..end]).TrimEnd('\r');
        return line.StartsWith(CommandPrefix, StringComparison.Ordinal) ? line[CommandPrefix.Length..] : null;
    }

    // A setting is compact JSON: a JSON string is unquoted; anything else, or a missing setting, gives null.
    private static string? Setting(PlanInfo? plan, string name)
    {
        if (plan?.Settings is not { } settings || !settings.TryGetValue(name, out var json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.String ? document.RootElement.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\Aacceptance failed \(exit (-?[0-9]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex AcceptanceFailed();
}

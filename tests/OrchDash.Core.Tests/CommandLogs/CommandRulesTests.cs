using System.Collections.Immutable;
using OrchDash.Core.CommandLogs;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Core.Tests.CommandLogs;

public sealed class CommandRulesTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(2);
    private static readonly DateTimeOffset Written = new(2026, 10, 3, 12, 0, 10, 600, Offset);   // cut: 12:00:10
    private static readonly PlanInfo Plan = new(null, "main", "orch/integration",
        ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [
            KeyValuePair.Create("setup", "\"dotnet restore X\""),
            KeyValuePair.Create("integrationCheck", "\"dotnet build X && dotnet test X\""),
        ]));

    [Theory]
    [InlineData(CommandKind.Setup, "attempt 1 started", CommandOutcome.Passed)]
    [InlineData(CommandKind.Setup, "FAILED: Setup command failed (exit 1)", CommandOutcome.Failed)]
    [InlineData(CommandKind.Acceptance, "review started", CommandOutcome.Passed)]
    [InlineData(CommandKind.Acceptance, "review passed", CommandOutcome.Passed)]
    [InlineData(CommandKind.Acceptance, "DONE: 0.12 USD", CommandOutcome.Passed)]
    [InlineData(CommandKind.Acceptance, "merge conflict, resolving", CommandOutcome.Passed)]
    [InlineData(CommandKind.Acceptance, "paused: waiting for a slot", CommandOutcome.Passed)]
    [InlineData(CommandKind.Acceptance, "FAILED: integration check failed", CommandOutcome.Passed)]
    [InlineData(CommandKind.Acceptance, "FAILED: merge kept conflicting", CommandOutcome.Passed)]
    [InlineData(CommandKind.Acceptance, "acceptance failed (exit 1)", CommandOutcome.Failed)]
    [InlineData(CommandKind.IntegrationSetup, "DONE and merged (abc1234)", CommandOutcome.Passed)]
    [InlineData(CommandKind.IntegrationSetup, "FAILED: integration check failed", CommandOutcome.Failed)]
    [InlineData(CommandKind.IntegrationCheck, "DONE and merged (abc1234)", CommandOutcome.Passed)]
    [InlineData(CommandKind.IntegrationCheck, "FAILED: integration check failed", CommandOutcome.Failed)]
    [InlineData(CommandKind.BootstrapSetup, "Skeleton committed: abc1234", CommandOutcome.Passed)]
    [InlineData(CommandKind.BootstrapSetup, "FAILED: setup did not pass", CommandOutcome.Failed)]
    [InlineData(CommandKind.BootstrapCheck, "Skeleton committed: abc1234", CommandOutcome.Passed)]
    [InlineData(CommandKind.BootstrapCheck, "FAILED: integration check did not pass", CommandOutcome.Failed)]
    public void The_decider_gives_the_outcome_of_its_row(CommandKind kind, string message, CommandOutcome expected)
    {
        var log = Log(kind);
        var source = log.TaskId ?? "bootstrap";

        var resolved = Resolve([log], [Entry(Written, source, message)]);

        Assert.Equal(expected, resolved.Outcome);
    }

    [Theory]
    [InlineData(CommandKind.Setup, "DONE: 0.12 USD")]
    [InlineData(CommandKind.Acceptance, "attempt 2 started")]
    [InlineData(CommandKind.Acceptance, "acceptance failed")]
    [InlineData(CommandKind.IntegrationCheck, "FAILED: Setup command failed")]
    [InlineData(CommandKind.BootstrapSetup, "DONE and merged")]
    public void A_message_of_another_kind_decides_nothing(CommandKind kind, string message)
    {
        var log = Log(kind);

        var resolved = Resolve([log], [Entry(Written, log.TaskId ?? "bootstrap", message)]);

        Assert.Equal(CommandOutcome.Unknown, resolved.Outcome);
    }

    [Theory]
    [InlineData("acceptance failed (exit 1)", 1)]
    [InlineData("acceptance failed (exit 137) after 3 attempts", 137)]
    [InlineData("acceptance failed (exit -1)", -1)]
    public void A_failed_acceptance_gives_the_exit_code(string message, int exitCode)
    {
        var resolved = Resolve([Log(CommandKind.Acceptance)], [Entry(Written, "t1", message)]);

        Assert.Equal((CommandOutcome.Failed, (int?)exitCode), (resolved.Outcome, resolved.ExitCode));
    }

    [Fact]
    public void A_passed_decider_has_no_exit_code()
    {
        var resolved = Resolve([Log(CommandKind.Acceptance)], [Entry(Written, "t1", "review passed")]);

        Assert.Null(resolved.ExitCode);
    }

    [Fact]
    public void The_first_decider_in_file_order_counts()
    {
        var resolved = Resolve([Log(CommandKind.Acceptance)],
        [
            Entry(Written, "t1", "worker: 12 tool calls"),
            Entry(Written.AddSeconds(5), "t1", "acceptance failed (exit 2)"),
            Entry(Written.AddSeconds(1), "t1", "review passed"),
        ]);

        Assert.Equal((CommandOutcome.Failed, (int?)2), (resolved.Outcome, resolved.ExitCode));
    }

    [Fact]
    public void Written_at_is_cut_to_whole_seconds()
    {
        var sameSecond = Resolve([Log(CommandKind.Setup)], [Entry(Written.AddMilliseconds(-600), "t1", "attempt 1 started")]);
        var secondBefore = Resolve([Log(CommandKind.Setup)], [Entry(Written.AddMilliseconds(-601), "t1", "attempt 1 started")]);

        Assert.Equal(CommandOutcome.Passed, sameSecond.Outcome);
        Assert.Equal(CommandOutcome.Unknown, secondBefore.Outcome);
    }

    [Fact]
    public void The_time_is_compared_across_offsets()
    {
        var utcEntry = Entry(Written.AddMilliseconds(-600).ToUniversalTime(), "t1", "attempt 1 started");

        Assert.Equal(CommandOutcome.Passed, Resolve([Log(CommandKind.Setup)], [utcEntry]).Outcome);
    }

    [Fact]
    public void An_entry_of_another_source_is_ignored()
    {
        var task = Resolve([Log(CommandKind.Setup)],
            [Entry(Written, "t2", "FAILED: Setup command failed"), Entry(Written, null, "attempt 1 started")]);
        var bootstrap = Resolve([Log(CommandKind.BootstrapSetup)], [Entry(Written, null, "Skeleton committed")]);

        Assert.Equal(CommandOutcome.Unknown, task.Outcome);
        Assert.Equal(CommandOutcome.Unknown, bootstrap.Outcome);
    }

    [Fact]
    public void A_failed_integration_setup_passed_when_the_check_is_not_older()
    {
        var setup = Log(CommandKind.IntegrationSetup);
        var failed = Entry(Written.AddSeconds(30), "t1", "FAILED: integration check failed");

        var sameTime = Resolve([Log(CommandKind.IntegrationCheck), setup], [failed], index: 1);
        var later = Resolve([Log(CommandKind.IntegrationCheck, Written.AddSeconds(5)), setup], [failed], index: 1);
        var older = Resolve([Log(CommandKind.IntegrationCheck, Written.AddMilliseconds(-1)), setup], [failed], index: 1);
        var otherTask = Resolve([Log(CommandKind.IntegrationCheck, Written.AddSeconds(5), taskId: "t2"), setup], [failed], index: 1);
        var none = Resolve([setup], [failed]);

        Assert.Equal(CommandOutcome.Passed, sameTime.Outcome);
        Assert.Equal(CommandOutcome.Passed, later.Outcome);
        Assert.Equal(CommandOutcome.Failed, older.Outcome);
        Assert.Equal(CommandOutcome.Failed, otherTask.Outcome);
        Assert.Equal(CommandOutcome.Failed, none.Outcome);
    }

    [Fact]
    public void Without_a_decider_the_newest_log_of_a_running_task_is_running()
    {
        ImmutableArray<CommandLog> logs =
            [Log(CommandKind.Acceptance, Written.AddSeconds(60)), Log(CommandKind.Setup), Log(CommandKind.Setup, taskId: "t2")];
        ImmutableArray<TaskView> tasks = [Task("t1", TaskState.Running), Task("t2", TaskState.Done)];

        var resolved = CommandRules.Resolve(logs, [], Plan, tasks, RunPhase.Running);

        Assert.Equal([CommandOutcome.Running, CommandOutcome.Unknown, CommandOutcome.Unknown], resolved.Select(log => log.Outcome));
    }

    [Fact]
    public void A_decider_wins_over_running()
    {
        var resolved = Resolve([Log(CommandKind.Setup)], [Entry(Written, "t1", "attempt 1 started")]);

        Assert.Equal(CommandOutcome.Passed, resolved.Outcome);
    }

    [Fact]
    public void Without_a_decider_the_newest_bootstrap_log_is_running_while_planning()
    {
        ImmutableArray<CommandLog> logs = [Log(CommandKind.BootstrapCheck, Written.AddSeconds(60)), Log(CommandKind.BootstrapSetup)];

        var planning = CommandRules.Resolve(logs, [], Plan, [], RunPhase.Planning);
        var running = CommandRules.Resolve(logs, [], Plan, [], RunPhase.Running);

        Assert.Equal([CommandOutcome.Running, CommandOutcome.Unknown], planning.Select(log => log.Outcome));
        Assert.Equal([CommandOutcome.Unknown, CommandOutcome.Unknown], running.Select(log => log.Outcome));
    }

    [Fact]
    public void A_log_of_an_unknown_task_is_unknown()
    {
        var resolved = Resolve([Log(CommandKind.Acceptance, taskId: "gone")], []);

        Assert.Equal((CommandOutcome.Unknown, (string?)null), (resolved.Outcome, resolved.Command));
    }

    [Fact]
    public void Commands_come_from_the_settings_the_task_and_the_bootstrap_text()
    {
        ImmutableArray<CommandLog> logs =
        [
            Log(CommandKind.Setup),
            Log(CommandKind.Acceptance),
            Log(CommandKind.IntegrationSetup),
            Log(CommandKind.IntegrationCheck),
            Log(CommandKind.BootstrapSetup, text: "> dotnet restore X\r\n  Restored X\n"),
            Log(CommandKind.BootstrapCheck, text: "Build started\n> dotnet build X\n"),
        ];

        var resolved = CommandRules.Resolve(logs, [], Plan, [Task("t1", TaskState.Done)], RunPhase.Finished);

        Assert.Equal(
            ["dotnet restore X", "dotnet test t1", "dotnet restore X", "dotnet build X && dotnet test X", "dotnet restore X", null],
            resolved.Select(log => log.Command));
    }

    [Fact]
    public void A_bootstrap_text_of_one_line_gives_its_command()
    {
        var resolved = Resolve([Log(CommandKind.BootstrapSetup, text: "> dotnet restore Y")], []);

        Assert.Equal("dotnet restore Y", resolved.Command);
    }

    [Theory]
    [InlineData("\"dotnet restore X\"", "dotnet restore X")]
    [InlineData("\"a \\\"quoted\\\" path\"", "a \"quoted\" path")]
    [InlineData("true", null)]
    [InlineData("3", null)]
    [InlineData("null", null)]
    [InlineData("[\"dotnet restore X\"]", null)]
    [InlineData("not json", null)]
    public void A_setting_that_is_a_json_string_is_unquoted(string json, string? expected)
    {
        var plan = Plan with { Settings = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create("setup", json)]) };

        var resolved = CommandRules.Resolve([Log(CommandKind.Setup)], [], plan, [Task("t1", TaskState.Done)], RunPhase.Finished);

        Assert.Equal(expected, resolved[0].Command);
    }

    [Fact]
    public void A_missing_setting_or_plan_gives_no_command()
    {
        var noSettings = Plan with { Settings = ImmutableSortedDictionary<string, string>.Empty };
        ImmutableArray<CommandLog> logs = [Log(CommandKind.Setup), Log(CommandKind.IntegrationCheck)];

        var missing = CommandRules.Resolve(logs, [], noSettings, [], RunPhase.Finished);
        var noPlan = CommandRules.Resolve(logs, [], null, [], RunPhase.Finished);

        Assert.All(missing, log => Assert.Null(log.Command));
        Assert.All(noPlan, log => Assert.Null(log.Command));
    }

    [Fact]
    public void Logs_keep_their_order_and_other_members()
    {
        ImmutableArray<CommandLog> logs =
            [Log(CommandKind.Setup, Written.AddSeconds(-5)), Log(CommandKind.BootstrapSetup), Log(CommandKind.Acceptance, Written.AddSeconds(5))];

        var resolved = CommandRules.Resolve(logs, [], Plan, [], RunPhase.Finished);

        Assert.Equal(logs.Select(log => log.Key), resolved.Select(log => log.Key));
        Assert.Equal(logs[0] with { Command = "dotnet restore X" }, resolved[0]);
        Assert.Empty(CommandRules.Resolve([], [], Plan, [], RunPhase.Finished));
    }

    private static CommandLog Resolve(ImmutableArray<CommandLog> logs, ImmutableArray<ProgressEntry> progress, int index = 0) =>
        CommandRules.Resolve(logs, progress, Plan, [Task("t1", TaskState.Done), Task("t2", TaskState.Done)], RunPhase.Finished)[index];

    private static ProgressEntry Entry(DateTimeOffset time, string? source, string message) =>
        new(time, source, message, ProgressKind.Info);

    private static CommandLog Log(CommandKind kind, DateTimeOffset? writtenAt = null, string taskId = "t1", string text = "output\n")
    {
        var bootstrap = kind is CommandKind.BootstrapSetup or CommandKind.BootstrapCheck;
        var key = kind switch
        {
            CommandKind.Setup => $"{taskId}/20261003-120000/setup.log",
            CommandKind.Acceptance => $"{taskId}/20261003-120000/attempt-1-acceptance.log",
            CommandKind.IntegrationSetup => $"{taskId}-integration-setup.log",
            CommandKind.IntegrationCheck => $"{taskId}-integration-check.log",
            CommandKind.BootstrapSetup => "bootstrap-20261003-115500/attempt-1-setup.log",
            _ => "bootstrap-20261003-115500/attempt-1-integration-check.log",
        };
        var path = @"C:\Work\Repo\.orchestrator\logs\" + key.Replace('/', '\\');
        return new CommandLog(kind, bootstrap ? null : taskId, null, null, key, path, path + ".stderr",
            writtenAt ?? Written, text.Length, text, "", null, CommandOutcome.Unknown, null);
    }

    private static TaskView Task(string id, TaskState status) => new(
        id, "Title of " + id, "prompt", [], [], "dotnet test " + id, null, 1, 0,
        status, "normal", 1, 0, 0, 0, null, null, null, null, null, null, null, null, "");
}

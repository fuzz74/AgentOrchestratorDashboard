using System.Collections.Immutable;
using OrchDash.Core.CommandLogs;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using OrchDash.Core.Tests.Fixtures;
using Xunit;

namespace OrchDash.Core.Tests.CommandLogs;

public sealed class CommandLogFixtureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Claude_run_has_six_logs_without_stderr_files()
    {
        var data = new CommandLogReader().Read(FixturePaths.ClaudeRunDir);

        Assert.Empty(data.Problems);
        (string, CommandKind, string?, string?, int?)[] expected =
            [
                ("audio-synth-integration-check.log", CommandKind.IntegrationCheck, "audio-synth", null, null),
                ("audio-synth-integration-setup.log", CommandKind.IntegrationSetup, "audio-synth", null, null),
                ("audio-synth/20261001-104634/attempt-1-acceptance.log", CommandKind.Acceptance, "audio-synth", "20261001-104634", 1),
                ("audio-synth/20261001-104634/setup.log", CommandKind.Setup, "audio-synth", "20261001-104634", null),
                ("bootstrap-20261001-100433/attempt-1-integration-check.log", CommandKind.BootstrapCheck, null, "bootstrap-20261001-100433", 1),
                ("bootstrap-20261001-100433/attempt-1-setup.log", CommandKind.BootstrapSetup, null, "bootstrap-20261001-100433", 1),
            ];
        Assert.Equal(expected, Facts(data.Logs));
        Assert.All(data.Logs, log =>
        {
            Assert.False(File.Exists(log.StderrPath));
            Assert.Equal("", log.StderrText);
        });
        AssertCommon(FixturePaths.ClaudeRunDir, data.Logs);
    }

    [Fact]
    public void Copilot_run_has_eleven_logs_with_empty_stderr_files()
    {
        var data = new CommandLogReader().Read(FixturePaths.CopilotRunDir);

        Assert.Empty(data.Problems);
        (string, CommandKind, string?, string?, int?)[] expected =
            [
                ("bootstrap-20261003-110028/attempt-1-integration-check.log", CommandKind.BootstrapCheck, null, "bootstrap-20261003-110028", 1),
                ("bootstrap-20261003-110028/attempt-1-setup.log", CommandKind.BootstrapSetup, null, "bootstrap-20261003-110028", 1),
                ("core-integration-check.log", CommandKind.IntegrationCheck, "core", null, null),
                ("core-integration-setup.log", CommandKind.IntegrationSetup, "core", null, null),
                ("core/20261003-113444/attempt-1-acceptance.log", CommandKind.Acceptance, "core", "20261003-113444", 1),
                ("core/20261003-113444/setup.log", CommandKind.Setup, "core", "20261003-113444", null),
                ("count-integration-check.log", CommandKind.IntegrationCheck, "count", null, null),
                ("count-integration-setup.log", CommandKind.IntegrationSetup, "count", null, null),
                ("count/20261003-114955/attempt-1-acceptance.log", CommandKind.Acceptance, "count", "20261003-114955", 1),
                ("count/20261003-115046/attempt-1-acceptance.log", CommandKind.Acceptance, "count", "20261003-115046", 1),
                ("count/20261003-115126/attempt-1-acceptance.log", CommandKind.Acceptance, "count", "20261003-115126", 1),
            ];
        Assert.Equal(expected, Facts(data.Logs));
        Assert.All(data.Logs, log =>
        {
            Assert.Equal(0, new FileInfo(log.StderrPath).Length);
            Assert.Equal("", log.StderrText);
        });
        AssertCommon(FixturePaths.CopilotRunDir, data.Logs);
    }

    [Fact]
    public void Claude_run_commands_come_from_the_plan_the_tasks_and_the_bootstrap_texts()
    {
        var logs = Resolve(FixturePaths.ClaudeRunDir, out var tasks);

        Assert.Equal("dotnet restore AnsiDemo.slnx", Single(logs, CommandKind.BootstrapSetup).Command);
        Assert.Equal("dotnet build AnsiDemo.slnx -warnaserror && dotnet test AnsiDemo.slnx --no-build",
            Single(logs, CommandKind.BootstrapCheck).Command);
        AssertTaskCommands(logs, tasks, "dotnet restore AnsiDemo.slnx",
            "dotnet build AnsiDemo.slnx -warnaserror && dotnet test AnsiDemo.slnx --no-build");
    }

    [Fact]
    public void Copilot_run_commands_come_from_the_plan_and_the_tasks_and_its_bootstrap_texts_have_none()
    {
        var logs = Resolve(FixturePaths.CopilotRunDir, out var tasks);

        Assert.Null(Single(logs, CommandKind.BootstrapSetup).Command);
        Assert.Null(Single(logs, CommandKind.BootstrapCheck).Command);
        AssertTaskCommands(logs, tasks, "dotnet restore TextKit.slnx",
            "dotnet build TextKit.slnx -warnaserror && dotnet test TextKit.slnx --no-build");
    }

    private static ImmutableArray<CommandLog> Resolve(string runDir, out ImmutableArray<TaskView> tasks)
    {
        var data = new RunFolderReader().Read(runDir, Now);
        var read = new CommandLogReader().Read(runDir);
        tasks = data.Tasks;
        var logs = CommandRules.Resolve(read.Logs, data.Progress, data.Plan, data.Tasks, data.Run.Phase);
        Assert.Equal(read.Logs.Select(log => log.Key), logs.Select(log => log.Key));
        return logs;
    }

    private static CommandLog Single(ImmutableArray<CommandLog> logs, CommandKind kind) => Assert.Single(logs, log => log.Kind == kind);

    private static void AssertTaskCommands(ImmutableArray<CommandLog> logs, ImmutableArray<TaskView> tasks, string setup,
        string integrationCheck)
    {
        foreach (var log in logs.Where(log => log.TaskId is not null))
        {
            var expected = log.Kind switch
            {
                CommandKind.Setup or CommandKind.IntegrationSetup => setup,
                CommandKind.IntegrationCheck => integrationCheck,
                _ => tasks.Single(task => task.Id == log.TaskId).Acceptance,
            };
            Assert.NotNull(expected);
            Assert.Equal(expected, log.Command);
        }
    }

    private static void AssertCommon(string runDir, ImmutableArray<CommandLog> logs)
    {
        Assert.Equal(
            logs.OrderByDescending(log => log.WrittenAt).ThenBy(log => log.Key, StringComparer.Ordinal).Select(log => log.Key),
            logs.Select(log => log.Key));
        Assert.All(logs, log =>
        {
            Assert.Equal(Path.Combine(runDir, "logs", log.Key.Replace('/', '\\')), log.Path);
            Assert.Equal(log.Path + ".stderr", log.StderrPath);
            Assert.NotEmpty(log.Text);
            Assert.Equal(new FileInfo(log.Path).Length, log.Length);
            Assert.Equal((null, CommandOutcome.Unknown, null), (log.Command, log.Outcome, log.ExitCode));
        });
    }

    private static IEnumerable<(string, CommandKind, string?, string?, int?)> Facts(ImmutableArray<CommandLog> logs) =>
        logs.Select(log => (log.Key, log.Kind, log.TaskId, log.StartFolder, log.Attempt)).OrderBy(fact => fact.Key, StringComparer.Ordinal);
}

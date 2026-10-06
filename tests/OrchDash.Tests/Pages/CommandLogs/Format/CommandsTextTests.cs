using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.CommandLogs.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.CommandLogs.Format;

public sealed class CommandsTextTests
{
    private const string GammaAttempt3Key = "gamma/20261003-120005/attempt-3-acceptance.log";

    private readonly ImmutableArray<CommandLog> _logs = SampleRun.CreateInsight().Commands;

    private CommandLog GammaAttempt3 => _logs[0];
    private CommandLog GammaAttempt2 => _logs[1];
    private CommandLog BetaSetup => _logs[2];
    private CommandLog AlphaIntegrationCheck => _logs[3];
    private CommandLog BootstrapSetup => _logs[10];

    [Theory]
    [InlineData(CommandKind.Setup, "setup")]
    [InlineData(CommandKind.Acceptance, "acceptance")]
    [InlineData(CommandKind.IntegrationSetup, "integration setup")]
    [InlineData(CommandKind.IntegrationCheck, "integration check")]
    [InlineData(CommandKind.BootstrapSetup, "bootstrap setup")]
    [InlineData(CommandKind.BootstrapCheck, "bootstrap check")]
    public void Kind_words(CommandKind kind, string word) => Assert.Equal(word, CommandsText.KindWord(kind));

    [Theory]
    [InlineData(CommandOutcome.Passed, "✔", "success", "passed")]
    [InlineData(CommandOutcome.Failed, "✖", "error", "failed")]
    [InlineData(CommandOutcome.Running, "▶", "primary", "running")]
    [InlineData(CommandOutcome.Unknown, "·", "muted", "unknown")]
    public void Outcome_icons_colours_and_words(CommandOutcome outcome, string icon, string color, string word)
    {
        Assert.Equal(icon, CommandsText.OutcomeIcon(outcome));
        Assert.Equal(color, CommandsText.OutcomeColor(outcome));
        Assert.Equal(word, CommandsText.OutcomeWord(outcome));
    }

    [Fact]
    public void The_owner_is_the_task_or_the_bootstrap()
    {
        Assert.Equal("gamma", CommandsText.Owner(GammaAttempt3));
        Assert.Equal("bootstrap", CommandsText.Owner(BootstrapSetup));
    }

    [Fact]
    public void Rows_show_outcome_time_owner_kind_attempt_and_size()
    {
        Assert.Equal("[error]✖[/] 12:19:50 gamma acceptance #3 406 B", CommandsText.Row(GammaAttempt3));
        Assert.Equal("[success]✔[/] 12:10:05 beta setup 202 B", CommandsText.Row(BetaSetup));
        Assert.Equal($"[success]✔[/] 12:09:28 alpha integration check {Look.Bytes(AlphaIntegrationCheck.Length)}",
            CommandsText.Row(AlphaIntegrationCheck));
        Assert.Equal($"[success]✔[/] 11:55:30 bootstrap bootstrap setup #1 {Look.Bytes(BootstrapSetup.Length)}",
            CommandsText.Row(BootstrapSetup));
    }

    [Fact]
    public void Rows_of_running_and_unknown_logs_and_kilobytes()
    {
        var running = BetaSetup with { Outcome = CommandOutcome.Running, Length = 47_400 };
        var unknown = BetaSetup with { Outcome = CommandOutcome.Unknown };

        Assert.Equal("[primary]▶[/] 12:10:05 beta setup 47.4 kB", CommandsText.Row(running));
        Assert.Equal("[muted]·[/] 12:10:05 beta setup 202 B", CommandsText.Row(unknown));
    }

    [Fact]
    public void Header_lines_show_command_outcome_with_exit_code_key_and_size()
    {
        Assert.Equal(
        [
            "[bold]dotnet test tests/Gamma[/]",
            "[error]failed (exit 1)[/]",
            GammaAttempt3Key,
            "406 B",
        ], CommandsText.HeaderLines(GammaAttempt3));
        Assert.Equal(
        [
            "[bold]dotnet restore Sample.slnx[/]",
            "[success]passed[/]",
            "beta/20261003-121000/setup.log",
            "202 B",
        ], CommandsText.HeaderLines(BetaSetup));
    }

    [Fact]
    public void Header_of_a_log_without_command()
    {
        var log = BetaSetup with { Command = null, Outcome = CommandOutcome.Unknown };

        Assert.Equal("[bold]command unknown[/]", CommandsText.HeaderLines(log)[0]);
        Assert.Equal("[muted]unknown[/]", CommandsText.HeaderLines(log)[1]);
    }

    [Fact]
    public void Output_text_adds_the_stderr_text_under_a_separator_line()
    {
        Assert.Equal(GammaAttempt3.Text + "\n── stderr ──\nerror: 1 test failed", CommandsText.OutputText(GammaAttempt3));
        Assert.Equal(GammaAttempt2.Text, CommandsText.OutputText(GammaAttempt2));
        Assert.DoesNotContain("stderr", CommandsText.OutputText(GammaAttempt2), StringComparison.Ordinal);
    }

    [Fact]
    public void Popup_titles_name_kind_owner_and_attempt()
    {
        Assert.Equal("acceptance gamma #3", CommandsText.PopupTitle(GammaAttempt3));
        Assert.Equal("setup beta", CommandsText.PopupTitle(BetaSetup));
        Assert.Equal("integration check alpha", CommandsText.PopupTitle(AlphaIntegrationCheck));
        Assert.Equal("bootstrap setup bootstrap #1", CommandsText.PopupTitle(BootstrapSetup));
    }

    [Fact]
    public void The_popup_has_command_outcome_output_and_stderr()
    {
        Assert.Equal(
        [
            new PopupSection("Command", "dotnet test tests/Gamma"),
            new PopupSection("Outcome", "failed (exit 1)"),
            new PopupSection("Output", GammaAttempt3.Text),
            new PopupSection("Stderr", "error: 1 test failed"),
        ], CommandsText.Popup(GammaAttempt3));
    }

    [Fact]
    public void The_popup_leaves_out_empty_sections()
    {
        Assert.Equal(["Command", "Outcome", "Output"], CommandsText.Popup(BetaSetup).Select(s => s.Heading));

        var bare = BetaSetup with { Command = null, Text = "", StderrText = " \n" };
        Assert.Equal([new PopupSection("Outcome", "passed")], CommandsText.Popup(bare));
    }

    [Fact]
    public void Model_text_is_escaped()
    {
        var log = GammaAttempt3 with { TaskId = "[x]", Command = "echo [red]", Key = "[x]/a.log" };

        Assert.Equal("[error]✖[/] 12:19:50 [[x]] acceptance #3 406 B", CommandsText.Row(log));
        Assert.Equal("[bold]echo [[red]][/]", CommandsText.HeaderLines(log)[0]);
        Assert.Equal("[[x]]/a.log", CommandsText.HeaderLines(log)[2]);
        Assert.Equal("acceptance [x] #3", CommandsText.PopupTitle(log));
        Assert.Equal("echo [red]", CommandsText.Popup(log)[0].Text);
    }

    [Fact]
    public void No_logs_text() => Assert.Equal("No command logs yet", CommandsText.NoLogs);
}

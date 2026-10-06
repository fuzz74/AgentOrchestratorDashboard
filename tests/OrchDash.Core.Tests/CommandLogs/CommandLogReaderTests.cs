using OrchDash.Core.CommandLogs;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Core.Tests.CommandLogs;

public sealed class CommandLogReaderTests
{
    private const string Acceptance = "core/20261003-113444/attempt-2-acceptance.log";
    private const string CutNotice = "... cut to the last 2,000,000 characters\n";

    [Fact]
    public void Every_row_of_the_command_log_table_is_recognised()
    {
        using var temp = new TempLogs();
        string[] keys =
        [
            "core/20261003-113444/setup.log",
            Acceptance,
            "core-integration-setup.log",
            "core-integration-check.log",
            "bootstrap-20261003-110028/attempt-1-setup.log",
            "bootstrap-20261003-110028/attempt-12-integration-check.log",
        ];
        foreach (var key in keys)
            temp.Write(key, "> dotnet build\n");

        var logs = new CommandLogReader().Read(temp.RunDir).Logs.ToDictionary(log => log.Key);

        Assert.Equal(keys.Order(StringComparer.Ordinal), logs.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((CommandKind.Setup, "core", "20261003-113444", (int?)null), Facts(logs[keys[0]]));
        Assert.Equal((CommandKind.Acceptance, "core", "20261003-113444", (int?)2), Facts(logs[keys[1]]));
        Assert.Equal((CommandKind.IntegrationSetup, "core", (string?)null, (int?)null), Facts(logs[keys[2]]));
        Assert.Equal((CommandKind.IntegrationCheck, "core", (string?)null, (int?)null), Facts(logs[keys[3]]));
        Assert.Equal((CommandKind.BootstrapSetup, (string?)null, "bootstrap-20261003-110028", (int?)1), Facts(logs[keys[4]]));
        Assert.Equal((CommandKind.BootstrapCheck, (string?)null, "bootstrap-20261003-110028", (int?)12), Facts(logs[keys[5]]));
        Assert.All(logs.Values, log =>
        {
            Assert.Equal(temp.FullPath(log.Key), log.Path);
            Assert.Equal(log.Path + ".stderr", log.StderrPath);
            Assert.Equal("> dotnet build\n", log.Text);
            Assert.Equal((null, CommandOutcome.Unknown, null), (log.Command, log.Outcome, log.ExitCode));
        });
    }

    [Fact]
    public void A_file_that_matches_no_row_is_ignored()
    {
        using var temp = new TempLogs();
        temp.Write("notes.txt", "x");
        temp.Write("core/20261003-113444/attempt-1-worker.json", "x");
        temp.Write("core/20261003-113444/attempt-1-acceptance.log.stderr", "x");
        temp.Write("core/2026-10-03/setup.log", "x");
        temp.Write("core/20261003-113444/nested/setup.log", "x");
        temp.Write("bootstrap-20261003-110028/attempt-x-setup.log", "x");
        temp.Write("bootstrap-2026/attempt-1-setup.log", "x");
        temp.Write("integration-setup.log", "x");

        var data = new CommandLogReader().Read(temp.RunDir);

        Assert.Empty(data.Logs);
        Assert.Empty(data.Problems);
    }

    [Fact]
    public void A_missing_logs_folder_gives_no_logs_and_no_problem()
    {
        using var temp = new TempLogs();
        Directory.Delete(Path.Combine(temp.RunDir, "logs"));

        var data = new CommandLogReader().Read(temp.RunDir);

        Assert.Empty(data.Logs);
        Assert.Empty(data.Problems);
    }

    [Fact]
    public void Logs_are_ordered_by_written_at_descending_then_key()
    {
        using var temp = new TempLogs();
        var early = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(temp.Write("b-integration-setup.log", "x"), early);
        File.SetLastWriteTimeUtc(temp.Write("a-integration-setup.log", "x"), early);
        File.SetLastWriteTimeUtc(temp.Write("c-integration-setup.log", "x"), early.AddMinutes(1));

        var logs = new CommandLogReader().Read(temp.RunDir).Logs;

        Assert.Equal(["c-integration-setup.log", "a-integration-setup.log", "b-integration-setup.log"], logs.Select(log => log.Key));
        Assert.Equal(new DateTimeOffset(early.AddMinutes(1)).ToLocalTime(), logs[0].WrittenAt);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(early.AddMinutes(1)), logs[0].WrittenAt.Offset);
    }

    [Fact]
    public void Written_at_is_the_later_of_the_log_and_its_stderr_file_and_length_is_the_logs()
    {
        using var temp = new TempLogs();
        var logTime = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(temp.Write(Acceptance, "12345"), logTime);
        File.SetLastWriteTimeUtc(temp.Write(Acceptance + ".stderr", "error: boom\n"), logTime.AddSeconds(30));

        var log = Assert.Single(new CommandLogReader().Read(temp.RunDir).Logs);

        Assert.Equal(new DateTimeOffset(logTime.AddSeconds(30)), log.WrittenAt);
        Assert.Equal(5, log.Length);
        Assert.Equal("error: boom\n", log.StderrText);

        File.SetLastWriteTimeUtc(temp.FullPath(Acceptance), logTime.AddMinutes(1));
        Assert.Equal(new DateTimeOffset(logTime.AddMinutes(1)), new CommandLogReader().Read(temp.RunDir).Logs[0].WrittenAt);
    }

    [Fact]
    public void A_missing_stderr_file_gives_an_empty_text()
    {
        using var temp = new TempLogs();
        temp.Write(Acceptance, "output\n");

        var log = Assert.Single(new CommandLogReader().Read(temp.RunDir).Logs);

        Assert.Equal("output\n", log.Text);
        Assert.Equal("", log.StderrText);
        Assert.False(File.Exists(log.StderrPath));
    }

    [Fact]
    public void A_byte_order_mark_is_removed()
    {
        using var temp = new TempLogs();
        temp.Write(Acceptance, "> dotnet test\n", bom: true);
        temp.Write(Acceptance + ".stderr", "warning\n", bom: true);

        var log = Assert.Single(new CommandLogReader().Read(temp.RunDir).Logs);

        Assert.Equal("> dotnet test\n", log.Text);
        Assert.Equal("warning\n", log.StderrText);
    }

    [Fact]
    public void The_same_text_comes_back_while_the_file_is_unchanged_and_a_new_one_after_an_append()
    {
        using var temp = new TempLogs();
        temp.Write(Acceptance, "line 1\n");
        temp.Write(Acceptance + ".stderr", "err 1\n");
        var reader = new CommandLogReader();

        var first = Assert.Single(reader.Read(temp.RunDir).Logs);
        var second = Assert.Single(reader.Read(temp.RunDir).Logs);
        Assert.Same(first.Text, second.Text);
        Assert.Same(first.StderrText, second.StderrText);

        temp.Append(Acceptance, "line 2\n");
        var third = Assert.Single(reader.Read(temp.RunDir).Logs);
        Assert.NotSame(first.Text, third.Text);
        Assert.Equal("line 1\nline 2\n", third.Text);
        Assert.Equal(14, third.Length);
        Assert.Same(first.StderrText, third.StderrText);
    }

    [Fact]
    public void A_text_longer_than_two_million_characters_is_cut_to_its_last_two_million()
    {
        using var temp = new TempLogs();
        temp.Write(Acceptance, new string('x', 5) + new string('y', 2_000_000));
        temp.Write(Acceptance + ".stderr", new string('e', 2_000_000));

        var log = Assert.Single(new CommandLogReader().Read(temp.RunDir).Logs);

        Assert.Equal(CutNotice.Length + 2_000_000, log.Text.Length);
        Assert.StartsWith(CutNotice, log.Text, StringComparison.Ordinal);
        Assert.Equal(CutNotice + new string('y', 2_000_000), log.Text);
        Assert.Equal(2_000_005, log.Length);
        Assert.Equal(new string('e', 2_000_000), log.StderrText);
    }

    [Fact]
    public void A_file_that_cannot_be_opened_before_the_first_read_gives_an_empty_text_and_a_problem()
    {
        using var temp = new TempLogs();
        temp.Write(Acceptance, "output\n");

        CommandLogData data;
        using (temp.Hold(Acceptance))
            data = new CommandLogReader().Read(temp.RunDir);

        var log = Assert.Single(data.Logs);
        Assert.Equal("", log.Text);
        var problem = Assert.Single(data.Problems);
        Assert.StartsWith(Acceptance + ": ", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_cannot_be_opened_after_a_read_keeps_its_last_text_and_gives_a_problem()
    {
        using var temp = new TempLogs();
        temp.Write(Acceptance, "line 1\n");
        temp.Write(Acceptance + ".stderr", "err 1\n");
        var reader = new CommandLogReader();
        var first = Assert.Single(reader.Read(temp.RunDir).Logs);

        temp.Append(Acceptance, "line 2\n");
        CommandLogData data;
        using (temp.Hold(Acceptance))
            data = reader.Read(temp.RunDir);

        var held = Assert.Single(data.Logs);
        Assert.Same(first.Text, held.Text);
        Assert.Same(first.StderrText, held.StderrText);
        var problem = Assert.Single(data.Problems);
        Assert.StartsWith(Acceptance + ": ", problem, StringComparison.Ordinal);

        var released = reader.Read(temp.RunDir);
        Assert.Empty(released.Problems);
        Assert.Equal("line 1\nline 2\n", Assert.Single(released.Logs).Text);
    }

    [Fact]
    public void A_stderr_file_that_cannot_be_opened_gives_a_problem_with_the_logs_key()
    {
        using var temp = new TempLogs();
        temp.Write(Acceptance, "line 1\n");
        temp.Write(Acceptance + ".stderr", "err 1\n");

        CommandLogData data;
        using (temp.Hold(Acceptance + ".stderr"))
            data = new CommandLogReader().Read(temp.RunDir);

        var log = Assert.Single(data.Logs);
        Assert.Equal(("line 1\n", ""), (log.Text, log.StderrText));
        var problem = Assert.Single(data.Problems);
        Assert.StartsWith(Acceptance + ": ", problem, StringComparison.Ordinal);
    }

    private static (CommandKind, string?, string?, int?) Facts(CommandLog log) => (log.Kind, log.TaskId, log.StartFolder, log.Attempt);
}

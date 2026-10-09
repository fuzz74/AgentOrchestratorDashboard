using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using OrchDash.Core.Tests.Fixtures;
using Xunit;

namespace OrchDash.Core.Tests.RunFolder;

public sealed class ProgressParserTests
{
    [Fact]
    public void Parses_time_as_local_wall_time_with_local_offset()
    {
        var entries = ProgressParser.Parse("2026-10-03 11:34:44  Run started: 5 tasks\n");

        var entry = Assert.Single(entries);
        Assert.Equal(new DateTime(2026, 10, 3, 11, 34, 44), entry.Time.DateTime);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 3, 11, 34, 44)), entry.Time.Offset);
        Assert.Null(entry.Source);
        Assert.Equal("Run started: 5 tasks", entry.Message);
    }

    [Fact]
    public void Empty_text_gives_no_entries()
    {
        Assert.Empty(ProgressParser.Parse(""));
        Assert.Empty(ProgressParser.Parse("\uFEFF"));
        Assert.Empty(ProgressParser.Parse("\n\r\n"));
    }

    [Fact]
    public void Adds_other_lines_to_the_previous_message()
    {
        const string text =
            "2026-10-01 10:05:53  [bootstrap] attempt 1: I set up the skeleton.\n" +
            "\n" +
            "What I created:\n" +
            "- **Solution:** `AnsiDemo.slnx`.\n" +
            "2026-10-01 10:06:00  Plan written: 24 tasks\n";

        var entries = ProgressParser.Parse(text);

        Assert.Equal(2, entries.Length);
        Assert.Equal("bootstrap", entries[0].Source);
        Assert.Equal("attempt 1: I set up the skeleton.\n\nWhat I created:\n- **Solution:** `AnsiDemo.slnx`.", entries[0].Message);
        Assert.Equal("Plan written: 24 tasks", entries[1].Message);
    }

    [Fact]
    public void Removes_carriage_returns_and_a_leading_bom()
    {
        const string text = "\uFEFF2026-10-01 10:00:00  first\r\nsecond line\r\n2026-10-01 10:00:01  [core] DONE\r\n";

        var entries = ProgressParser.Parse(text);

        Assert.Equal(2, entries.Length);
        Assert.Equal(new DateTime(2026, 10, 1, 10, 0, 0), entries[0].Time.DateTime);
        Assert.Equal("first\nsecond line", entries[0].Message);
        Assert.Equal("core", entries[1].Source);
        Assert.Equal("DONE", entries[1].Message);
    }

    [Fact]
    public void Keeps_the_last_line_without_a_line_break()
    {
        var entries = ProgressParser.Parse("2026-10-01 10:00:00  first\nmore");

        Assert.Equal("first\nmore", Assert.Single(entries).Message);
    }

    [Fact]
    public void Drops_lines_before_the_first_entry()
    {
        const string text = "# Progress\n\nnot an entry\n2026-10-01 10:00:00  first\n";

        var entry = Assert.Single(ProgressParser.Parse(text));

        Assert.Equal("first", entry.Message);
    }

    [Theory]
    [InlineData("2026-10-01 10:00:00 one space")]
    [InlineData("2026-10-01 10:00:00\ttab")]
    [InlineData("2026-13-01 10:00:00  bad month")]
    [InlineData("2026-10-01 25:00:00  bad hour")]
    [InlineData("2026-10-01T10:00:00  iso")]
    [InlineData("26-10-01 10:00:00  short year")]
    [InlineData(" 2026-10-01 10:00:00  indented")]
    public void A_line_without_a_valid_time_and_two_spaces_does_not_start_an_entry(string line)
    {
        var entry = Assert.Single(ProgressParser.Parse("2026-10-01 09:00:00  first\n" + line + "\n"));

        Assert.Equal("first\n" + line, entry.Message);
    }

    [Theory]
    [InlineData("[core] started (fresh)", "core", "started (fresh)")]
    [InlineData("[planner] tool: view", "planner", "tool: view")]
    [InlineData("[core]no space", null, "[core]no space")]
    [InlineData("[] empty", null, "[] empty")]
    [InlineData("[open bracket only", null, "[open bracket only")]
    [InlineData("not [core] at start", null, "not [core] at start")]
    public void Takes_the_source_from_a_bracket_prefix(string message, string? source, string expected)
    {
        var entry = Assert.Single(ProgressParser.Parse("2026-10-01 10:00:00  " + message));

        Assert.Equal(source, entry.Source);
        Assert.Equal(expected, entry.Message);
    }

    [Theory]
    // Activity
    [InlineData("[core] worker: 12 tool calls so far", ProgressKind.Activity)]
    [InlineData("[core] review: 3 tool calls", ProgressKind.Activity)]
    [InlineData("[core] resolver: 7 tool calls", ProgressKind.Activity)]
    [InlineData("[planner] tool: view", ProgressKind.Activity)]
    [InlineData("[planner] Read .orchestrator/spec.md", ProgressKind.Activity)]
    [InlineData("[bootstrap] Edit src/App.csproj", ProgressKind.Activity)]
    [InlineData("[planner] Write tasks.json", ProgressKind.Activity)]
    [InlineData("[planner] MultiEdit src/A.cs", ProgressKind.Activity)]
    [InlineData("[planner] NotebookEdit a.ipynb", ProgressKind.Activity)]
    [InlineData("[planner] Glob **/*.cs", ProgressKind.Activity)]
    [InlineData("[planner] Grep \"class\"", ProgressKind.Activity)]
    [InlineData("[bootstrap] Bash mkdir -p src\nmore", ProgressKind.Activity)]
    [InlineData("[bootstrap] PowerShell dotnet --list-sdks", ProgressKind.Activity)]
    [InlineData("[bootstrap] StructuredOutput reporting the result", ProgressKind.Activity)]
    [InlineData("[planner] Task explore the repo", ProgressKind.Activity)]
    [InlineData("[planner] Agent explore the repo", ProgressKind.Activity)]
    [InlineData("[planner] Read", ProgressKind.Activity)]
    [InlineData("[planner] Bash\nsecond line", ProgressKind.Activity)]
    [InlineData("tool: view", ProgressKind.Activity)]
    // Failure
    [InlineData("[core] FAILED after 3 attempts", ProgressKind.Failure)]
    [InlineData("[core] worker error: exit code 1", ProgressKind.Failure)]
    [InlineData("[core] acceptance failed: 2 tests failed", ProgressKind.Failure)]
    [InlineData("[core] review rejected: spec fail", ProgressKind.Failure)]
    [InlineData("[core] commit failed: nothing to commit", ProgressKind.Failure)]
    [InlineData("[core] sync with integration failed", ProgressKind.Failure)]
    [InlineData("[core] attempt 2 failed: acceptance", ProgressKind.Failure)]
    // Success
    [InlineData("[core] DONE and merged (0 USD, 1 attempt(s))", ProgressKind.Success)]
    [InlineData("[core] review passed", ProgressKind.Success)]
    [InlineData("Plan written: 5 tasks, 0,00 USD", ProgressKind.Success)]
    [InlineData("[bootstrap] Skeleton committed as 1a2b3c", ProgressKind.Success)]
    [InlineData("Run finished: 5 done, 0 failed, 0 blocked, 0,00 USD", ProgressKind.Success)]
    // Warning
    [InlineData("[core] merge conflict in src/A.cs", ProgressKind.Warning)]
    [InlineData("paused: stop requested", ProgressKind.Warning)]
    [InlineData("Graceful stop requested", ProgressKind.Warning)]
    [InlineData("[core] edited files outside owns: README.md", ProgressKind.Warning)]
    [InlineData("[core] no changes", ProgressKind.Warning)]
    [InlineData("[core] worker gave no structured result", ProgressKind.Warning)]
    [InlineData("Plan invalid: cycle in deps", ProgressKind.Warning)]
    // Info
    [InlineData("Run started: 5 tasks, max 3 in parallel", ProgressKind.Info)]
    [InlineData("Claude: C:\\claude.exe", ProgressKind.Info)]
    [InlineData("[core] started (fresh) in C:\\Work", ProgressKind.Info)]
    [InlineData("[core] attempt 1/3: worker started (gpt-6-sol)", ProgressKind.Info)]
    public void Assigns_the_kind_by_the_first_matching_row(string message, ProgressKind kind)
    {
        var entry = Assert.Single(ProgressParser.Parse("2026-10-01 10:00:00  " + message));

        Assert.Equal(kind, entry.Kind);
    }

    [Theory]
    [InlineData("[planner] Read spec.md", ProgressKind.Activity)]
    [InlineData("[core] Read spec.md", ProgressKind.Info)]
    [InlineData("Read spec.md", ProgressKind.Info)]
    [InlineData("[planner] Reader spec.md", ProgressKind.Info)]
    [InlineData("[planner] read spec.md", ProgressKind.Info)]
    [InlineData("[core] done", ProgressKind.Info)]
    [InlineData("[core] worker: many tool calls", ProgressKind.Info)]
    [InlineData("[core] attempt x failed", ProgressKind.Info)]
    [InlineData("[core] the worker: 3 tool calls", ProgressKind.Info)]
    public void Matches_kinds_ordinally_and_tool_names_only_for_planner_and_bootstrap(string message, ProgressKind kind)
    {
        var entry = Assert.Single(ProgressParser.Parse("2026-10-01 10:00:00  " + message));

        Assert.Equal(kind, entry.Kind);
    }

    [Fact]
    public void Tests_the_kind_on_the_message_without_its_source()
    {
        // "[DONE] x" has source "DONE" and message "x"; the message decides the kind.
        var entry = Assert.Single(ProgressParser.Parse("2026-10-01 10:00:00  [DONE] review passed"));

        Assert.Equal("DONE", entry.Source);
        Assert.Equal(ProgressKind.Success, entry.Kind);
        Assert.Equal(ProgressKind.Info, Assert.Single(ProgressParser.Parse("2026-10-01 10:00:00  [DONE] x")).Kind);
    }

    [Theory]
    [InlineData("[planner] ↳ [Map the repo] Glob **/*", "planner", "Map the repo", "Glob **/*", ProgressKind.Activity)]
    [InlineData("[bootstrap] ↳ [Survey] Bash mkdir -p src", "bootstrap", "Survey", "Bash mkdir -p src", ProgressKind.Activity)]
    [InlineData("[planner] ↳ [x] tool: glob", "planner", "x", "tool: glob", ProgressKind.Activity)]
    [InlineData("↳ [x] tool: glob", null, "x", "tool: glob", ProgressKind.Activity)]
    [InlineData("[core] ↳ [x] Read a.cs", "core", "x", "Read a.cs", ProgressKind.Info)]
    [InlineData("↳ [x] Glob **/*", null, "x", "Glob **/*", ProgressKind.Info)]
    [InlineData("[core] ↳ [x] review passed", "core", "x", "review passed", ProgressKind.Success)]
    [InlineData("[planner] ↳ [a] b] c", "planner", "a", "b] c", ProgressKind.Info)]
    [InlineData("[planner] ↳ [a]b] Read c.cs", "planner", "a]b", "Read c.cs", ProgressKind.Activity)]
    [InlineData("[planner] ↳ [x] ", "planner", "x", "", ProgressKind.Info)]
    public void Takes_the_sub_agent_from_a_leading_tag_and_classifies_the_rest(string message, string? source,
        string subAgent, string expected, ProgressKind kind)
    {
        var entry = Assert.Single(ProgressParser.Parse("2026-10-01 10:00:00  " + message));

        Assert.Equal(source, entry.Source);
        Assert.Equal(subAgent, entry.SubAgent);
        Assert.Equal(expected, entry.Message);
        Assert.Equal(kind, entry.Kind);
    }

    [Theory]
    [InlineData("[core] worker: 5 tool calls, last: ↳ [x] Read a.cs", "core", ProgressKind.Activity)]
    [InlineData("[planner] Read ↳ [x] a.cs", "planner", ProgressKind.Activity)]
    [InlineData("[planner] ↳ [] Glob **/*", "planner", ProgressKind.Info)]
    [InlineData("↳ [] x", null, ProgressKind.Info)]
    [InlineData("[planner] ↳ [x]Glob **/*", "planner", ProgressKind.Info)]
    [InlineData("[planner] ↳ [open bracket only", "planner", ProgressKind.Info)]
    [InlineData("[planner]  ↳ [x] Glob **/*", "planner", ProgressKind.Info)]
    [InlineData("[planner] ↳[x] Glob **/*", "planner", ProgressKind.Info)]
    public void Leaves_a_tag_that_is_not_leading_or_has_no_name_in_the_message(string message, string? source, ProgressKind kind)
    {
        var entry = Assert.Single(ProgressParser.Parse("2026-10-01 10:00:00  " + message));

        Assert.Equal(source, entry.Source);
        Assert.Null(entry.SubAgent);
        Assert.Equal(source is null ? message : message[(source.Length + 3)..], entry.Message);
        Assert.Equal(kind, entry.Kind);
    }

    [Fact]
    public void A_tagged_message_keeps_its_continuation_lines()
    {
        const string text =
            "2026-10-01 10:00:00  [planner] ↳ [Map the repo] Bash ls\r\n" +
            "second line\r\n" +
            "] third line\r\n" +
            "2026-10-01 10:00:01  [planner] ↳ [no end\n" +
            "x] Read a.cs\n";

        var entries = ProgressParser.Parse(text);

        Assert.Equal(2, entries.Length);
        Assert.Equal(("planner", "Map the repo", "Bash ls\nsecond line\n] third line", ProgressKind.Activity),
            (entries[0].Source, entries[0].SubAgent, entries[0].Message, entries[0].Kind));
        // The tag ends on its own line: a "] " on a continuation line does not close it.
        Assert.Equal(("planner", (string?)null, "↳ [no end\nx] Read a.cs", ProgressKind.Info),
            (entries[1].Source, entries[1].SubAgent, entries[1].Message, entries[1].Kind));
    }

    [Fact]
    public void Untagged_entries_have_no_sub_agent()
    {
        const string text =
            "2026-10-01 10:00:00  Run started: 1 tasks\n" +
            "2026-10-01 10:00:01  [planner] Glob **/*\n" +
            "↳ [x] continued\n" +
            "2026-10-01 10:00:02  [core] DONE\n";

        var entries = ProgressParser.Parse(text);

        Assert.Equal(3, entries.Length);
        Assert.All(entries, entry => Assert.Null(entry.SubAgent));
        Assert.Equal("Glob **/*\n↳ [x] continued", entries[1].Message);
    }

    [Fact]
    public void Parses_the_claude_fixture()
    {
        var entries = ProgressParser.Parse(File.ReadAllText(Path.Combine(FixturePaths.ClaudeRunDir, "progress.md")));

        Assert.NotEmpty(entries);
        Assert.Equal(new DateTime(2026, 10, 1, 10, 4, 33), entries[0].Time.DateTime);
        Assert.Equal("bootstrap", entries[0].Source);
        Assert.Contains(entries, entry => entry.Source is null && entry.Message.StartsWith("Run started: 24 tasks", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Source == "bootstrap" && entry.Kind == ProgressKind.Activity);
        Assert.All(entries, entry => Assert.DoesNotContain('\r', entry.Message));

        var multiLine = Assert.Single(entries, entry => entry.Message.StartsWith("attempt 1: I set up the skeleton", StringComparison.Ordinal));
        Assert.Contains("\nWhat I created:\n", multiLine.Message, StringComparison.Ordinal);

        var finished = entries[^1];
        Assert.Null(finished.Source);
        Assert.Equal(ProgressKind.Success, finished.Kind);
        Assert.StartsWith("Run finished: 24 done", finished.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_the_copilot_fixture()
    {
        var entries = ProgressParser.Parse(File.ReadAllText(Path.Combine(FixturePaths.CopilotRunDir, "progress.md")));

        Assert.Equal(3, entries.Count(entry => entry.Source is null && entry.Message.StartsWith("Run started: ", StringComparison.Ordinal)));
        Assert.Contains(entries, entry => entry.Source == "planner" && entry.Message == "tool: view" && entry.Kind == ProgressKind.Activity);
        Assert.Contains(entries, entry => entry.Source == "core" && entry.Message == "review passed" && entry.Kind == ProgressKind.Success);
        Assert.Contains(entries, entry => entry.Source is null && entry.Message.StartsWith("Plan written", StringComparison.Ordinal) && entry.Kind == ProgressKind.Success);
    }
}

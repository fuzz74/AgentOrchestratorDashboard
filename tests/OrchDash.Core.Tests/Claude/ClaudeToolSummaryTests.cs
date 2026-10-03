using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Claude.ClaudeLines;

namespace OrchDash.Core.Tests.Claude;

public sealed class ClaudeToolSummaryTests
{
    private const string WorkDir = @"C:\Work\Repo.worktrees\alpha";

    private static string Summary(string name, string input, string? workDir = null, params string[] before)
    {
        var content = ParseIn(workDir, [.. before, Assistant("msg_1", Time1, ToolUse("toolu_1", name, input))]);
        return Assert.IsType<ToolCall>(Assert.Single(content.Items)).Summary;
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("Edit")]
    [InlineData("Write")]
    [InlineData("MultiEdit")]
    [InlineData("NotebookEdit")]
    public void File_tools_show_the_file_path_relative_to_the_init_cwd(string name)
    {
        var summary = Summary(name, """{"file_path":"C:\\Work\\Repo\\src\\Alpha\\Parser.cs","content":"x"}""", WorkDir, Init());

        Assert.Equal($"{name} src/Alpha/Parser.cs", summary);
    }

    [Fact]
    public void View_shows_the_path()
    {
        Assert.Equal("view src/a.cs", Summary("view", """{"path":"C:\\Work\\Repo\\src\\a.cs"}""", null, Init()));
    }

    [Theory]
    [InlineData("Glob")]
    [InlineData("glob")]
    [InlineData("Grep")]
    [InlineData("rg")]
    public void Search_tools_show_the_pattern_and_the_path_when_set(string name)
    {
        Assert.Equal($"{name} **/*.cs", Summary(name, """{"pattern":"**/*.cs"}"""));
        Assert.Equal($"{name} class \\w+ in src/Alpha",
            Summary(name, """{"pattern":"class \\w+","path":"C:\\Work\\Repo\\src\\Alpha"}""", null, Init()));
    }

    [Theory]
    [InlineData("Bash")]
    [InlineData("PowerShell")]
    [InlineData("powershell")]
    public void Shell_tools_show_the_first_line_of_the_command(string name)
    {
        Assert.Equal($"{name} dotnet build", Summary(name, """{"command":"dotnet build\r\ndotnet test","description":"Build"}"""));
    }

    [Fact]
    public void StructuredOutput_reports_the_result()
    {
        Assert.Equal("StructuredOutput reporting the result", Summary("StructuredOutput", """{"status":"done","summary":"ok"}"""));
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Agent")]
    public void Agent_tools_show_the_description(string name)
    {
        Assert.Equal($"{name} Find the callers", Summary(name, """{"description":"Find the callers","prompt":"..."}"""));
    }

    [Fact]
    public void Another_tool_shows_only_its_name()
    {
        Assert.Equal("WebFetch", Summary("WebFetch", """{"url":"https://example.com","intentionSummary":"fetch"}"""));
    }

    [Fact]
    public void A_missing_detail_shows_only_the_name()
    {
        Assert.Equal("Read", Summary("Read", "{}"));
        Assert.Equal("Bash", Summary("Bash", """{"command":42}"""));
    }

    [Fact]
    public void Detail_longer_than_100_characters_is_cut_to_97_and_an_ellipsis()
    {
        var exact = new string('a', 100);
        var tooLong = new string('b', 98) + "cde";

        Assert.Equal($"Bash {exact}", Summary("Bash", $$"""{"command":"{{exact}}"}"""));
        Assert.Equal($"Bash {new string('b', 97)}...", Summary("Bash", $$"""{"command":"{{tooLong}}"}"""));
    }

    [Fact]
    public void Paths_are_relative_to_work_dir_when_there_is_no_init()
    {
        Assert.Equal("Read src/a.cs", Summary("Read", """{"file_path":"C:\\Work\\Repo.worktrees\\alpha\\src\\a.cs"}""", WorkDir));
    }

    [Fact]
    public void Init_cwd_wins_over_work_dir()
    {
        var summary = Summary("Read", """{"file_path":"C:\\Work\\Repo.worktrees\\alpha\\src\\a.cs"}""", WorkDir,
            Init(@"C:\\Work\\Repo.worktrees"));

        Assert.Equal("Read alpha/src/a.cs", summary);
    }

    [Theory]
    [InlineData("c:/work/repo.WORKTREES/Alpha/src/a.cs", "src/a.cs")]
    [InlineData("C:\\\\Work\\\\Repo.worktrees\\\\alpha/tests\\\\b.cs", "tests/b.cs")]
    [InlineData("C:\\\\Work\\\\Repo.worktrees\\\\alphabet\\\\a.cs", "C:\\Work\\Repo.worktrees\\alphabet\\a.cs")]
    [InlineData("D:\\\\Other\\\\a.cs", "D:\\Other\\a.cs")]
    [InlineData("src\\\\a.cs", "src\\a.cs")]
    public void Paths_compare_case_insensitively_with_either_slash_and_outside_paths_stay(string jsonPath, string expected)
    {
        Assert.Equal($"Read {expected}", Summary("Read", $$"""{"file_path":"{{jsonPath}}"}""", WorkDir + "\\"));
    }
}

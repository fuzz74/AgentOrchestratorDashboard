using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Copilot.CopilotEvents;

namespace OrchDash.Core.Tests.Copilot;

/// <summary>The tool summary rules, through tool requests in <c>assistant.message</c> lines (work dir <see cref="CopilotEvents.WorkDir"/>).</summary>
public sealed class CopilotToolSummaryTests
{
    private static readonly string LongText = new('x', 150);

    [Theory]
    [InlineData("Read", """{"file_path":"C:\\Work\\Repo.worktrees\\core\\src\\A.cs"}""", null, "Read src/A.cs")]
    [InlineData("Edit", """{"file_path":"c:/work/REPO.worktrees/core/src/Sub/B.cs"}""", null, "Edit src/Sub/B.cs")]
    [InlineData("Write", """{"file_path":"C:\\Work\\Repo.worktrees\\core/src\\C.cs","content":"x"}""", null, "Write src/C.cs")]
    [InlineData("MultiEdit", """{"file_path":"C:\\Other\\D.cs"}""", null, "MultiEdit C:\\Other\\D.cs")]
    [InlineData("NotebookEdit", """{"file_path":"C:\\Work\\Repo.worktrees\\core-2\\E.ipynb"}""", null, "NotebookEdit C:\\Work\\Repo.worktrees\\core-2\\E.ipynb")]
    [InlineData("Read", """{"file_path":"src\\Relative.cs"}""", null, "Read src\\Relative.cs")]
    [InlineData("Read", """{"file_path":42}""", null, "Read")]
    [InlineData("Read", """{}""", null, "Read")]
    [InlineData("view", """{"path":"C:\\Work\\Repo.worktrees\\core\\README.md"}""", "view the file", "view README.md")]
    [InlineData("view", """{"path":"C:\\Work\\Repo.worktrees\\core"}""", null, "view .")]
    [InlineData("Glob", """{"pattern":"**/*.cs"}""", null, "Glob **/*.cs")]
    [InlineData("glob", """{"pattern":"*.md","path":"C:\\Work\\Repo.worktrees\\core\\docs"}""", null, "glob *.md in docs")]
    [InlineData("Grep", """{"pattern":"TODO","path":"D:\\Elsewhere"}""", null, "Grep TODO in D:\\Elsewhere")]
    [InlineData("rg", """{"pattern":"CountCommand","glob":"*.cs","path":""}""", null, "rg CountCommand")]
    [InlineData("Bash", """{"command":"dotnet build\ndotnet test"}""", null, "Bash dotnet build")]
    [InlineData("PowerShell", """{"command":"git status\r\ngit diff"}""", null, "PowerShell git status")]
    [InlineData("powershell", """{"command":"git status --short; dotnet --version","description":"Inspect"}""", "Inspect", "powershell git status --short; dotnet --version")]
    [InlineData("StructuredOutput", """{"status":"done"}""", null, "StructuredOutput reporting the result")]
    [InlineData("Task", """{"description":"Find the parser","prompt":"..."}""", null, "Task Find the parser")]
    [InlineData("Agent", """{"description":"Review the diff"}""", null, "Agent Review the diff")]
    [InlineData("apply_patch", "\"*** Begin Patch\"", "Apply the patch", "apply_patch Apply the patch")]
    [InlineData("read_powershell", """{"shellId":"0"}""", null, "read_powershell")]
    [InlineData("view", "\"not an object\"", null, "view")]
    public void Summary_follows_the_tool_table(string name, string arguments, string? intentionSummary, string expected)
    {
        var call = Request(WorkDir, name, arguments, intentionSummary);

        Assert.Equal(expected, call.Summary);
    }

    [Fact]
    public void Detail_longer_than_100_characters_is_cut()
    {
        var call = Request(WorkDir, "Bash", $$"""{"command":{{Quote(LongText)}}}""");

        Assert.Equal("Bash " + new string('x', 97) + "...", call.Summary);
    }

    [Fact]
    public void Detail_of_100_characters_is_kept()
    {
        var text = LongText[..100];

        var call = Request(WorkDir, "Bash", $$"""{"command":{{Quote(text)}}}""");

        Assert.Equal("Bash " + text, call.Summary);
    }

    [Fact]
    public void Relative_path_is_cut_after_it_is_made_relative()
    {
        var path = WorkDir + @"\src\" + LongText[..120];

        var call = Request(WorkDir, "Read", $$"""{"file_path":{{Quote(path)}}}""");

        Assert.Equal("Read src/" + new string('x', 93) + "...", call.Summary);
    }

    [Fact]
    public void Intention_summary_is_cut_too()
    {
        var call = Request(WorkDir, "apply_patch", "\"patch\"", LongText);

        Assert.Equal("apply_patch " + new string('x', 97) + "...", call.Summary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_a_work_dir_paths_stay_as_they_are(string? workDir)
    {
        var call = Request(workDir, "view", """{"path":"C:\\Work\\Repo.worktrees\\core\\README.md"}""");

        Assert.Equal(@"view C:\Work\Repo.worktrees\core\README.md", call.Summary);
    }

    [Fact]
    public void Work_dir_with_forward_slashes_and_a_trailing_slash_matches()
    {
        var call = Request("c:/work/repo.worktrees/core/", "view", """{"path":"C:\\Work\\Repo.worktrees\\core\\src\\A.cs"}""");

        Assert.Equal("view src/A.cs", call.Summary);
    }

    private static ToolCall Request(string? workDir, string name, string arguments, string? intentionSummary = null)
    {
        var content = ParseIn(workDir, Message("0", toolRequests: ToolRequest("call_1", name, arguments, intentionSummary)));
        return Assert.IsType<ToolCall>(Assert.Single(content.Items));
    }
}

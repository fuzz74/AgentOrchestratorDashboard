using OrchDash.App;
using OrchDash.Tests.Host;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using Xunit;

namespace OrchDash.Tests.App;

// Spec 1.1-1.5: the start path, the exit codes and stderr, with a fake runUi or the real UI on the in-memory terminal.
// A fixture run gets the fixture store folders, any other repo empty temp store folders.
public sealed class AppRunnerTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly TempFolder _stores = new();
    private readonly StringWriter _stderr = new();
    private int _uiRuns;

    public void Dispose()
    {
        _stderr.Dispose();
        _temp.Dispose();
        _stores.Dispose();
    }

    /// <summary>Runs the app with empty temp store folders.</summary>
    private int Run(string[] args, string currentDirectory, Action<Visual, Func<TerminalLoopResult>> runUi) =>
        Run(args, currentDirectory, runUi, _stores.Folder("claude"), _stores.Folder("copilot"));

    /// <summary>Runs the app with the fixture store folders.</summary>
    private int RunFixture(string[] args, string currentDirectory, Action<Visual, Func<TerminalLoopResult>> runUi) =>
        Run(args, currentDirectory, runUi, FixtureRuns.ClaudeStore, FixtureRuns.CopilotStore);

    private int Run(string[] args, string currentDirectory, Action<Visual, Func<TerminalLoopResult>> runUi, string claudeDir, string copilotDir) =>
        AppRunner.Run(args, currentDirectory, _stderr, (root, onUpdate) =>
        {
            _uiRuns++;
            runUi(root, onUpdate);
        }, claudeDir, copilotDir, FixtureRuns.CommandLogsOnly());

    /// <summary>A runUi that ticks once, as Terminal.Run does before its first frame, and then returns.</summary>
    private static void TickOnce(Visual root, Func<TerminalLoopResult> onUpdate)
    {
        Assert.NotNull(root);
        Assert.Equal(TerminalLoopResult.Continue, onUpdate());
    }

    [Fact]
    public void Without_a_repo_it_writes_the_full_start_path_to_stderr_and_returns_2_without_the_ui()
    {
        var start = _temp.Folder("plain/src");

        var code = Run(["src"], Path.Combine(_temp.Path, "plain"), TickOnce);

        Assert.Equal(2, code);
        Assert.Equal($"No .orchestrator folder at or above {start}{Environment.NewLine}", _stderr.ToString());
        Assert.Equal(0, _uiRuns);
    }

    [Fact]
    public void A_start_path_that_does_not_exist_returns_2()
    {
        var missing = Path.Combine(_temp.Path, "missing");

        var code = Run([missing], _temp.Path, TickOnce);

        Assert.Equal(2, code);
        Assert.Equal($"No .orchestrator folder at or above {missing}{Environment.NewLine}", _stderr.ToString());
        Assert.Equal(0, _uiRuns);
    }

    [Fact]
    public void Without_an_argument_the_search_starts_at_the_current_directory()
    {
        var current = _temp.Folder("plain");

        var code = Run([], current, TickOnce);

        Assert.Equal(2, code);
        Assert.Equal($"No .orchestrator folder at or above {current}{Environment.NewLine}", _stderr.ToString());
    }

    [Fact]
    public void Without_an_argument_a_repo_above_the_current_directory_is_used()
    {
        var code = RunFixture([], Path.Combine(FixtureRuns.ClaudeRepo, ".orchestrator", "logs"), TickOnce);

        Assert.Equal(0, code);
        Assert.Equal(1, _uiRuns);
        Assert.Equal("", _stderr.ToString());
    }

    [Fact]
    public void When_runUi_returns_it_returns_0()
    {
        _temp.Folder("repo/.orchestrator");

        var code = Run(["repo"], _temp.Path, TickOnce);

        Assert.Equal(0, code);
        Assert.Equal(1, _uiRuns);
        Assert.Equal("", _stderr.ToString());
    }

    [Fact]
    public void When_runUi_throws_it_writes_the_message_to_stderr_and_returns_1()
    {
        _temp.Folder("repo/.orchestrator");

        var code = Run(["repo"], _temp.Path, (_, _) => throw new InvalidOperationException("The terminal went away."));

        Assert.Equal(1, code);
        Assert.Equal($"The terminal went away.{Environment.NewLine}", _stderr.ToString());
    }

    [Fact]
    public void The_real_ui_shows_the_relative_repo_argument_and_q_exits_with_0()
    {
        var before = FixtureRuns.Listing(FixtureRuns.ClaudeRepo);
        string? frame = null;
        var exited = false;

        var code = RunFixture([Path.Combine("..", "claude-run")], FixtureRuns.CopilotRepo, (root, onUpdate) =>
        {
            using var harness = TerminalHarness.Start(root, onUpdate);
            frame = harness.Frame();
            harness.Type('q');
            exited = harness.Exited;
        });

        Assert.Equal(0, code);
        Assert.Equal("", _stderr.ToString());
        Assert.True(exited);
        Assert.NotNull(frame);
        Assert.StartsWith("claude-run  Finished", frame, StringComparison.Ordinal);
        Assert.Contains("Overview", frame, StringComparison.Ordinal);
        Assert.Contains("Conversation", frame, StringComparison.Ordinal);
        Assert.Equal(before, FixtureRuns.Listing(FixtureRuns.ClaudeRepo));
    }

    [Fact]
    public void Ctrl_q_in_the_real_ui_exits_with_0()
    {
        var exited = false;

        var code = RunFixture([FixtureRuns.CopilotRepo], _temp.Path, (root, onUpdate) =>
        {
            using var harness = TerminalHarness.Start(root, onUpdate);
            Assert.StartsWith("copilot-run  Finished", harness.Frame(), StringComparison.Ordinal);
            harness.Type('q', TerminalModifiers.Ctrl);
            exited = harness.Exited;
        });

        Assert.Equal(0, code);
        Assert.Equal("", _stderr.ToString());
        Assert.True(exited);
    }
}

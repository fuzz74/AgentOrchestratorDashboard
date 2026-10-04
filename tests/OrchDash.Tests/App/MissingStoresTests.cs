using OrchDash.App;
using OrchDash.Core.Model;
using OrchDash.Tests.Host;
using Xunit;

namespace OrchDash.Tests.App;

// Spec 18.3: store folders, or the folders and files under them, that do not exist give the run with the reasons of
// 14.6 and no exception.
public sealed class MissingStoresTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>
    /// The two store folders for a case: "none" leaves both missing, "empty" creates them without content, "folders"
    /// also creates <c>projects</c> and <c>session-state</c> but no session files and no database.
    /// </summary>
    private (string Claude, string Copilot) Stores(string missing)
    {
        var claude = Path.Combine(_temp.Path, "claude");
        var copilot = Path.Combine(_temp.Path, "copilot");
        if (missing is "empty" or "folders")
        {
            _temp.Folder("claude");
            _temp.Folder("copilot");
        }
        if (missing is "folders")
        {
            _temp.Folder("claude/projects");
            _temp.Folder("copilot/session-state");
        }
        return (claude, copilot);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("empty")]
    [InlineData("folders")]
    public void The_claude_run_without_stores_has_no_transcript_for_every_session(string missing)
    {
        var (claude, copilot) = Stores(missing);

        var s = FixtureRuns.Poll(FixtureRuns.ClaudeRepo, claude, copilot);

        Assert.Equal(3, s.Sessions.Length);
        Assert.All(s.Sessions, x => Assert.Equal(["no transcript"], x.Unavailable));
        Assert.All(s.Sessions, x => Assert.Same(StoreData.Empty, x.Stores));
        Assert.Empty(s.Problems);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("empty")]
    [InlineData("folders")]
    public void The_copilot_run_without_stores_has_no_session_folder_and_no_database_rows(string missing)
    {
        var (claude, copilot) = Stores(missing);

        var s = FixtureRuns.Poll(FixtureRuns.CopilotRepo, claude, copilot);

        Assert.Equal(10, s.Sessions.Length);
        Assert.All(s.Sessions, x => Assert.Equal(["no session folder", "no database rows"], x.Unavailable));
        Assert.All(s.Sessions, x => Assert.Same(StoreData.Empty, x.Stores));
        Assert.Equal(["Copilot database not found: " + Path.Combine(copilot, "session-store.db")], s.Problems);
    }

    [Theory]
    [InlineData("claude-run")]
    [InlineData("copilot-run")]
    public void The_app_without_stores_reaches_the_ui_and_returns_0(string run)
    {
        var (claude, copilot) = Stores("none");
        using var stderr = new StringWriter();
        string? frame = null;

        var code = AppRunner.Run([Path.Combine(FixtureRuns.Root, run)], _temp.Path, stderr, (root, onUpdate) =>
        {
            using var harness = TerminalHarness.Start(root, onUpdate);
            harness.Type('3');
            frame = harness.Frame();
            harness.Type('q');
        }, claude, copilot);

        Assert.Equal(0, code);
        Assert.Equal("", stderr.ToString());
        Assert.NotNull(frame);
        Assert.StartsWith(run + "  Finished", frame, StringComparison.Ordinal);
        Assert.Contains("unavailable: " + (run == "claude-run" ? "no transcript" : "no session folder, no database rows"), frame, StringComparison.Ordinal);
        Assert.False(Directory.Exists(claude));
        Assert.False(Directory.Exists(copilot));
    }
}

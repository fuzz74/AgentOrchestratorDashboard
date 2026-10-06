using OrchDash.App;
using OrchDash.Core.SessionFolder;
using OrchDash.Core.Transcript;
using OrchDash.Core.UsageDb;
using OrchDash.Tests.Host;
using Xunit;

namespace OrchDash.Tests.App;

// Spec 18.1, 18.2 and 28.1: the seven pages in the real UI, and the provider stores on the folders the app is given
// (FixtureRuns.CreateStore passes the fixture store folders to AppRunner.CreateStore).
public sealed class AppWiringTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_pages_are_overview_conversation_context_usage_graph_git_and_commands_in_this_order()
    {
        Assert.Equal(
            ["overview", "conversation", "context", "usage", "graph", "git", "commands"],
            AppRunner.CreatePages().Select(p => p.Id));
    }

    [Fact]
    public void The_real_ui_shows_the_seven_tabs_and_keys_3_to_7_show_the_other_pages()
    {
        string? overview = null, context = null, usage = null, graph = null, git = null, commands = null;

        var code = AppRunner.Run([FixtureRuns.ClaudeRepo], _temp.Path, TextWriter.Null, (root, onUpdate) =>
        {
            using var harness = TerminalHarness.Start(root, onUpdate);
            overview = harness.Frame();
            harness.Type('3');
            context = harness.Frame();
            harness.SaveSvg("app-claude-context");
            harness.Type('4');
            usage = harness.Frame();
            harness.SaveSvg("app-claude-usage");
            harness.Type('5');
            graph = harness.Frame();
            harness.SaveSvg("app-claude-graph");
            harness.Type('6');
            git = harness.Frame();
            harness.SaveSvg("app-claude-git");
            harness.Type('7');
            commands = harness.Frame();
            harness.SaveSvg("app-claude-commands");
            harness.Type('q');
        }, FixtureRuns.ClaudeStore, FixtureRuns.CopilotStore, FixtureRuns.CommandLogsOnly());

        Assert.Equal(0, code);
        // Row 0 is the header, row 1 the time bar, rows 2 to 4 are the tabs.
        Assert.StartsWith(
            "│ Overview │ │ Conversation │ │ Context │ │ Usage │ │ Graph │ │ Git │ │ Commands │",
            overview!.Split('\n')[3],
            StringComparison.Ordinal);
        Assert.DoesNotContain("Context per call", overview, StringComparison.Ordinal);
        Assert.Contains("Context per call", context, StringComparison.Ordinal);
        Assert.Contains("Make-up at call 8", context, StringComparison.Ordinal);
        Assert.Contains("Tokens per group", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("Context per call", usage, StringComparison.Ordinal);
        Assert.Contains("W1", graph, StringComparison.Ordinal);
        Assert.DoesNotContain("Tokens per group", graph, StringComparison.Ordinal);
        Assert.Contains("read -", git, StringComparison.Ordinal);
        Assert.Contains("audio-synth acceptance #1", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("read -", commands, StringComparison.Ordinal);
    }

    [Fact]
    public void The_provider_stores_set_all_four_sources_with_one_copilot_folder_store()
    {
        var stores = AppRunner.CreateProviderStores(FixtureRuns.ClaudeStore, FixtureRuns.CopilotStore);

        Assert.IsType<ClaudeTranscriptStore>(stores.ClaudeTranscripts);
        Assert.IsType<CopilotFolderStore>(stores.CopilotFolders);
        Assert.Same(stores.CopilotFolders, stores.CopilotIds);
        Assert.IsType<CopilotUsageReader>(stores.CopilotUsage);
    }

    [Fact]
    public void Creating_the_provider_stores_touches_no_file()
    {
        var claude = Path.Combine(_temp.Path, "claude");
        var copilot = Path.Combine(_temp.Path, "copilot");

        AppRunner.CreateProviderStores(claude, copilot);

        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Path));
    }

    [Theory]
    [InlineData("claude-run", "audio-synth/20261001-104634/attempt-1-worker.json")]
    [InlineData("copilot-run", "core/20261003-113444/attempt-1-worker.json")]
    public void A_store_on_the_fixture_store_folders_gives_sessions_with_store_data(string run, string key)
    {
        using var store = FixtureRuns.CreateStore(Path.Combine(FixtureRuns.Root, run));
        store.Poll();

        var session = Assert.Single(store.Current.Sessions, x => x.Files.Key == key);
        Assert.NotEmpty(session.Stores.SystemPrompt);
        Assert.NotNull(session.Stores.CliVersion);
        Assert.Empty(session.Unavailable);
    }
}

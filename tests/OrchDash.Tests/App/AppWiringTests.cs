using OrchDash.App;
using OrchDash.Core.SessionFolder;
using OrchDash.Core.Transcript;
using OrchDash.Core.UsageDb;
using OrchDash.Tests.Host;
using Xunit;

namespace OrchDash.Tests.App;

// Spec 18.1 and 18.2: the four pages in the real UI, and the provider stores on the folders the app is given
// (FixtureRuns.CreateStore passes the fixture store folders to AppRunner.CreateStore).
public sealed class AppWiringTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_pages_are_overview_conversation_context_and_usage_in_this_order()
    {
        Assert.Equal(["overview", "conversation", "context", "usage"], AppRunner.CreatePages().Select(p => p.Id));
    }

    [Fact]
    public void The_real_ui_shows_the_four_tabs_and_keys_3_and_4_show_the_context_and_usage_pages()
    {
        string? overview = null, context = null, usage = null;

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
            harness.Type('q');
        }, FixtureRuns.ClaudeStore, FixtureRuns.CopilotStore);

        Assert.Equal(0, code);
        // Row 0 is the header, rows 1 to 3 are the tabs.
        Assert.StartsWith("│ Overview │ │ Conversation │ │ Context │ │ Usage │", overview!.Split('\n')[2], StringComparison.Ordinal);
        Assert.DoesNotContain("Context per call", overview, StringComparison.Ordinal);
        Assert.Contains("Context per call", context, StringComparison.Ordinal);
        Assert.Contains("Make-up at call 8", context, StringComparison.Ordinal);
        Assert.Contains("Tokens per group", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("Context per call", usage, StringComparison.Ordinal);
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

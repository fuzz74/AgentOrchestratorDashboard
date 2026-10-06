using System.Diagnostics;
using System.Text.RegularExpressions;
using OrchDash.App;
using OrchDash.Core.CommandLogs;
using OrchDash.Core.Git;
using OrchDash.Core.Model;
using OrchDash.Core.Processes;
using OrchDash.Core.Store;
using OrchDash.Pages.GitView;
using OrchDash.Tests.Host;
using Xunit;

namespace OrchDash.Tests.App;

// Spec 28.1-28.3 and N.9 end to end: the Graph, Git and Commands pages on both fixture runs through AppRunner.Run, the
// fixture folder unchanged by the command-log reader, and the real sources where git cannot read the run. No test here
// runs git against this repo: the fixtures get no git reader or one that cannot start git, and the real git reader
// only sees a copy of a fixture run in the temp folder, outside any work tree.
public sealed class InsightWiringTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string RevParseProblem = "git rev-parse --show-toplevel: ";

    // A row of the Commands page's log list, after the pane border and the selection marker: icon, clock, owner.
    private static readonly Regex CommandRow = new(@"^│\s*(→ )?[✔✖▶·] \d\d:\d\d:\d\d ", RegexOptions.Multiline);

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("claude-run", 6, "audio-synth integration check")]
    [InlineData("copilot-run", 11, "count acceptance #1")]
    public void The_app_shows_the_graph_git_and_commands_pages_of_a_fixture_run(string run, int logs, string row)
    {
        string? graph = null, git = null, commands = null;

        var code = AppRunner.Run([Path.Combine(FixtureRuns.Root, run)], _temp.Path, TextWriter.Null, (root, onUpdate) =>
        {
            using var harness = TerminalHarness.Start(root, onUpdate);
            harness.Type('5');
            graph = harness.Frame();
            harness.Type('6');
            git = harness.Frame();
            harness.Type('7');
            commands = harness.Frame();
            harness.Type('q');
        }, FixtureRuns.ClaudeStore, FixtureRuns.CopilotStore, FixtureRuns.CommandLogsOnly());

        Assert.Equal(0, code);
        Assert.Contains("W1", graph, StringComparison.Ordinal);
        // Without a git reader the header has no read time and no problem line.
        Assert.Contains("read -", git, StringComparison.Ordinal);
        Assert.DoesNotContain("git unavailable", git, StringComparison.Ordinal);
        Assert.Equal(logs, CommandRow.Count(commands!));
        Assert.Contains(row, commands, StringComparison.Ordinal);
        Assert.Contains("bootstrap bootstrap setup #1", commands, StringComparison.Ordinal);
        Assert.Contains("bootstrap bootstrap check #1", commands, StringComparison.Ordinal);
    }

    [Fact]
    public void The_claude_run_lists_the_bootstrap_task_and_integration_logs()
    {
        var keys = FixtureRuns.Poll(FixtureRuns.ClaudeRepo).Commands.Select(c => c.Key).Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "audio-synth-integration-check.log",
                "audio-synth-integration-setup.log",
                "audio-synth/20261001-104634/attempt-1-acceptance.log",
                "audio-synth/20261001-104634/setup.log",
                "bootstrap-20261001-100433/attempt-1-integration-check.log",
                "bootstrap-20261001-100433/attempt-1-setup.log",
            ],
            keys);
    }

    [Fact]
    public void Reading_the_command_logs_of_both_fixture_runs_changes_no_file()
    {
        var before = FixtureRuns.Listing(FixtureRuns.Root);

        foreach (var repo in new[] { FixtureRuns.ClaudeRepo, FixtureRuns.CopilotRepo })
        {
            using var store = FixtureRuns.CreateStore(repo);
            store.Poll();
            store.Poll();
            Assert.NotEmpty(store.Current.Commands);
            Assert.All(store.Current.Commands, c => Assert.NotEmpty(c.Text));
        }

        Assert.Equal(before, FixtureRuns.Listing(FixtureRuns.Root));
    }

    [Fact]
    public async Task The_real_sources_on_a_run_outside_any_work_tree_show_the_git_problem_without_an_exception()
    {
        var copy = _temp.Copy(FixtureRuns.ClaudeRepo, "claude-run");

        // Null sources: CreateInsightSources(), so real git, the WMI query and the command logs (28.2).
        using var store = AppRunner.CreateStore(copy, TimeSpan.FromMilliseconds(100), _temp.Folder("claude"), _temp.Folder("copilot"));
        store.Start();
        await Until(store, s => s.Problems.Any(p => p.StartsWith(RevParseProblem, StringComparison.Ordinal)));

        var current = store.Current;
        Assert.Contains(current.Problems, p => p.StartsWith(RevParseProblem, StringComparison.Ordinal));
        Assert.Null(current.Git.RepoRoot);
        Assert.Equal(6, current.Commands.Length);
        Assert.True(
            current.Processes.SampledAt is not null || current.Problems.Any(p => p.StartsWith("processes: ", StringComparison.Ordinal)),
            "no process sample and no processes problem");
    }

    [Fact]
    public async Task A_git_that_cannot_start_shows_git_unavailable_on_the_git_page()
    {
        var sources = new InsightSources(new GitReader(gitPath: "no-such-git"), null, null);
        using var store = FixtureRuns.CreateStore(FixtureRuns.ClaudeRepo, TimeSpan.FromMilliseconds(100), sources);
        store.Start();
        await Until(store, s => s.Problems.Any(p => p.StartsWith(RevParseProblem, StringComparison.Ordinal)));

        var snapshot = store.Current;
        Assert.Contains(snapshot.Problems, p => p.StartsWith(RevParseProblem, StringComparison.Ordinal));
        Assert.Null(snapshot.Git.RepoRoot);
        Assert.Empty(snapshot.Commands);
        using var host = UiTestHost.Start([new GitPage()], snapshot);
        Assert.Contains("git unavailable: " + RevParseProblem, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_insight_sources_are_the_git_reader_the_wmi_process_lister_and_the_command_log_reader()
    {
        var sources = AppRunner.CreateInsightSources();

        Assert.IsType<GitReader>(sources.Git);
        Assert.IsType<WmiProcessLister>(sources.Processes);
        Assert.IsType<CommandLogReader>(sources.Commands);

        // Never polled, so no git process and no WMI query; disposing the store disposes the git reader.
        var store = AppRunner.CreateStore(_temp.Path, claudeDir: _temp.Folder("claude"), copilotDir: _temp.Folder("copilot"), sources: sources);
        store.Dispose();
    }

    /// <summary>Waits until <paramref name="condition"/> holds for the store's current snapshot, at most 10 seconds.</summary>
    private static async Task Until(RunStore store, Func<RunSnapshot, bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition(store.Current) && watch.Elapsed < Patience)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Assert.True(condition(store.Current), $"not published within {Patience}; problems: {string.Join(" | ", store.Current.Problems)}");
    }
}

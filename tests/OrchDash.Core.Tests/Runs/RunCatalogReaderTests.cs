using OrchDash.Core.Model;
using OrchDash.Core.Runs;
using OrchDash.Core.Tests.Fixtures;
using Xunit;

namespace OrchDash.Core.Tests.Runs;

// Spec 31.3-31.5: the current run and the archived runs of a repo.
public sealed class RunCatalogReaderTests : IDisposable
{
    private static readonly string[] RunFiles = ["tasks.json", "state.json", "progress.md"];

    private readonly TempRuns _runs = new();

    public RunCatalogReaderTests()
    {
        _runs.CopyRun(_runs.RepoPath, FixturePaths.CopilotRunDir, RunFiles);
        _runs.CopyRun(_runs.Archive("20261001-100000"), FixturePaths.ClaudeRunDir, RunFiles);
        _runs.CopyRun(_runs.Archive("20261003-110000"), FixturePaths.CopilotRunDir, RunFiles);
        _runs.Write(_runs.Archive("20261004-120000"), "progress.md", "2026-10-04 12:00:00  Planning from .orchestrator/spec.md with opus\n");
        Directory.CreateDirectory(_runs.Archive("junk"));
        _runs.Write(_runs.Archive("20261005-120000"), "tasks.json", "not json");
    }

    public void Dispose() => _runs.Dispose();

    private static DateTimeOffset Local(int month, int day, int hour, int minute, int second) =>
        new(new DateTime(2026, month, day, hour, minute, second, DateTimeKind.Local));

    [Fact]
    public void The_current_run_comes_first_then_the_archived_runs_by_stamp_descending()
    {
        var catalog = RunCatalogReader.List(_runs.RepoPath);

        Assert.Null(catalog.Problem);
        Assert.Equal([null, "20261005-120000", "20261004-120000", "20261003-110000", "20261001-100000"],
            catalog.Runs.Select(r => r.Stamp));
        Assert.Equal([_runs.RepoPath, _runs.Archive("20261005-120000"), _runs.Archive("20261004-120000"),
                      _runs.Archive("20261003-110000"), _runs.Archive("20261001-100000")],
            catalog.Runs.Select(r => r.RepoPath));
    }

    [Fact]
    public void A_claude_run_is_filled_from_its_files()
    {
        var entry = RunCatalogReader.List(_runs.RepoPath).Runs.Single(r => r.Stamp == "20261001-100000");

        // progress.md line 1 is the skeleton entry "... with claude-opus-5-5"; line 25, the later
        // "Planning from .orchestrator/spec.md with claude-fable-5-1", decides.
        Assert.Equal(new RunEntry(_runs.Archive("20261001-100000"), "20261001-100000", "spec.md",
                Local(10, 1, 10, 4, 33), Local(10, 1, 11, 19, 15), 24, 24, 0, Provider.Claude, "claude-fable-5-1", null),
            entry);
    }

    [Fact]
    public void A_copilot_run_is_filled_from_its_files_as_current_and_as_archived_run()
    {
        var runs = RunCatalogReader.List(_runs.RepoPath).Runs;

        foreach (var (repoPath, stamp) in new[] { (_runs.RepoPath, (string?)null), (_runs.Archive("20261003-110000"), "20261003-110000") })
        {
            Assert.Equal(new RunEntry(repoPath, stamp, "spec.md",
                    Local(10, 3, 11, 0, 28), Local(10, 3, 11, 53, 38), 5, 5, 0, Provider.Copilot, "gpt-6-sol", null),
                runs.Single(r => r.Stamp == stamp));
        }
    }

    [Fact]
    public void A_run_with_only_a_progress_file_has_the_model_of_its_planning_entry()
    {
        var entry = RunCatalogReader.List(_runs.RepoPath).Runs.Single(r => r.Stamp == "20261004-120000");

        Assert.Equal(new RunEntry(_runs.Archive("20261004-120000"), "20261004-120000", null,
                Local(10, 4, 12, 0, 0), null, 0, 0, 0, Provider.Unknown, "opus", null),
            entry);
    }

    [Fact]
    public void A_tasks_file_that_is_not_json_gives_a_problem_and_no_tasks()
    {
        var entry = RunCatalogReader.List(_runs.RepoPath).Runs.Single(r => r.Stamp == "20261005-120000");

        Assert.NotNull(entry.Problem);
        Assert.StartsWith("tasks.json: ", entry.Problem);
        Assert.DoesNotContain('\n', entry.Problem);
        Assert.Equal(0, entry.TaskCount);
        Assert.Null(entry.Spec);
        Assert.Null(entry.StartedAt);
        Assert.Equal(Provider.Unknown, entry.Provider);
    }

    [Fact]
    public void A_file_that_cannot_be_opened_gives_a_problem_and_the_rest_of_the_entry()
    {
        var progress = Path.Combine(_runs.Archive("20261003-110000"), ".orchestrator", "progress.md");
        RunEntry entry;
        using (new FileStream(progress, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            entry = RunCatalogReader.List(_runs.RepoPath).Runs.Single(r => r.Stamp == "20261003-110000");

        Assert.NotNull(entry.Problem);
        Assert.StartsWith("progress.md: ", entry.Problem);
        Assert.Null(entry.StartedAt);
        Assert.Null(entry.Model);
        Assert.Equal(("spec.md", 5, 5), (entry.Spec, entry.TaskCount, entry.Done));
    }

    [Fact]
    public void An_archived_run_gives_the_same_catalog_as_its_repo()
    {
        var fromRepo = RunCatalogReader.List(_runs.RepoPath);
        var fromArchive = RunCatalogReader.List(_runs.Archive("20261003-110000"));

        Assert.Null(fromArchive.Problem);
        Assert.Equal(fromRepo.Runs, fromArchive.Runs);
    }

    [Fact]
    public void A_run_folder_with_only_the_project_file_gives_no_current_entry()
    {
        foreach (var name in RunFiles)
            File.Delete(Path.Combine(_runs.RepoPath, ".orchestrator", name));
        _runs.Write(_runs.RepoPath, "project.json", "{}");

        var catalog = RunCatalogReader.List(_runs.RepoPath);

        Assert.DoesNotContain(catalog.Runs, r => r.Stamp is null);
        Assert.Equal(4, catalog.Runs.Length);
    }

    [Fact]
    public void A_missing_runs_folder_is_no_problem()
    {
        Directory.Delete(_runs.RunsPath, recursive: true);

        var catalog = RunCatalogReader.List(_runs.RepoPath);

        Assert.Null(catalog.Problem);
        Assert.Null(Assert.Single(catalog.Runs).Stamp);
    }

    [Fact]
    public void A_path_that_does_not_exist_gives_an_empty_catalog()
    {
        var catalog = RunCatalogReader.List(Path.Combine(_runs.Root, "Missing"));

        Assert.Empty(catalog.Runs);
        Assert.Null(catalog.Problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\X\0Y")]
    public void An_empty_or_invalid_path_gives_an_empty_catalog(string repoPath)
    {
        var catalog = RunCatalogReader.List(repoPath);

        Assert.Empty(catalog.Runs);
        Assert.Null(catalog.Problem);
    }

    [Fact]
    public void Listing_changes_nothing()
    {
        var before = _runs.Listing();

        RunCatalogReader.List(_runs.RepoPath);
        RunCatalogReader.List(_runs.Archive("20261003-110000"));

        Assert.Equal(before, _runs.Listing());
    }
}

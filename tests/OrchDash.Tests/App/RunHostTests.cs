using System.Collections.Concurrent;
using System.Diagnostics;
using OrchDash.App;
using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;

namespace OrchDash.Tests.App;

// Spec 34.2 and 34.3: the run host on a temp tree with <temp>\Repo, a copy of claude-run, and
// <temp>\Repo.runs\20261003-110000, a copy of copilot-run. Only the .orchestrator folders are copied. The stores get the
// command-log reader only: the copies lie outside any work tree, but a git reader would still start processes.
public sealed class RunHostTests : IDisposable
{
    private const string Stamp = "20261003-110000";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly TempFolder _temp = new();
    private readonly string _repo;
    private readonly string _archive;
    private readonly ConcurrentQueue<(string Path, RunStore Store)> _created = new();
    private readonly ManualResetEventSlim _release = new(true);

    public RunHostTests()
    {
        _repo = Path.Combine(_temp.Path, "Repo");
        _archive = Path.Combine(_temp.Path, "Repo.runs", Stamp);
        _temp.Copy(Path.Combine(FixtureRuns.ClaudeRepo, ".orchestrator"), Path.Combine("Repo", ".orchestrator"));
        _temp.Copy(Path.Combine(FixtureRuns.CopilotRepo, ".orchestrator"), Path.Combine("Repo.runs", Stamp, ".orchestrator"));
    }

    public void Dispose()
    {
        _release.Set();
        foreach (var (_, store) in _created)
        {
            store.Dispose();
        }
        _release.Dispose();
        _temp.Dispose();
    }

    /// <summary>
    /// The factory of the tests: a fixture store with the command-log reader only, remembered in <see cref="_created"/>.
    /// A call for the archive waits for <see cref="_release"/>, which is set unless a test holds a switch open.
    /// </summary>
    private RunStore Create(string path)
    {
        if (RunPaths.Same(path, _archive))
        {
            _release.Wait(Patience);
        }
        var store = FixtureRuns.CreateStore(path, sources: FixtureRuns.CommandLogsOnly());
        _created.Enqueue((path, store));
        return store;
    }

    private RunHost StartedHost()
    {
        var host = new RunHost(_repo, Create);
        host.Start();
        return host;
    }

    private RunStore StoreOf(string path) => Assert.Single(_created, c => c.Path == path).Store;

    [Fact]
    public void Before_Start_Latest_is_one_cached_empty_snapshot_and_no_store_exists()
    {
        using var host = new RunHost(_repo, Create);

        var latest = host.Latest();

        Assert.Equal(RunSnapshot.Empty(_repo), latest);
        Assert.Same(latest, host.Latest());
        Assert.Equal(_repo, host.RepoPath);
        Assert.Empty(_created);
        Assert.Null(host.Runs);
        Assert.Null(host.Loading);
        Assert.Null(host.Problem);
    }

    [Fact]
    public void Start_creates_and_starts_the_store_of_the_repo_on_the_calling_thread()
    {
        using var host = StartedHost();

        Assert.Equal(_repo, Assert.Single(_created).Path);
        Assert.Equal(_repo, host.RepoPath);
        Assert.Equal(_repo, host.Latest().RepoPath);
        // Start polls once before it returns.
        Assert.Equal(RunPhase.Finished, host.Latest().Run.Phase);
        Assert.Same(StoreOf(_repo).Current, host.Latest());
    }

    [Fact]
    public void SwitchTo_loads_the_archive_off_the_calling_thread_and_disposes_the_old_store()
    {
        using var host = StartedHost();

        host.SwitchTo(_archive);
        var loading = host.Loading;

        // Loading is set before SwitchTo returns and cleared only after the switch, so null means it is done already.
        Assert.True(loading == Stamp || (loading is null && host.RepoPath == _archive), $"Loading was {loading}");
        Until(() => host.Loading is null && host.Latest().RepoPath == _archive);
        Assert.Equal(_archive, host.RepoPath);
        Assert.Null(host.Problem);
        Assert.Same(StoreOf(_archive).Current, host.Latest());
        Assert.Throws<ObjectDisposedException>(() => StoreOf(_repo).Poll());
        StoreOf(_archive).Poll();
    }

    [Fact]
    public void A_second_SwitchTo_while_loading_is_ignored()
    {
        using var host = StartedHost();
        _release.Reset();

        host.SwitchTo(_archive);
        host.SwitchTo(Path.Combine(_temp.Path, "Other"));

        Assert.Equal(Stamp, host.Loading);
        Assert.Equal(_repo, host.Latest().RepoPath);
        _release.Set();
        Until(() => host.Loading is null);
        Assert.Equal(_archive, host.Latest().RepoPath);
        Assert.Equal([_repo, _archive], _created.Select(c => c.Path));
    }

    [Fact]
    public void SwitchTo_the_current_path_is_ignored()
    {
        using var host = StartedHost();

        host.SwitchTo(_repo + Path.DirectorySeparatorChar);
        host.SwitchTo(_repo.ToUpperInvariant());

        Assert.Null(host.Loading);
        Assert.Single(_created);
        Assert.Equal(_repo, host.RepoPath);
    }

    [Fact]
    public void A_throwing_factory_sets_the_problem_and_keeps_the_store()
    {
        using var host = new RunHost(_repo, path => RunPaths.Same(path, _archive) ? throw new IOException("The archive is gone.") : Create(path));
        host.Start();

        host.SwitchTo(_archive);
        Until(() => host.Loading is null);

        Assert.Equal("The archive is gone.", host.Problem);
        Assert.Equal(_repo, host.RepoPath);
        Assert.Equal(_repo, host.Latest().RepoPath);
        StoreOf(_repo).Poll();
    }

    [Fact]
    public void A_store_whose_Start_throws_is_disposed_and_its_message_is_the_problem()
    {
        using var host = new RunHost(_repo, path =>
        {
            var store = Create(path);
            if (RunPaths.Same(path, _archive))
            {
                // A second Start throws InvalidOperationException.
                store.Start();
            }
            return store;
        });
        host.Start();

        host.SwitchTo(_archive);
        Until(() => host.Loading is null);

        Assert.Equal("The store has already been started.", host.Problem);
        Assert.Equal(_repo, host.Latest().RepoPath);
        Assert.Throws<ObjectDisposedException>(() => StoreOf(_archive).Poll());
        StoreOf(_repo).Poll();
    }

    [Fact]
    public void RefreshRuns_publishes_a_new_catalog_with_the_current_run_first_and_then_the_archive()
    {
        using var host = StartedHost();

        host.RefreshRuns();
        Until(() => host.Runs is not null);

        var first = host.Runs!;
        Assert.Null(first.Problem);
        Assert.Collection(
            first.Runs,
            current =>
            {
                Assert.True(RunPaths.Same(_repo, current.RepoPath), current.RepoPath);
                Assert.Null(current.Stamp);
                Assert.Equal(Provider.Claude, current.Provider);
                Assert.Equal(24, current.TaskCount);
            },
            archive =>
            {
                Assert.True(RunPaths.Same(_archive, archive.RepoPath), archive.RepoPath);
                Assert.Equal(Stamp, archive.Stamp);
                Assert.Equal(Provider.Copilot, archive.Provider);
                Assert.Equal(5, archive.TaskCount);
            });

        host.RefreshRuns();
        Until(() => !ReferenceEquals(first, host.Runs));
        Assert.Equal(2, host.Runs!.Runs.Length);
    }

    [Fact]
    public void A_later_successful_listing_clears_the_problem()
    {
        using var host = new RunHost(_repo, path => RunPaths.Same(path, _archive) ? throw new IOException("The archive is gone.") : Create(path));
        host.Start();
        host.SwitchTo(_archive);
        Until(() => host.Loading is null);
        Assert.NotNull(host.Problem);

        host.RefreshRuns();
        Until(() => host.Runs is not null);

        Assert.Null(host.Problem);
    }

    [Fact]
    public void Dispose_disposes_the_current_store_and_Latest_keeps_its_last_snapshot()
    {
        var host = StartedHost();
        var last = host.Latest();

        host.Dispose();

        Assert.Throws<ObjectDisposedException>(() => StoreOf(_repo).Poll());
        Assert.Same(last, host.Latest());
        host.SwitchTo(_archive);
        Assert.Null(host.Loading);
    }

    [Fact]
    public void A_switch_that_ends_after_Dispose_disposes_its_new_store()
    {
        var host = StartedHost();
        _release.Reset();
        host.SwitchTo(_archive);

        host.Dispose();
        _release.Set();
        Until(() => host.Loading is null);

        Assert.Throws<ObjectDisposedException>(() => StoreOf(_repo).Poll());
        Assert.Throws<ObjectDisposedException>(() => StoreOf(_archive).Poll());
        Assert.Equal(_repo, host.Latest().RepoPath);
    }

    /// <summary>Waits until <paramref name="condition"/> holds, at most 10 seconds.</summary>
    private static void Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < Patience)
        {
            Thread.Sleep(20);
        }
        Assert.True(condition(), $"not within {Patience}");
    }
}

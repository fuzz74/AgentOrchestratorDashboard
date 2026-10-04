using OrchDash.App;
using Xunit;

namespace OrchDash.Tests.App;

// Spec 1.1: the nearest folder at or above the start path that contains .orchestrator.
public sealed class RepoLocatorTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_folder_with_orchestrator_is_its_own_repo()
    {
        var repo = _temp.Folder("repo");
        _temp.Folder("repo/.orchestrator");

        Assert.Equal(repo, RepoLocator.Find(repo));
        Assert.Equal(repo, RepoLocator.Find(repo + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void The_orchestrator_folder_two_levels_up_is_found()
    {
        var repo = _temp.Folder("repo");
        _temp.Folder("repo/.orchestrator");
        var start = _temp.Folder("repo/src/deep");

        Assert.Equal(repo, RepoLocator.Find(start));
    }

    [Fact]
    public void The_nearest_orchestrator_folder_wins()
    {
        _temp.Folder("outer/.orchestrator");
        var inner = _temp.Folder("outer/inner");
        _temp.Folder("outer/inner/.orchestrator");

        Assert.Equal(inner, RepoLocator.Find(_temp.Folder("outer/inner/src")));
    }

    [Fact]
    public void A_file_starts_the_search_at_its_folder()
    {
        var repo = _temp.Folder("repo");
        _temp.Folder("repo/.orchestrator");
        var file = _temp.Write("repo/src/Program.cs", "");

        Assert.Equal(repo, RepoLocator.Find(file));
    }

    [Fact]
    public void A_file_named_orchestrator_does_not_count()
    {
        _temp.Write("repo/.orchestrator", "");

        Assert.Null(RepoLocator.Find(_temp.Folder("repo/src")));
    }

    [Fact]
    public void No_orchestrator_folder_anywhere_gives_null()
    {
        Assert.Null(RepoLocator.Find(_temp.Folder("plain/src")));
    }

    [Fact]
    public void A_path_that_does_not_exist_gives_null()
    {
        _temp.Folder("repo/.orchestrator");

        Assert.Null(RepoLocator.Find(Path.Combine(_temp.Path, "repo", "missing")));
    }

    [Fact]
    public void A_relative_path_is_resolved_against_the_current_directory()
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, FixtureRuns.ClaudeRepo);

        Assert.False(Path.IsPathRooted(relative));
        Assert.Equal(FixtureRuns.ClaudeRepo, RepoLocator.Find(Path.Combine(relative, ".orchestrator", "logs")));
    }
}

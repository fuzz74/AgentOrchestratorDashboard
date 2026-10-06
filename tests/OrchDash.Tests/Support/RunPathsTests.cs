using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class RunPathsTests
{
    private const string Repo = @"C:\X\Repo";
    private const string Archive = @"C:\X\Repo.runs\20261006-195310";
    private const string Stamp = "20261006-195310";

    [Theory]
    [InlineData(Repo)]
    [InlineData(@"C:\X\Repo\")]
    public void A_repo_is_its_own_root_without_a_stamp(string path)
    {
        Assert.Equal(Repo, RunPaths.RepoRoot(path));
        Assert.Null(RunPaths.Stamp(path));
    }

    [Theory]
    [InlineData(Archive)]
    [InlineData(@"C:\X\Repo.runs\20261006-195310\")]
    [InlineData("C:/X/Repo.runs/20261006-195310")]
    [InlineData(@"C:\X\Repo.RUNS\20261006-195310")]
    public void An_archived_run_has_the_repo_beside_its_runs_folder_as_root_and_its_folder_name_as_stamp(string path)
    {
        Assert.Equal(Repo, RunPaths.RepoRoot(path));
        Assert.Equal(Stamp, RunPaths.Stamp(path));
    }

    [Fact]
    public void A_folder_named_runs_is_its_own_root()
    {
        Assert.Equal(@"C:\X\Foo.runs", RunPaths.RepoRoot(@"C:\X\Foo.runs"));
        Assert.Null(RunPaths.Stamp(@"C:\X\Foo.runs"));
    }

    [Fact]
    public void A_parent_named_only_runs_is_no_runs_folder()
    {
        Assert.Equal(@"C:\X\.runs\20261006-195310", RunPaths.RepoRoot(@"C:\X\.runs\20261006-195310"));
        Assert.Null(RunPaths.Stamp(@"C:\X\.runs\20261006-195310"));
    }

    [Theory]
    [InlineData(Repo)]
    [InlineData(@"C:\X\Repo\")]
    [InlineData(Archive)]
    [InlineData("C:/X/Repo.runs/20261006-195310/")]
    public void WorkDir_is_the_task_worktree_beside_the_repo_root(string path)
    {
        Assert.Equal(@"C:\X\Repo.worktrees\alpha", RunPaths.WorkDir(path, "alpha"));
        Assert.Equal(Repo, RunPaths.WorkDir(path, null));
    }

    [Theory]
    [InlineData(Repo, Repo)]
    [InlineData(Repo, @"c:\x\REPO")]
    [InlineData(Repo, @"C:\X\Repo\")]
    [InlineData("C:/X/Repo.runs/20261006-195310/", Archive)]
    public void Same_ignores_case_slashes_and_trailing_separators(string a, string b)
    {
        Assert.True(RunPaths.Same(a, b));
        Assert.True(RunPaths.Same(b, a));
    }

    [Theory]
    [InlineData(Repo, @"C:\X\Other")]
    [InlineData(Repo, Archive)]
    [InlineData(Repo, @"C:\X\Repo.worktrees\alpha")]
    public void Same_is_false_for_different_folders(string a, string b)
    {
        Assert.False(RunPaths.Same(a, b));
    }

    [Fact]
    public void An_empty_path_does_not_throw_and_is_used_as_given()
    {
        Assert.Equal("", RunPaths.RepoRoot(""));
        Assert.Null(RunPaths.Stamp(""));
        Assert.Equal("", RunPaths.WorkDir("", null));
        Assert.Equal(@".worktrees\alpha", RunPaths.WorkDir("", "alpha"));
        Assert.True(RunPaths.Same("", ""));
        Assert.False(RunPaths.Same("", Repo));
    }

    [Fact]
    public void An_invalid_path_does_not_throw_and_is_used_as_given()
    {
        const string invalid = "C:\\X\\Re\0po";

        Assert.Equal(invalid, RunPaths.RepoRoot(invalid));
        Assert.Null(RunPaths.Stamp(invalid));
        Assert.True(RunPaths.Same(invalid, invalid));
    }
}

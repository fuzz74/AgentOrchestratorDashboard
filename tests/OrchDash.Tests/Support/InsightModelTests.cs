using System.Runtime.Versioning;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class InsightModelTests
{
    [Fact]
    public void GitInfo_Empty_has_nulls_and_empty_arrays()
    {
        var empty = GitInfo.Empty;

        Assert.Null(empty.ReadAt);
        Assert.Null(empty.RepoRoot);
        Assert.Null(empty.IntegrationBranch);
        Assert.Null(empty.IntegrationTip);
        Assert.Null(empty.BaseBranch);
        Assert.Null(empty.BaseTip);
        Assert.False(empty.Worktrees.IsDefault);
        Assert.Empty(empty.Worktrees);
        Assert.False(empty.Branches.IsDefault);
        Assert.Empty(empty.Branches);
        Assert.False(empty.Tasks.IsDefault);
        Assert.Empty(empty.Tasks);
        Assert.Null(empty.Problem);
        Assert.Same(empty, GitInfo.Empty);
    }

    [Fact]
    public void ProcessInfo_Empty_has_no_sample_and_no_processes()
    {
        var empty = ProcessInfo.Empty;

        Assert.Null(empty.SampledAt);
        Assert.False(empty.Processes.IsDefault);
        Assert.Empty(empty.Processes);
        Assert.Null(empty.Problem);
        Assert.Same(empty, ProcessInfo.Empty);
    }

    [Fact]
    public void InsightSources_None_has_no_sources()
    {
        var none = InsightSources.None;

        Assert.Null(none.Git);
        Assert.Null(none.Processes);
        Assert.Null(none.Commands);
        Assert.Same(none, InsightSources.None);
    }

    [Fact]
    public void An_empty_snapshot_has_no_insight_data()
    {
        var snapshot = RunSnapshot.Empty("x");

        Assert.Same(GitInfo.Empty, snapshot.Git);
        Assert.Same(ProcessInfo.Empty, snapshot.Processes);
        Assert.False(snapshot.Commands.IsDefault);
        Assert.Empty(snapshot.Commands);
    }

    [Fact]
    public void DiffStat_sums_the_files_and_counts_a_binary_file_as_zero()
    {
        var stat = new DiffStat([
            new DiffFile("src/A.cs", 38, 0),
            new DiffFile("tests/ATests.cs", 22, 3),
            new DiffFile("assets/logo.png", null, null),
        ]);

        Assert.Equal(60, stat.Added);
        Assert.Equal(3, stat.Removed);
        Assert.Equal(0, new DiffStat([]).Added);
        Assert.Equal(0, new DiffStat([]).Removed);
    }

    [Fact]
    public void TaskDetails_OwnsOverlap_is_public()
    {
        Assert.True(TaskDetails.OwnsOverlap(["src/Alpha/**"], ["src/Alpha/Parser.cs"]));
        Assert.False(TaskDetails.OwnsOverlap(["src/Alpha/**"], ["src/Beta/**"]));
    }

    [Fact]
    public void The_assemblies_are_marked_windows_only()
    {
        Assert.Contains(SupportedPlatforms(typeof(RunSnapshot)), name => name == "windows");
        Assert.Contains(SupportedPlatforms(typeof(Look)), name => name == "windows");
    }

    private static IEnumerable<string> SupportedPlatforms(Type type) =>
        type.Assembly.GetCustomAttributes(typeof(SupportedOSPlatformAttribute), false)
            .Cast<SupportedOSPlatformAttribute>()
            .Select(attribute => attribute.PlatformName);
}

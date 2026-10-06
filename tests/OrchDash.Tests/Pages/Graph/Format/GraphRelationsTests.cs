using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Pages.Graph.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.Graph.Format;

public sealed class GraphRelationsTests
{
    private readonly ImmutableArray<TaskView> _tasks = SampleRun.CreateEnriched().Tasks;

    [Fact]
    public void Direct_deps_and_dependents()
    {
        Assert.Equal(["alpha"], GraphRelations.Deps(_tasks, "beta"));
        Assert.Equal(["epsilon"], GraphRelations.Dependents(_tasks, "beta"));
        Assert.Empty(GraphRelations.Deps(_tasks, "alpha"));
        Assert.Equal(["beta"], GraphRelations.Dependents(_tasks, "alpha"));
        Assert.Empty(GraphRelations.Deps(_tasks, "zeta"));
        Assert.Empty(GraphRelations.Dependents(_tasks, "zeta"));
    }

    [Fact]
    public void Deps_keep_the_deps_order_and_skip_ids_that_are_not_tasks()
    {
        ImmutableArray<TaskView> tasks = [.. _tasks.Select(t => t.Id == "epsilon" ? t with { Deps = ["gamma", "zeta", "alpha"] } : t)];

        Assert.Equal(["gamma", "alpha"], GraphRelations.Deps(tasks, "epsilon"));
        Assert.Equal(["beta", "epsilon"], GraphRelations.Dependents(tasks, "alpha"));
    }

    [Fact]
    public void All_deps_and_dependents_are_direct_and_indirect_in_snapshot_order()
    {
        Assert.Equal(["alpha", "beta"], GraphRelations.AllDeps(_tasks, "epsilon"));
        Assert.Equal(["beta", "epsilon"], GraphRelations.AllDependents(_tasks, "alpha"));
        Assert.Equal(["delta"], GraphRelations.AllDependents(_tasks, "gamma"));
        Assert.Empty(GraphRelations.AllDeps(_tasks, "gamma"));
    }

    [Fact]
    public void A_cycle_ends_and_leaves_out_the_task_itself()
    {
        ImmutableArray<TaskView> tasks = [.. _tasks.Select(t => t.Id == "alpha" ? t with { Deps = ["epsilon", "alpha"] } : t)];

        Assert.Equal(["alpha", "beta"], GraphRelations.AllDeps(tasks, "epsilon"));
        Assert.Equal(["alpha", "beta"], GraphRelations.AllDependents(tasks, "epsilon"));
        Assert.Equal(["epsilon"], GraphRelations.Deps(tasks, "alpha"));
    }
}

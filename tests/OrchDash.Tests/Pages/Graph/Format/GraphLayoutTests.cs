using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Pages.Graph.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.Graph.Format;

public sealed class GraphLayoutTests
{
    private readonly ImmutableArray<TaskView> _tasks = SampleRun.CreateEnriched().Tasks;

    [Fact]
    public void Build_makes_one_column_per_wave_with_its_tasks_in_snapshot_order()
    {
        var layout = GraphLayout.Build(_tasks);

        Assert.Equal(["W1", "W2", "W3"], layout.Columns.Select(c => c.Title));
        Assert.Equal([1, 2, 3], layout.Columns.Select(c => c.Wave));
        Assert.Equal(["alpha", "gamma"], layout.Columns[0].TaskIds);
        Assert.Equal(["beta", "delta"], layout.Columns[1].TaskIds);
        Assert.Equal(["epsilon"], layout.Columns[2].TaskIds);
        Assert.Equal(10, layout.Width);
        Assert.Equal(3, layout.BusRow);
        Assert.Equal(4, layout.Height);
        Assert.Equal(36, layout.TotalWidth);
        Assert.Equal([0, 13, 26], Enumerable.Range(0, 3).Select(layout.ColumnStart));
        Assert.Equal([2, 2, 1], Enumerable.Range(0, 3).Select(layout.Rows));
    }

    [Fact]
    public void Build_orders_the_waves_ascending_and_skips_missing_ones()
    {
        var layout = GraphLayout.Build(
        [
            Task("epsilon") with { Wave = 5 },
            Task("alpha"),
            Task("beta") with { Wave = 5 },
        ]);

        Assert.Equal(["W1", "W5"], layout.Columns.Select(c => c.Title));
        Assert.Equal(["epsilon", "beta"], layout.Columns[1].TaskIds);
    }

    [Fact]
    public void Build_without_tasks_has_no_columns()
    {
        var layout = GraphLayout.Build([]);

        Assert.Empty(layout.Columns);
        Assert.Equal(3, layout.Width);
        Assert.Equal(1, layout.BusRow);
        Assert.Equal(2, layout.Height);
        Assert.Equal(0, layout.TotalWidth);
        Assert.Null(layout.CardAt(0, 1));
        Assert.Null(layout.NearestInColumn(0, 1));
    }

    [Fact]
    public void Position_gives_the_column_and_row_of_a_card()
    {
        var layout = GraphLayout.Build(_tasks);

        Assert.Equal((0, 1), layout.Position("alpha"));
        Assert.Equal((0, 2), layout.Position("gamma"));
        Assert.Equal((1, 2), layout.Position("delta"));
        Assert.Equal((2, 1), layout.Position("epsilon"));
        Assert.Null(layout.Position("zeta"));
    }

    [Fact]
    public void TaskAt_gives_the_card_of_a_column_and_row()
    {
        var layout = GraphLayout.Build(_tasks);

        Assert.Equal("beta", layout.TaskAt(1, 1));
        Assert.Equal("delta", layout.TaskAt(1, 2));
        Assert.Null(layout.TaskAt(1, 0));
        Assert.Null(layout.TaskAt(2, 2));
        Assert.Null(layout.TaskAt(3, 1));
        Assert.Null(layout.TaskAt(-1, 1));
    }

    [Fact]
    public void CardAt_maps_a_cell_to_the_card_under_it()
    {
        var layout = GraphLayout.Build(_tasks);

        Assert.Equal("alpha", layout.CardAt(0, 1));
        Assert.Equal("alpha", layout.CardAt(9, 1));
        Assert.Null(layout.CardAt(10, 1));
        Assert.Null(layout.CardAt(12, 1));
        Assert.Equal("beta", layout.CardAt(13, 1));
        Assert.Equal("delta", layout.CardAt(22, 2));
        Assert.Equal("epsilon", layout.CardAt(35, 1));
        Assert.Null(layout.CardAt(26, 2));
        Assert.Null(layout.CardAt(36, 1));
        Assert.Null(layout.CardAt(0, 0));
        Assert.Null(layout.CardAt(0, 3));
        Assert.Null(layout.CardAt(-1, 1));
    }

    [Fact]
    public void NearestInColumn_gives_the_card_whose_row_is_nearest()
    {
        var layout = GraphLayout.Build(_tasks);

        Assert.Equal("beta", layout.NearestInColumn(1, 1));
        Assert.Equal("delta", layout.NearestInColumn(1, 2));
        Assert.Equal("epsilon", layout.NearestInColumn(2, 2));
        Assert.Equal("gamma", layout.NearestInColumn(0, 7));
        Assert.Equal("alpha", layout.NearestInColumn(0, 0));
        Assert.Null(layout.NearestInColumn(3, 1));
    }

    private TaskView Task(string id) => _tasks.Single(t => t.Id == id);
}

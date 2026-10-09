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

    // alpha (row 1) has two child lines (rows 2, 3) above gamma (row 4); beta (row 1) has one (row 2) above delta (row 3).
    private GraphLayout WithChildLines() =>
        GraphLayout.Build(_tasks, new Dictionary<string, int> { ["alpha"] = 2, ["beta"] = 1 });

    [Fact]
    public void Child_lines_move_the_later_cards_of_their_column_down()
    {
        var layout = WithChildLines();

        Assert.Equal([2, 0], layout.Columns[0].ChildLines);
        Assert.Equal([1, 0], layout.Columns[1].ChildLines);
        Assert.Equal([0], layout.Columns[2].ChildLines);
        Assert.Equal([4, 3, 1], layout.Columns.Select(c => c.Lines));
        Assert.Equal((0, 1), layout.Position("alpha"));
        Assert.Equal((0, 4), layout.Position("gamma"));
        Assert.Equal((1, 1), layout.Position("beta"));
        Assert.Equal((1, 3), layout.Position("delta"));
        Assert.Equal((2, 1), layout.Position("epsilon"));
        Assert.Equal(4, layout.CardRow(0, 1));
        Assert.Equal([2, 2, 1], Enumerable.Range(0, 3).Select(layout.Rows));
        Assert.Equal(10, layout.Width);
    }

    [Fact]
    public void The_bus_row_lies_below_the_tallest_column_with_its_child_lines()
    {
        var layout = WithChildLines();

        Assert.Equal(5, layout.BusRow);
        Assert.Equal(6, layout.Height);
        Assert.Equal(36, layout.TotalWidth);
    }

    [Fact]
    public void A_task_without_an_entry_has_no_child_lines()
    {
        var empty = GraphLayout.Build(_tasks, new Dictionary<string, int>());
        var zeta = GraphLayout.Build(_tasks, new Dictionary<string, int> { ["zeta"] = 4, ["gamma"] = 0, ["delta"] = -1 });

        foreach (var layout in new[] { empty, zeta })
        {
            Assert.Equal(3, layout.BusRow);
            Assert.Equal((0, 2), layout.Position("gamma"));
            Assert.Equal((1, 2), layout.Position("delta"));
            Assert.Null(layout.ChildAt(0, 2));
        }
    }

    [Fact]
    public void TaskAt_and_CardAt_give_nothing_on_a_child_line()
    {
        var layout = WithChildLines();

        Assert.Equal("alpha", layout.TaskAt(0, 1));
        Assert.Null(layout.TaskAt(0, 2));
        Assert.Null(layout.TaskAt(0, 3));
        Assert.Equal("gamma", layout.TaskAt(0, 4));
        Assert.Null(layout.TaskAt(0, 5));
        Assert.Null(layout.TaskAt(1, 2));
        Assert.Equal("delta", layout.TaskAt(1, 3));

        Assert.Equal("alpha", layout.CardAt(9, 1));
        Assert.Null(layout.CardAt(0, 2));
        Assert.Null(layout.CardAt(9, 3));
        Assert.Equal("gamma", layout.CardAt(0, 4));
        Assert.Null(layout.CardAt(13, 2));
        Assert.Equal("delta", layout.CardAt(22, 3));
        Assert.Null(layout.CardAt(0, 5));
    }

    [Fact]
    public void ChildAt_maps_a_cell_to_the_child_line_under_it()
    {
        var layout = WithChildLines();

        Assert.Equal(("alpha", 0), layout.ChildAt(0, 2));
        Assert.Equal(("alpha", 1), layout.ChildAt(9, 3));
        Assert.Equal(("beta", 0), layout.ChildAt(13, 2));
        Assert.Equal(("beta", 0), layout.ChildAt(22, 2));
        Assert.Null(layout.ChildAt(10, 2));
        Assert.Null(layout.ChildAt(12, 3));
        Assert.Null(layout.ChildAt(0, 0));
        Assert.Null(layout.ChildAt(0, 1));
        Assert.Null(layout.ChildAt(0, 4));
        Assert.Null(layout.ChildAt(0, 5));
        Assert.Null(layout.ChildAt(13, 3));
        Assert.Null(layout.ChildAt(26, 2));
        Assert.Null(layout.ChildAt(39, 2));
        Assert.Null(layout.ChildAt(-1, 2));
    }

    [Fact]
    public void NearestInColumn_measures_to_the_card_lines_and_prefers_the_upper_card_of_two_as_near()
    {
        var layout = WithChildLines();

        Assert.Equal("alpha", layout.NearestInColumn(0, 2));
        Assert.Equal("gamma", layout.NearestInColumn(0, 3));
        Assert.Equal("gamma", layout.NearestInColumn(0, 9));
        Assert.Equal("alpha", layout.NearestInColumn(0, 0));
        Assert.Equal("beta", layout.NearestInColumn(1, 2));
        Assert.Equal("delta", layout.NearestInColumn(1, 4));
        Assert.Equal("epsilon", layout.NearestInColumn(2, 4));
        Assert.Null(layout.NearestInColumn(3, 1));
        Assert.Null(layout.NearestInColumn(-1, 1));
    }

    private TaskView Task(string id) => _tasks.Single(t => t.Id == id);
}

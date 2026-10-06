using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Pages.Graph.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.Graph.Format;

public sealed class GraphTextTests
{
    private const int Wide = 200;

    private readonly RunSnapshot _run = SampleRun.CreateEnriched();

    private static readonly string Gap = new(' ', 3);
    private static readonly string NoCard = new(' ', 10);

    private TaskView Task(string id) => _run.Tasks.Single(t => t.Id == id);

    private static ImmutableArray<string> Plain(ImmutableArray<TaskView> tasks, string? selectedId, int width = Wide) =>
        GraphText.PlainLines(GraphLayout.Build(tasks), tasks, selectedId, width);

    private static ImmutableArray<string> Markup(ImmutableArray<TaskView> tasks, string? selectedId, int width = Wide) =>
        GraphText.Lines(GraphLayout.Build(tasks), tasks, selectedId, width);

    private static string Spaces(int n) => new(' ', n);

    // alpha, gamma (W1); beta deps alpha, delta (W2); epsilon deps alpha (W2); beta's dep changed by the caller.
    private ImmutableArray<TaskView> OneGutterGraph() =>
    [
        Task("alpha"),
        Task("gamma"),
        Task("beta") with { Deps = ["gamma"] },
        Task("delta") with { Deps = [] },
        Task("epsilon") with { Wave = 2, Deps = ["alpha"] },
    ];

    // As the sample, but epsilon (W3) depends on alpha (W1): an edge along the bus row.
    private ImmutableArray<TaskView> BusGraph() =>
        [.. _run.Tasks.Select(t => t.Id == "epsilon" ? t with { Deps = ["alpha"] } : t)];

    [Fact]
    public void With_alpha_selected_the_cards_are_marked_and_the_edge_to_beta_is_drawn()
    {
        Assert.Equal(
        [
            "W1" + Spaces(11) + "W2" + Spaces(11) + "W3" + Spaces(8),
            "●✔ alpha  " + "───" + "▸▶ beta   " + Gap + "▸· epsilon",
            " ✖ gamma  " + Gap + " ⊘ delta  " + Gap + NoCard,
            Spaces(36),
        ], Plain(_run.Tasks, "alpha"));
    }

    [Fact]
    public void Row_one_with_alpha_selected_reads_cell_by_cell()
    {
        var row = Plain(_run.Tasks, "alpha")[1];

        Assert.Equal(36, row.Length);
        Assert.Equal("●✔ alpha", row[..8]);
        Assert.Equal("  ", row[8..10]);
        Assert.Equal('─', row[10]);
        Assert.Equal('─', row[11]);
        Assert.Equal('─', row[12]);
        Assert.Equal("▸▶ beta", row[13..20]);
        Assert.Equal(Spaces(6), row[20..26]);
        Assert.Equal("▸· epsilon", row[26..]);
    }

    [Fact]
    public void Markup_colours_the_titles_the_selected_card_the_marked_cards_and_the_edges()
    {
        Assert.Equal(
        [
            "[bold]W1[/]" + Spaces(11) + "[bold]W2[/]" + Spaces(11) + "[bold]W3[/]" + Spaces(8),
            "[bold]●✔ alpha[/]  [accent]───▸▶ beta[/]      [accent]▸· epsilon[/]",
            "[error] ✖ gamma[/]     [warning] ⊘ delta[/]" + Spaces(15),
            Spaces(36),
        ], Markup(_run.Tasks, "alpha"));
    }

    [Fact]
    public void With_beta_selected_alpha_is_a_dep_epsilon_a_dependent_and_both_edges_are_drawn()
    {
        var plain = Plain(_run.Tasks, "beta");
        var markup = Markup(_run.Tasks, "beta");

        Assert.Equal("◂✔ alpha  " + "───" + "●▶ beta   " + "───" + "▸· epsilon", plain[1]);
        Assert.Equal(" ✖ gamma  " + Gap + " ⊘ delta  " + Gap + NoCard, plain[2]);
        Assert.Equal("[accent]◂✔ alpha[/]  [accent]───[/][bold]●▶ beta[/]   [accent]───▸· epsilon[/]", markup[1]);
    }

    [Fact]
    public void With_gamma_selected_the_edge_to_delta_runs_along_their_row()
    {
        Assert.Equal(
        [
            "W1" + Spaces(11) + "W2" + Spaces(11) + "W3" + Spaces(8),
            " ✔ alpha  " + Gap + " ▶ beta   " + Gap + " · epsilon",
            "●✖ gamma  " + "───" + "▸⊘ delta  " + Gap + NoCard,
            Spaces(36),
        ], Plain(_run.Tasks, "gamma"));
    }

    [Fact]
    public void Without_a_selection_no_card_is_marked_and_no_edge_is_drawn()
    {
        var expected = new[]
        {
            "W1" + Spaces(11) + "W2" + Spaces(11) + "W3" + Spaces(8),
            " ✔ alpha  " + Gap + " ▶ beta   " + Gap + " · epsilon",
            " ✖ gamma  " + Gap + " ⊘ delta  " + Gap + NoCard,
            Spaces(36),
        };

        Assert.Equal(expected, Plain(_run.Tasks, null));
        Assert.Equal(expected, Plain(_run.Tasks, "zeta"));
        Assert.Equal("[success] ✔ alpha[/]     [primary] ▶ beta[/]      [muted] · epsilon[/]", Markup(_run.Tasks, null)[1]);
    }

    [Fact]
    public void A_dependent_on_a_lower_row_turns_down_in_the_gutter()
    {
        Assert.Equal(
        [
            "W1" + Spaces(11) + "W2" + Spaces(8),
            "◂✔ alpha  " + "─┐ " + " ▶ beta   ",
            " ✖ gamma  " + " │ " + " ⊘ delta  ",
            NoCard + " └─" + "●· epsilon",
            Spaces(23),
        ], Plain(OneGutterGraph(), "epsilon"));
    }

    [Fact]
    public void A_dep_on_a_lower_row_turns_up_in_the_gutter()
    {
        var plain = Plain(OneGutterGraph(), "beta");

        Assert.Equal(" ✔ alpha  " + " ┌─" + "●▶ beta   ", plain[1]);
        Assert.Equal("◂✖ gamma  " + "─┘ " + " ⊘ delta  ", plain[2]);
        Assert.Equal(NoCard + Gap + " · epsilon", plain[3]);
        Assert.Equal("[accent]◂✖ gamma[/]  [accent]─┘[/] [warning] ⊘ delta[/]  ", Markup(OneGutterGraph(), "beta")[2]);
    }

    [Fact]
    public void An_edge_across_a_column_runs_along_the_bus_row()
    {
        Assert.Equal(
        [
            "W1" + Spaces(11) + "W2" + Spaces(11) + "W3" + Spaces(8),
            "◂✔ alpha  " + "─┐ " + " ▶ beta   " + " ┌─" + "●· epsilon",
            " ✖ gamma  " + " │ " + " ⊘ delta  " + " │ " + NoCard,
            Spaces(11) + "└" + new string('─', 12) + "┘" + Spaces(11),
        ], Plain(BusGraph(), "epsilon"));
    }

    [Fact]
    public void A_cell_two_edges_draw_with_different_glyphs_is_a_crossing()
    {
        Assert.Equal(
        [
            "W1" + Spaces(11) + "W2" + Spaces(11) + "W3" + Spaces(8),
            "●✔ alpha  " + "─┼─" + "▸▶ beta   " + " ┌─" + "▸· epsilon",
            " ✖ gamma  " + " │ " + " ⊘ delta  " + " │ " + NoCard,
            Spaces(11) + "└" + new string('─', 12) + "┘" + Spaces(11),
        ], Plain(BusGraph(), "alpha"));
        Assert.Equal(
            "[bold]●✔ alpha[/]  [accent]─┼─▸▶ beta[/]    [accent]┌─▸· epsilon[/]",
            Markup(BusGraph(), "alpha")[1]);
    }

    [Fact]
    public void Lines_are_cut_at_the_width()
    {
        Assert.Equal(["W1" + Spaces(10), "●✔ alpha  ──", " ✖ gamma    ", Spaces(12)], Plain(_run.Tasks, "alpha", 12));
        Assert.Equal(["[bold]W1[/]" + Spaces(10), "[bold]●✔ alpha[/]  [accent]──[/]", "[error] ✖ gamma[/]    ", Spaces(12)],
            Markup(_run.Tasks, "alpha", 12));
        Assert.All(Plain(_run.Tasks, "alpha", 0), line => Assert.Equal("", line));
    }

    [Fact]
    public void Without_tasks_the_graph_has_empty_lines()
    {
        var layout = GraphLayout.Build([]);

        Assert.Equal(["", ""], GraphText.Lines(layout, [], null, Wide));
        Assert.Equal("No plan yet", GraphText.NoPlan);
    }

    [Fact]
    public void Detail_of_alpha()
    {
        Assert.Equal(
        [
            "alpha - Alpha parser",
            "deps: -",
            "dependents: ▶ beta",
            "owns: src/Alpha/**, tests/Alpha/**",
            "overlaps: -",
            "state: Done · 1 attempt · 0.25 USD",
            "detail: 0.25 USD, 1 attempt(s)",
            "sessions: 2",
        ], GraphText.Detail(Task("alpha"), _run));
    }

    [Fact]
    public void Detail_of_epsilon()
    {
        Assert.Equal(
        [
            "epsilon - Epsilon command line",
            "deps: ▶ beta",
            "dependents: -",
            "owns: src/Epsilon/**",
            "overlaps: -",
            "state: Pending · 0 attempts · 0.00 USD",
            "detail: waiting for beta",
            "sessions: 0",
        ], GraphText.Detail(Task("epsilon"), _run));
    }

    [Fact]
    public void Detail_counts_the_sessions_and_lists_several_deps_with_their_icons()
    {
        var delta = Task("delta") with { Deps = ["alpha", "gamma"] };
        var run = _run with { Tasks = [.. _run.Tasks.Select(t => t.Id == "delta" ? delta : t)] };

        Assert.Equal("deps: ✔ alpha, ✖ gamma", GraphText.Detail(delta, run)[1]);
        Assert.Equal("dependents: ▶ beta, ⊘ delta", GraphText.Detail(Task("alpha"), run)[2]);
        Assert.Equal("state: Failed · 3 attempts · 0.62 USD", GraphText.Detail(Task("gamma"), run)[5]);
        Assert.Equal("sessions: 2", GraphText.Detail(Task("gamma"), run)[7]);
        Assert.Equal("sessions: 1", GraphText.Detail(Task("beta"), run)[7]);
    }

    [Fact]
    public void A_task_with_empty_owns_overlaps_every_other_task()
    {
        var epsilon = Task("epsilon") with { Owns = [] };
        var run = _run with { Tasks = [.. _run.Tasks.Select(t => t.Id == "epsilon" ? epsilon : t)] };

        Assert.Equal("owns: -", GraphText.Detail(epsilon, run)[3]);
        Assert.Equal("overlaps: alpha, gamma, beta, delta", GraphText.Detail(epsilon, run)[4]);
        Assert.Equal("overlaps: epsilon", GraphText.Detail(Task("alpha"), run)[4]);
    }

    [Fact]
    public void Overlaps_follow_the_owns_prefixes()
    {
        var delta = Task("delta") with { Owns = ["src/Beta/Report/**", "tests/Alpha/Delta*.cs"] };
        var run = _run with { Tasks = [.. _run.Tasks.Select(t => t.Id == "delta" ? delta : t)] };

        Assert.Equal("overlaps: alpha, beta", GraphText.Detail(delta, run)[4]);
    }
}

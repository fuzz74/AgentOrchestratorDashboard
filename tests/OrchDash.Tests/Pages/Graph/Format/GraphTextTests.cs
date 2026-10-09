using System.Collections.Immutable;
using OrchDash.Contracts;
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

    // 40.1: the child lines of the sub-agent sample; the longest is 25 cells, so every column is 25 cells wide.
    private const string AlphaSub1 = "  ├✔ Survey the parser m…";
    private const string AlphaSub2 = "  └✖ Check the public AP…";
    private const string BetaSub = "  └▶ Survey CLI flags";
    private const int SubWidth = 25;

    private readonly RunSnapshot _subRun = SampleRun.CreateSubAgents();

    private static string Pad(string text) => text.PadRight(SubWidth);

    private static ImmutableArray<string> SubPlain(RunSnapshot run, ImmutableArray<TaskView> tasks, string? selectedId)
    {
        var rows = GraphText.ChildRows(run);
        return GraphText.PlainLines(GraphText.Layout(tasks, rows), tasks, selectedId, Wide, rows);
    }

    private static ImmutableArray<string> SubMarkup(RunSnapshot run, ImmutableArray<TaskView> tasks, string? selectedId)
    {
        var rows = GraphText.ChildRows(run);
        return GraphText.Lines(GraphText.Layout(tasks, rows), tasks, selectedId, Wide, rows);
    }

    private Session SubSession(string key) => _subRun.Sessions.Single(s => s.Files.Key == key);

    [Fact]
    public void ChildRows_are_the_agent_tree_rows_of_each_task_with_sub_agents()
    {
        var rows = GraphText.ChildRows(_subRun);

        Assert.Equal(["alpha", "beta"], rows.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([SampleRun.AlphaSub1Id, SampleRun.AlphaSub2Id], rows["alpha"].Select(r => r.SubAgent.Id));
        Assert.Equal(["├", "└"], rows["alpha"].Select(r => r.Prefix));
        Assert.All(rows["alpha"], r => Assert.Equal(SampleRun.AlphaWorkerKey, r.Session.Files.Key));
        Assert.Equal([SampleRun.BetaSub1Id], rows["beta"].Select(r => r.SubAgent.Id));
        Assert.Equal(["└"], rows["beta"].Select(r => r.Prefix));
        Assert.Empty(GraphText.ChildRows(_run));
        Assert.Empty(GraphText.ChildRows(SampleRun.Create()));
    }

    [Fact]
    public void Each_card_is_followed_by_its_child_lines_and_the_later_cards_move_down()
    {
        Assert.Equal(
        [
            "W1" + Spaces(26) + "W2" + Spaces(26) + "W3" + Spaces(23),
            Pad("●✔ alpha") + "───" + Pad("▸▶ beta") + Gap + Pad("▸· epsilon"),
            AlphaSub1 + Gap + Pad(BetaSub) + Gap + Spaces(SubWidth),
            AlphaSub2 + Gap + Pad(" ⊘ delta") + Gap + Spaces(SubWidth),
            Pad(" ✖ gamma") + Gap + Spaces(SubWidth) + Gap + Spaces(SubWidth),
            Spaces(81),
        ], SubPlain(_subRun, _subRun.Tasks, "alpha"));
    }

    [Fact]
    public void Child_lines_are_in_the_colour_of_their_state()
    {
        var markup = SubMarkup(_subRun, _subRun.Tasks, "alpha");

        Assert.Equal("[success]  ├✔ Survey the parser m…[/]   [primary]  └▶ Survey CLI flags[/]" + Spaces(32), markup[2]);
        Assert.Equal("[error]  └✖ Check the public AP…[/]   [warning] ⊘ delta[/]" + Spaces(45), markup[3]);
    }

    [Fact]
    public void A_running_sub_agent_of_a_finished_session_reads_aborted()
    {
        var worker = SubSession(SampleRun.AlphaWorkerKey);
        var sub = worker.Content.SubAgents[0] with { State = SessionState.Running, FinishedAt = null };
        var row = new AgentRow(worker, sub, 1, "└");
        var rows = new Dictionary<string, ImmutableArray<AgentRow>> { ["alpha"] = [row] };

        Assert.Equal("  └◌ Survey the parser m…", GraphText.ChildLine(row));
        Assert.StartsWith("[muted]  └◌ Survey the parser m…[/]",
            GraphText.Lines(GraphText.Layout(_run.Tasks, rows), _run.Tasks, null, Wide, rows)[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Survey CLI flags", "  └▶ Survey CLI flags")]
    [InlineData("Survey the CLI flags", "  └▶ Survey the CLI flags")]
    [InlineData("Survey the CLI flags.", "  └▶ Survey the CLI flag…")]
    [InlineData("Check the public API surface of…", "  └▶ Check the public AP…")]
    public void A_name_longer_than_20_characters_is_cut_to_19_and_an_ellipsis(string name, string line)
    {
        var worker = SubSession(SampleRun.BetaWorkerKey);
        var sub = worker.Content.SubAgents[0] with { Name = name };

        Assert.Equal(line, GraphText.ChildLine(new AgentRow(worker, sub, 1, "└")));
    }

    [Fact]
    public void The_columns_fit_the_longest_card_or_child_line()
    {
        var layout = GraphText.Layout(_subRun.Tasks, GraphText.ChildRows(_subRun));
        Assert.Equal(SubWidth, layout.Width);
        Assert.Equal(AlphaSub1.Length, layout.Width);
        Assert.Equal(5, layout.BusRow);
        Assert.Equal(6, layout.Height);

        var worker = SubSession(SampleRun.BetaWorkerKey);
        var row = new AgentRow(worker, worker.Content.SubAgents[0] with { Name = "List" }, 1, "└");
        var rows = new Dictionary<string, ImmutableArray<AgentRow>> { ["beta"] = [row] };
        var narrow = GraphText.Layout(_run.Tasks, rows);
        Assert.Equal(10, narrow.Width);
        Assert.Equal(" ✖ gamma  " + Gap + "  └▶ List " + Gap + NoCard, GraphText.PlainLines(narrow, _run.Tasks, null, Wide, rows)[2]);
    }

    [Fact]
    public void Edges_leave_and_enter_the_card_lines_beside_the_child_lines()
    {
        Assert.Equal(
        [
            "W1" + Spaces(26) + "W2" + Spaces(26) + "W3" + Spaces(23),
            Pad(" ✔ alpha") + Gap + Pad(" ▶ beta") + Gap + Pad(" · epsilon"),
            AlphaSub1 + Gap + Pad(BetaSub) + Gap + Spaces(SubWidth),
            AlphaSub2 + " ┌─" + Pad("▸⊘ delta") + Gap + Spaces(SubWidth),
            Pad("●✖ gamma") + "─┘ " + Spaces(SubWidth) + Gap + Spaces(SubWidth),
            Spaces(81),
        ], SubPlain(_subRun, _subRun.Tasks, "gamma"));
    }

    [Fact]
    public void An_edge_across_a_column_runs_along_the_bus_row_below_the_child_lines()
    {
        Assert.Equal(
        [
            "W1" + Spaces(26) + "W2" + Spaces(26) + "W3" + Spaces(23),
            Pad("◂✔ alpha") + "─┐ " + Pad(" ▶ beta") + " ┌─" + Pad("●· epsilon"),
            AlphaSub1 + " │ " + Pad(BetaSub) + " │ " + Spaces(SubWidth),
            AlphaSub2 + " │ " + Pad(" ⊘ delta") + " │ " + Spaces(SubWidth),
            Pad(" ✖ gamma") + " │ " + Spaces(SubWidth) + " │ " + Spaces(SubWidth),
            Spaces(26) + "└" + new string('─', 27) + "┘" + Spaces(26),
        ], SubPlain(_subRun, BusGraph(), "epsilon"));
    }

    [Fact]
    public void Without_sub_agents_the_graph_is_drawn_as_before()
    {
        foreach (var run in new[] { SampleRun.Create(), _run })
        {
            var layout = GraphText.Layout(run.Tasks, GraphText.ChildRows(run));
            Assert.Equal(GraphLayout.Build(run.Tasks).Width, layout.Width);
            Assert.Equal(GraphLayout.Build(run.Tasks).Height, layout.Height);
            Assert.Equal(Plain(run.Tasks, "alpha"), SubPlain(run, run.Tasks, "alpha"));
            Assert.Equal(Markup(run.Tasks, "beta"), SubMarkup(run, run.Tasks, "beta"));
        }
        // Without the child rows argument, the existing calls draw no child lines even for a run with sub-agents.
        Assert.Equal(Plain(_run.Tasks, "alpha"), Plain(_subRun.Tasks, "alpha"));
    }

    [Fact]
    public void Detail_counts_the_sub_agents_of_the_task_and_those_running()
    {
        Assert.Equal("sessions: 2 · sub-agents: 2 (0 running)", GraphText.Detail(Task("alpha"), _subRun)[7]);
        Assert.Equal("sessions: 1 · sub-agents: 1 (1 running)", GraphText.Detail(Task("beta"), _subRun)[7]);
        Assert.Equal("sessions: 2", GraphText.Detail(Task("gamma"), _subRun)[7]);
        Assert.Equal("sessions: 0", GraphText.Detail(Task("epsilon"), _subRun)[7]);

        var finished = _subRun with
        {
            Sessions = [.. _subRun.Sessions.Select(s => s.Files.Key == SampleRun.BetaWorkerKey ? s with { State = SessionState.Succeeded } : s)],
        };
        Assert.Equal("sessions: 1 · sub-agents: 1 (0 running)", GraphText.Detail(Task("beta"), finished)[7]);
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

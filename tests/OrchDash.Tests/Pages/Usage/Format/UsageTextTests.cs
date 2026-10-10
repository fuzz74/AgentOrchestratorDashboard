using System.Text.RegularExpressions;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Usage.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.Usage.Format;

public sealed class UsageTextTests
{
    private readonly RunSnapshot _run = SampleRun.CreateEnriched();
    private readonly RunSnapshot _subRun = SampleRun.CreateSubAgents();

    private Session AlphaWorker => Session(SampleRun.AlphaWorkerKey);
    private Session AlphaReview => Session(SampleRun.AlphaReviewKey);
    private Session BetaWorker => Session(SampleRun.BetaWorkerKey);
    private Session GammaWorker1 => Session(SampleRun.GammaWorker1Key);

    private Session Session(string key) => _run.Sessions.Single(s => s.Files.Key == key);

    // A session of CreateSubAgents().
    private Session SubSession(string key) => _subRun.Sessions.Single(s => s.Files.Key == key);

    [Fact]
    public void Run_panel_shows_the_figures_summed_over_all_sessions()
    {
        Assert.Equal(
        [
            "Sessions: 5",
            "Calls: 10",
            "Input: 3.7k",
            "Cache read: 207.2k",
            "Cache write: 49.7k",
            "Output: 9.4k",
            "Thinking: 1.9k",
            "Cost: 0.87 USD",
            "Premium requests: 1",
            "AIU: 3.99 AIU",
            "Lines: +12 -3",
        ], UsageText.RunPanel(_run));
    }

    [Fact]
    public void Run_panel_without_sessions_shows_dashes()
    {
        Assert.Equal(
        [
            "Sessions: 0",
            "Calls: 0",
            "Input: -",
            "Cache read: -",
            "Cache write: -",
            "Output: -",
            "Thinking: -",
            "Cost: -",
            "Premium requests: -",
            "AIU: -",
            "Lines: -",
        ], UsageText.RunPanel(RunSnapshot.Empty(SampleRun.RepoPath)));
    }

    [Fact]
    public void Run_panel_shows_premium_requests_with_at_most_two_decimals()
    {
        var review = AlphaReview with
        {
            Content = AlphaReview.Content with { Result = AlphaReview.Content.Result! with { PremiumRequests = 1.3333 } },
        };

        Assert.Contains("Premium requests: 1.33", UsageText.RunPanel(UsageRulesTests.Replace(_run, review)));
    }

    [Fact]
    public void Rate_limit_line_shows_the_latest_rate_limit()
    {
        Assert.Equal("Rate limits: 5-hour 20 %, resets 17:00 · 7-day 27 %, resets 2026-10-07 09:00 · [success]allowed[/]",
            UsageText.RateLimitLine(_run));

        var later = new RateLimit("allowed_warning", "seven_day",
            0.36, SampleRun.At(17, 0, 0), 0.9, null, SampleRun.At(12, 10, 6));
        var run = UsageRulesTests.Replace(_run, BetaWorker with { Content = BetaWorker.Content with { RateLimit = later } });

        Assert.Equal("Rate limits: 5-hour 36 %, resets 17:00 · 7-day 90 % · [warning]allowed_warning[/]",
            UsageText.RateLimitLine(run));
    }

    [Fact]
    public void Rate_limit_line_without_rate_limit_is_a_dash()
    {
        Assert.Equal("Rate limits: -", UsageText.RateLimitLine(SampleRun.Create()));
    }

    [Fact]
    public void Versions_line_of_the_sample_warns_about_the_claude_version_only()
    {
        Assert.Equal(
            $"Versions: [warning]Claude Code 2.1.3 (made for {TestedVersions.ClaudeCode})[/] · Copilot CLI 1.0.91 (made for {TestedVersions.CopilotCli})",
            UsageText.VersionsLine(_run));
    }

    [Fact]
    public void Versions_line_shows_unknown_and_several_versions()
    {
        var review = AlphaReview with { Stores = StoreData.Empty };
        var gamma1 = GammaWorker1 with
        {
            Content = GammaWorker1.Content with { Init = null },
            Stores = GammaWorker1.Stores with { CliVersion = TestedVersions.ClaudeCode },
        };

        Assert.Equal(
            $"Versions: [warning]Claude Code 2.1.3, {TestedVersions.ClaudeCode} (made for {TestedVersions.ClaudeCode})[/] · Copilot CLI unknown (made for {TestedVersions.CopilotCli})",
            UsageText.VersionsLine(UsageRulesTests.Replace(_run, review, gamma1)));
    }

    [Fact]
    public void Versions_line_leaves_out_a_provider_without_sessions_and_warns_only_when_a_version_differs()
    {
        var tested = _run with
        {
            Sessions =
            [
                .. _run.Sessions
                    .Where(s => s.Provider == Provider.Claude)
                    .Select(s => s with { Content = s.Content with { Init = null }, Stores = s.Stores with { CliVersion = TestedVersions.ClaudeCode } }),
            ],
        };

        Assert.Equal($"Versions: Claude Code {TestedVersions.ClaudeCode} (made for {TestedVersions.ClaudeCode})",
            UsageText.VersionsLine(tested));
        Assert.Equal("Versions: -", UsageText.VersionsLine(RunSnapshot.Empty(SampleRun.RepoPath)));
    }

    [Fact]
    public void Group_header_and_rows()
    {
        var groups = UsageRules.Groups(_run);
        var width = UsageText.GroupNameWidth(groups);

        Assert.Equal(5, width);
        Assert.Equal(
            "[muted]Group  Sess  Calls    Input   C.read  C.write   Output       Cost   Prem        AIU        Lines[/]",
            UsageText.GroupHeader(width));
        Assert.Equal(
            "alpha     2      4     2.0k    70.2k    21.4k     4.3k   0.25 USD      1   3.99 AIU       +12 -3",
            UsageText.GroupRow(groups[0], width));
        Assert.Equal(
            "gamma     2      4       10    89.2k    24.5k     5.1k   0.62 USD      -          -            -",
            UsageText.GroupRow(groups[1], width));
    }

    [Fact]
    public void Group_names_are_escaped_and_cut_to_the_widest_name_column()
    {
        var group = new UsageGroup("[long]-task-name-here", [], UsageRules.Sum([]));

        Assert.Equal(UsageText.MaxGroupNameWidth, UsageText.GroupNameWidth([group]));
        Assert.StartsWith("[[long]]-task...  ",UsageText.GroupRow(group, UsageText.GroupNameWidth([group])));
    }

    [Fact]
    public void Group_rows_and_session_rows_fit_the_test_frame()
    {
        var group = new UsageGroup(new string('g', 30), [], UsageRules.Groups(_run)[0].Figures);
        var width = UsageText.GroupNameWidth([group]);

        Assert.InRange(Plain(UsageText.GroupRow(group, width)).Length, 0, 105);
        Assert.Equal(Plain(UsageText.GroupHeader(width)).Length, Plain(UsageText.GroupRow(group, width)).Length);
        Assert.All(_run.Sessions, s => Assert.InRange(Plain(UsageText.SessionRow(_run, s)).Length, 0, 150));
        Assert.Equal(Plain(UsageText.SessionHeader()).Length, Plain(UsageText.SessionRow(_run, AlphaWorker)).Length);
    }

    [Fact]
    public void Bar_items_are_the_tokens_per_group()
    {
        Assert.Equal(
        [
            new UsageBar("alpha", 97_965),
            new UsageBar("gamma", 118_830),
            new UsageBar("beta", 53_300),
        ], UsageText.BarItems(UsageRules.Groups(_run)));
    }

    [Fact]
    public void Session_header_and_rows()
    {
        Assert.Equal(
            "[muted]   Role       Attempt   Model                 Calls     Peak    Input   C.read  C.write   Output       Cost        AIU        Lines[/]",
            UsageText.SessionHeader());
        Assert.Equal(
            "[success]✔[/]  [cyan]worker   [/]  #1        claude-sonnet-4-5         2    43.3k     2.0k    56.0k     4.5k     3.1k   0.25 USD          -       +12 -3",
            UsageText.SessionRow(_run, AlphaWorker));
        Assert.Equal(
            "[primary]▶[/]  [cyan]worker   [/]  #1        claude-sonnet-4-5         2    34.5k     1.7k    47.8k     3.8k        -          -          -            -",
            UsageText.SessionRow(_run, BetaWorker));
    }

    [Fact]
    public void Session_row_of_a_reviewer_shows_its_review_try_and_aiu()
    {
        var row = UsageText.SessionRow(_run, AlphaReview);

        Assert.StartsWith("[success]✔[/]  [yellow]reviewer [/]  #1.1      gpt-5.1   ", row);
        Assert.EndsWith("          -   3.99 AIU        +0 -0", row);
    }

    [Fact]
    public void Pop_up_of_the_alpha_worker()
    {
        Assert.Equal("Usage: alpha worker #1", UsageText.SessionPopupTitle(AlphaWorker));
        Assert.Equal(
        [
            new PopupSection("Totals", """
                calls: 2
                input: 2.0k
                cache read: 56.0k
                cache write: 4.5k
                output: 3.1k
                thinking: 1.4k
                peak context: 43.3k of 200.0k (22 %)
                cost: 0.25 USD
                premium requests: -
                AIU: -
                lines: +12 -3
                """.ReplaceLineEndings("\n")),
            new PopupSection("Calls", """
                call 1 · 12:00:10 · claude-sonnet-4-5 · input 1.2k · cache read 15.0k · cache write 3.0k · output 640 · thinking 210 · AIU - · duration - · stop tool_use
                call 2 · 12:03:00 · claude-sonnet-4-5 · input 800 · cache read 41.0k · cache write 1.5k · output 2.5k · thinking 1.2k · AIU - · duration - · stop end_turn
                """.ReplaceLineEndings("\n")),
        ], UsageText.SessionPopup(_run, AlphaWorker));
    }

    [Fact]
    public void Pop_up_of_the_alpha_review_shows_aiu_and_duration_per_call()
    {
        var calls = UsageText.SessionPopup(_run, AlphaReview).Single(s => s.Heading == "Calls").Text.Split('\n');

        Assert.Equal("Usage: alpha reviewer #1.1", UsageText.SessionPopupTitle(AlphaReview));
        Assert.Equal(
            "call 1 · 12:06:30 · gpt-5.1 · input 20 · cache read 0 · cache write 14.2k · output 310 · thinking 96 · 1.85 AIU · duration 4s · stop tool_calls",
            calls[0]);
        Assert.EndsWith("· 2.14 AIU · duration 11s · stop stop", calls[1]);
    }

    [Fact]
    public void Pop_up_of_the_running_beta_worker_lists_what_is_unavailable()
    {
        Assert.Equal("Usage: beta worker #1", UsageText.SessionPopupTitle(BetaWorker));
        Assert.Equal(
        [
            new PopupSection("Totals", """
                calls: 2
                input: 1.7k
                cache read: 47.8k
                cache write: 3.8k
                output: -
                thinking: -
                peak context: 34.5k of 200.0k (17 %)
                cost: -
                premium requests: -
                AIU: -
                lines: -
                """.ReplaceLineEndings("\n")),
            new PopupSection("Calls", """
                call 1 · 12:10:05 · claude-sonnet-4-5 · input 1.1k · cache read 14.8k · cache write 2.9k · output - · thinking - · AIU - · duration - · stop -
                call 2 · 12:20:00 · claude-sonnet-4-5 · input 600 · cache read 33.0k · cache write 900 · output - · thinking - · AIU - · duration - · stop -
                """.ReplaceLineEndings("\n")),
            new PopupSection("Unavailable", "no transcript"),
        ], UsageText.SessionPopup(_run, BetaWorker));
    }

    [Fact]
    public void Run_panel_with_sub_agents_ends_with_their_number()
    {
        var lines = UsageText.RunPanel(_subRun);

        Assert.Equal(12, lines.Count);
        Assert.Equal(["Sessions: 6", "Calls: 19"], lines.Take(2));
        Assert.Equal("Lines: +12 -3", lines[10]);
        Assert.Equal("sub-agents 6", lines[11]);
    }

    [Fact]
    public void With_sub_agents_the_group_table_has_the_sub_column()
    {
        var groups = UsageRules.Groups(_subRun);
        var width = UsageText.GroupNameWidth(groups);

        Assert.Equal(
            "[muted]Group    Sess  Calls    Input   C.read  C.write   Output       Cost   Prem        AIU        Lines         Sub[/]",
            UsageText.GroupHeader(width, subAgents: true));
        Assert.Equal(
            "alpha       2      7     2.0k    74.2k    36.6k     4.3k   0.25 USD      1   3.99 AIU       +12 -3    2 · 17 %",
            UsageText.GroupRow(groups[1], width, subAgents: true));
        Assert.Equal(UsageText.GroupRow(groups[0], width) + "    3 · 40 %", UsageText.GroupRow(groups[0], width, subAgents: true));
        Assert.Equal(UsageText.GroupRow(groups[2], width) + "           -", UsageText.GroupRow(groups[2], width, subAgents: true));
        Assert.Equal(UsageText.GroupRow(groups[3], width) + "     1 · 6 %", UsageText.GroupRow(groups[3], width, subAgents: true));
        Assert.All(groups, g =>
            Assert.Equal(Plain(UsageText.GroupHeader(width, subAgents: true)).Length, Plain(UsageText.GroupRow(g, width, subAgents: true)).Length));
    }

    [Fact]
    public void Sub_column_of_a_group_without_tokens_has_no_share()
    {
        var group = new UsageGroup("alpha", [], UsageRules.Sum([])) { SubAgentCount = 2 };

        Assert.EndsWith("-       2 · -", UsageText.GroupRow(group, 5, subAgents: true));
    }

    [Fact]
    public void Bar_items_with_sub_agents_add_a_bar_under_each_group_that_has_any()
    {
        Assert.Equal(
        [
            new UsageBar("planner", 48_418),
            new UsageBar("└ sub-agents", 19_413),
            new UsageBar("alpha", 117_123),
            new UsageBar("└ sub-agents", 19_538),
            new UsageBar("gamma", 118_830),
            new UsageBar("beta", 56_903),
            new UsageBar("└ sub-agents", 3_603),
        ], UsageText.BarItems(UsageRules.Groups(_subRun)));
    }

    [Fact]
    public void With_sub_agents_the_attempt_column_widens_so_that_role_and_attempt_hold_a_child_row()
    {
        Assert.Equal(
            "[muted]   Role       Attempt                   Model                 Calls     Peak    Input   C.read  C.write   Output       Cost        AIU        Lines[/]",
            UsageText.SessionHeader(subAgents: true));
        Assert.Equal(
            "[success]✔[/]  [cyan]worker   [/]  #1                        claude-sonnet-4-5         5    43.3k     2.0k    60.0k    19.7k     3.1k   0.25 USD          -       +12 -3",
            UsageText.SessionRow(_subRun, SubSession(SampleRun.AlphaWorkerKey)));
    }

    [Fact]
    public void Child_rows_show_the_tree_the_model_and_the_figures_of_the_sub_agents_calls()
    {
        var rows = AgentTree.Rows([SubSession(SampleRun.AlphaWorkerKey)]);

        Assert.Equal(
            "   [success]├✔ Survey the parser module        [/]  claude-haiku-4-5          2     5.4k        5     4.0k     5.4k      380          -          -            -",
            UsageText.ChildRow(rows[0]));
        Assert.Equal(
            "   [error]└✖ Check the public API surface of…[/]  claude-sonnet-4-5         1     9.8k        3        0     9.8k        -          -          -            -",
            UsageText.ChildRow(rows[1]));
    }

    [Fact]
    public void Child_rows_of_nested_sub_agents_carry_the_tree_prefix_and_their_aiu()
    {
        var rows = AgentTree.Rows([SubSession(SampleRun.PlannerKey)]).Select(UsageText.ChildRow).ToList();

        Assert.StartsWith("   [success]├✔ Map the repo ", rows[0]);
        Assert.StartsWith("   [success]│ └✔ Read the spec ", rows[1]);
        Assert.StartsWith("   [success]└✔ Survey the tests ", rows[2]);
        Assert.EndsWith("  gpt-5.6-luna              1     5.9k       10        0     5.9k      180          -   0.27 AIU            -", rows[1]);
    }

    [Fact]
    public void A_child_row_is_cut_to_the_role_and_attempt_columns()
    {
        var planner = SubSession(SampleRun.PlannerKey);
        var content = planner.Content with
        {
            SubAgents =
            [
                .. planner.Content.SubAgents.Select(s => s.Id == SampleRun.PlannerSub2Id ? s with { Name = "Read the whole spec and list all" } : s),
            ],
        };

        var row = UsageText.ChildRow(AgentTree.Rows([planner with { Content = content }])[1]);

        Assert.StartsWith("   [success]│ └✔ Read the whole spec and lis...[/]  gpt-5.6-luna  ", row);
    }

    [Fact]
    public void A_running_sub_agent_of_a_session_that_no_longer_runs_shows_as_aborted()
    {
        var beta = SubSession(SampleRun.BetaWorkerKey);

        Assert.StartsWith("   [primary]└▶ Survey CLI flags ", UsageText.ChildRow(AgentTree.Rows([beta])[0]));
        Assert.StartsWith("   [muted]└◌ Survey CLI flags ", UsageText.ChildRow(AgentTree.Rows([beta with { State = SessionState.Failed }])[0]));
    }

    [Fact]
    public void Child_rows_are_escaped()
    {
        var beta = SubSession(SampleRun.BetaWorkerKey);
        var content = beta.Content with { SubAgents = [beta.Content.SubAgents[0] with { Name = "[red]flags", Model = "[m]" }] };

        var row = UsageText.ChildRow(AgentTree.Rows([beta with { Content = content }])[0]);

        Assert.StartsWith("   [primary]└▶ [[red]]flags ", row);
        Assert.Contains("  [[m]]   ", row);
    }

    [Fact]
    public void With_sub_agents_session_rows_and_child_rows_align_with_the_header()
    {
        var width = Plain(UsageText.SessionHeader(subAgents: true)).Length;

        Assert.Equal(Plain(UsageText.SessionHeader()).Length + 16, width);   // the attempt column: 24 instead of 8
        Assert.All(_subRun.Sessions, s => Assert.Equal(width, Plain(UsageText.SessionRow(_subRun, s)).Length));
        Assert.All(AgentTree.Rows(_subRun.Sessions), row => Assert.Equal(width, Plain(UsageText.ChildRow(row)).Length));
    }

    [Fact]
    public void Pop_up_of_a_session_with_sub_agents_lists_its_own_calls_then_each_sub_agents_calls()
    {
        Assert.Equal(
        [
            new PopupSection("Totals", """
                calls: 5
                input: 2.0k
                cache read: 60.0k
                cache write: 19.7k
                output: 3.1k
                thinking: 1.4k
                peak context: 43.3k of 200.0k (22 %)
                cost: 0.25 USD
                premium requests: -
                AIU: -
                lines: +12 -3
                """.ReplaceLineEndings("\n")),
            new PopupSection("Calls", """
                call 1 · 12:00:10 · claude-sonnet-4-5 · input 1.2k · cache read 15.0k · cache write 3.0k · output 640 · thinking 210 · AIU - · duration - · stop tool_use
                call 2 · 12:03:00 · claude-sonnet-4-5 · input 800 · cache read 41.0k · cache write 1.5k · output 2.5k · thinking 1.2k · AIU - · duration - · stop end_turn
                """.ReplaceLineEndings("\n")),
            new PopupSection("Sub-agent Survey the parser module", """
                call 1 · 12:00:19 · claude-haiku-4-5 · input 3 · cache read 0 · cache write 4.0k · output 120 · thinking - · AIU - · duration - · stop tool_use
                call 2 · 12:00:50 · claude-haiku-4-5 · input 2 · cache read 4.0k · cache write 1.4k · output 260 · thinking - · AIU - · duration - · stop end_turn
                """.ReplaceLineEndings("\n")),
            new PopupSection("Sub-agent Check the public API surface of…",
                "call 1 · 12:03:09 · claude-sonnet-4-5 · input 3 · cache read 0 · cache write 9.8k · output - · thinking - · AIU - · duration - · stop tool_use"),
        ], UsageText.SessionPopup(_subRun, SubSession(SampleRun.AlphaWorkerKey)));
    }

    [Fact]
    public void Pop_up_of_the_planner_numbers_the_calls_within_each_section_in_tree_order()
    {
        var sections = UsageText.SessionPopup(_subRun, SubSession(SampleRun.PlannerKey));

        Assert.Equal(["Totals", "Calls", "Sub-agent Map the repo", "Sub-agent Read the spec", "Sub-agent Survey the tests"],
            sections.Select(s => s.Heading));
        Assert.Equal(
            "call 2 · 11:59:54 · gpt-5.6-luna · input 25 · cache read 12.4k · cache write 3.4k · output 420 · thinking 260 · 1.91 AIU · duration 4s · stop stop",
            sections[1].Text.Split('\n')[1]);
        Assert.Equal(
            "call 1 · 11:59:12 · gpt-5.6-luna · input 10 · cache read 0 · cache write 5.9k · output 180 · thinking 48 · 0.27 AIU · duration 2s · stop tool_calls",
            sections[3].Text);
    }

    [Fact]
    public void Pop_up_of_the_running_beta_worker_lists_its_sub_agent_before_what_is_unavailable()
    {
        var sections = UsageText.SessionPopup(_subRun, SubSession(SampleRun.BetaWorkerKey));

        Assert.Equal(["Totals", "Calls", "Sub-agent Survey CLI flags", "Unavailable"], sections.Select(s => s.Heading));
        Assert.Equal(2, sections[1].Text.Split('\n').Length);
    }

    [Fact]
    public void Pop_up_of_a_sub_agent_has_its_path_its_totals_and_its_calls()
    {
        var worker = SubSession(SampleRun.AlphaWorkerKey);

        Assert.Equal("Usage: alpha worker #1 › Survey the parser module", UsageText.SubAgentPopupTitle(worker, SampleRun.AlphaSub1Id));
        Assert.Equal(
        [
            new PopupSection("Totals", """
                calls: 2
                input: 5
                cache read: 4.0k
                cache write: 5.4k
                output: 380
                thinking: -
                peak context: 5.4k
                AIU: -
                """.ReplaceLineEndings("\n")),
            new PopupSection("Calls", """
                call 1 · 12:00:19 · claude-haiku-4-5 · input 3 · cache read 0 · cache write 4.0k · output 120 · thinking - · AIU - · duration - · stop tool_use
                call 2 · 12:00:50 · claude-haiku-4-5 · input 2 · cache read 4.0k · cache write 1.4k · output 260 · thinking - · AIU - · duration - · stop end_turn
                """.ReplaceLineEndings("\n")),
        ], UsageText.SubAgentPopup(_subRun, worker, SampleRun.AlphaSub1Id));
    }

    [Fact]
    public void Pop_up_of_a_nested_sub_agent_has_its_whole_path_and_the_context_window_of_its_model()
    {
        var planner = SubSession(SampleRun.PlannerKey);
        var windowed = planner with
        {
            Content = planner.Content with { Result = planner.Content.Result! with { ContextWindow = 400_000 } },
        };

        var totals = UsageText.SubAgentPopup(_subRun, planner, SampleRun.PlannerSub2Id)[0].Text.Split('\n');
        var withWindow = UsageText.SubAgentPopup(UsageRulesTests.Replace(_subRun, windowed), windowed, SampleRun.PlannerSub2Id)[0].Text.Split('\n');

        Assert.Equal("Usage: planner #1 › Map the repo › Read the spec", UsageText.SubAgentPopupTitle(planner, SampleRun.PlannerSub2Id));
        Assert.Contains("peak context: 5.9k", totals);
        Assert.Contains("AIU: 0.27 AIU", totals);
        Assert.Contains("peak context: 5.9k of 400.0k (1 %)", withWindow);
    }

    // The visible text of a markup line.
    private static string Plain(string markup) =>
        Regex.Replace(markup, @"\[(?:[a-z]+|/)\]", "")
            .Replace("[[", "[", StringComparison.Ordinal)
            .Replace("]]", "]", StringComparison.Ordinal);
}

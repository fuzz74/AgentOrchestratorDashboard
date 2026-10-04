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

    private Session AlphaWorker => Session(SampleRun.AlphaWorkerKey);
    private Session AlphaReview => Session(SampleRun.AlphaReviewKey);
    private Session BetaWorker => Session(SampleRun.BetaWorkerKey);
    private Session GammaWorker1 => Session(SampleRun.GammaWorker1Key);

    private Session Session(string key) => _run.Sessions.Single(s => s.Files.Key == key);

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

    // The visible text of a markup line.
    private static string Plain(string markup) =>
        Regex.Replace(markup, @"\[(?:[a-z]+|/)\]", "")
            .Replace("[[", "[", StringComparison.Ordinal)
            .Replace("]]", "]", StringComparison.Ordinal);
}
